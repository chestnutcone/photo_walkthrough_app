using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vela.Api.Account;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Account.AccountKit;

namespace Vela.Api.Tests.Account;

[Collection(PostgresCollection.Name)]
public class DeletionTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    private readonly RecordingCdnPurger _cdn = new();
    private readonly RecordingAppleRevoker _apple = new();

    private async Task<(ProfileTestHost Host, string Cs)> Host(Action<IServiceCollection>? extra = null)
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var h = NewHost(pg, cs, _cdn, _apple, extra);
        _hosts.Add(h);
        return (h, cs);
    }

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    [Fact]
    public async Task Deleted_user_cannot_sign_in_or_refresh_immediately()
    {
        var (h, _) = await Host();
        var client = h.Anonymous();
        var sub = "del-" + Guid.NewGuid();
        var sso = await client.PostAsJsonAsync("/v1/auth/sso", new { provider = "fake", idToken = $"fake:{sub}", nonce = "n" });
        var tokens = JsonDocument.Parse(await sso.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        var access = tokens.GetProperty("accessToken").GetString()!;
        var refresh = tokens.GetProperty("refreshToken").GetString()!;
        var me = new TestUser(h.Anonymous(), Guid.Empty);
        me.Client.DefaultRequestHeaders.Authorization = new("Bearer", access);

        var r = await me.Delete("/v1/me");

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.True(r.Body.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, r.Body.GetProperty("data").ValueKind);
        var refreshed = await client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
        var again = await client.PostAsJsonAsync("/v1/auth/sso", new { provider = "fake", idToken = $"fake:{sub}", nonce = "n" });
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Contains("account_deleted", await again.Content.ReadAsStringAsync());
        await using var db = h.Db();
        Assert.Equal(h.Clock.GetUtcNow(), (await db.Users.SingleAsync()).DeletedAt);
        Assert.All(await db.RefreshTokens.ToListAsync(), t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task Requesting_deletion_twice_is_idempotent_and_keeps_the_first_timestamp()
    {
        var (h, _) = await Host();
        var u = await h.SignIn();
        await RequestDelete(u);
        h.Clock.Advance(TimeSpan.FromMinutes(5));

        // The old access token no longer passes (403 account_deleted), so repeat the request at the service level.
        Assert.Equal("account_deleted", (await RequestDelete(u)).ErrorCode);
        using (var scope = h.Scope())
            await scope.ServiceProvider.GetRequiredService<Vela.Api.Account.IAccountDeletionService>().RequestAsync(u.Id);

        await using var db = h.Db();
        Assert.Equal(ProfileTestHost.Now, (await db.Users.SingleAsync(x => x.Id == u.Id)).DeletedAt);
    }

    [Fact]
    public async Task Deleting_an_account_requires_sign_in()
    {
        var (h, _) = await Host();
        var r = await h.Anonymous().DeleteAsync("/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Replies_by_deleted_user_on_other_works_are_removed_and_counts_update()
    {
        var (h, _) = await Host();
        var w = await Seed(h);
        var before = await w.T.Get($"/v1/artists/{(await HandleOf(h, w.O.Id))}");
        Assert.Equal(2, before.Data.GetProperty("followerCount").GetInt32());
        Assert.Equal(2, before.Data.GetProperty("followingCount").GetInt32());

        await DeleteAndRun(h, w.U);

        var after = await w.T.Get($"/v1/artists/{(await HandleOf(h, w.O.Id))}");
        Assert.Equal(1, after.Data.GetProperty("followerCount").GetInt32());
        Assert.Equal(1, after.Data.GetProperty("followingCount").GetInt32());
        await using var db = h.Db();
        Assert.DoesNotContain(await db.Replies.ToListAsync(), r => r.AuthorId == w.U.Id);
        Assert.Contains(await db.Replies.ToListAsync(), r => r.Id == w.TReplyOnO);
        Assert.DoesNotContain(await db.Replies.ToListAsync(), r => r.Id == w.OReplyOnU); // on U's work: gone with it
        var chapter = (await w.T.Get($"/v1/works/{w.OWork}")).Data.GetProperty("chapters")[0];
        Assert.Equal(1, chapter.GetProperty("replyCount").GetInt32());
    }

    private static async Task<string> HandleOf(ProfileTestHost h, Guid id)
    {
        await using var db = h.Db();
        return (await db.Profiles.SingleAsync(p => p.UserId == id)).Handle;
    }

    [Fact]
    public async Task Blobs_for_works_avatar_and_exports_are_deleted_and_the_cdn_is_purged()
    {
        var (h, _) = await Host();
        var w = await Seed(h);

        await DeleteAndRun(h, w.U);

        Assert.DoesNotContain(h.Blobs.Names, n => n.Contains(w.UWork.ToString()) || n.Contains(w.U.Id.ToString()));
        Assert.Contains($"works/{w.OWork}/thumb.webp", h.Blobs.Names);
        Assert.Contains($"originals/works/{w.TWork}", h.Blobs.Names);
        Assert.Contains($"avatars/{w.O.Id}/avatar-256.webp", h.Blobs.Names);
        Assert.Contains($"exports/{w.O.Id}/seed.zip", h.Blobs.Names);
        Assert.Contains(_cdn.Purged, p => p.Contains($"works/{w.UWork}/"));
        Assert.Contains(_cdn.Purged, p => p.Contains($"avatars/{w.U.Id}/"));
        Assert.DoesNotContain(_cdn.Purged, p => p.Contains(w.OWork.ToString()) || p.Contains("originals"));
    }

    [Fact]
    public async Task Job_is_idempotent_when_run_twice()
    {
        var (h, cs) = await Host();
        var w = await Seed(h);
        await DeleteAndRun(h, w.U);
        var snapshot = await Query(cs, "SELECT (SELECT count(*) FROM works), (SELECT count(*) FROM replies), (SELECT count(*) FROM follows), (SELECT count(*) FROM notifications), (SELECT count(*) FROM reports), (SELECT count(*) FROM users)");

        await RunJob(h, w.U.Id);
        await RunJob(h, Guid.NewGuid()); // a user that never existed is a no-op too

        Assert.Equal(snapshot[0], (await Query(cs, "SELECT (SELECT count(*) FROM works), (SELECT count(*) FROM replies), (SELECT count(*) FROM follows), (SELECT count(*) FROM notifications), (SELECT count(*) FROM reports), (SELECT count(*) FROM users)"))[0]);
        Assert.Equal(2, int.Parse(snapshot[0][5]));
    }

    [Fact]
    public async Task Apple_identities_are_revoked_before_rows_are_removed()
    {
        var (h, _) = await Host();
        var w = await Seed(h);
        Guid apple;
        await using (var db = h.Db()) apple = (await db.Identities.SingleAsync(i => i.UserId == w.U.Id && i.Provider == "apple")).Id;

        await DeleteAndRun(h, w.U);

        Assert.Equal([apple], _apple.Revoked);
    }

    [Fact]
    public async Task Other_users_data_survives_and_content_about_the_deleted_user_disappears()
    {
        var (h, _) = await Host();
        var w = await Seed(h);

        await DeleteAndRun(h, w.U);

        await using var db = h.Db();
        Assert.Equal(new[] { w.O.Id, w.T.Id }.Order(), (await db.Users.Select(u => u.Id).ToListAsync()).Order());
        Assert.Equal(new[] { w.OWork, w.TWork }.Order(), (await db.Works.Select(x => x.Id).ToListAsync()).Order());
        Assert.Equal(4, await db.Chapters.CountAsync());
        Assert.Equal(2, await db.Profiles.CountAsync());
        Assert.Equal(2, await db.Follows.CountAsync()); // T->O and O->T
        Assert.Empty(await db.Blocks.ToListAsync());
        Assert.Equal(2, await db.Notifications.CountAsync()); // the follower note from T and T's new-story note
        Assert.DoesNotContain(await db.Notifications.ToListAsync(), n => n.ActorId == w.U.Id || n.WorkId == w.UWork || n.ReplyId == w.UReplyOnO || n.ReplyId == w.OReplyOnU);
        Assert.Equal(w.T.Id, (await db.Inquiries.SingleAsync()).SenderId);
        Assert.Equal(w.OExportJob, (await db.ExportJobs.SingleAsync()).Id);
    }

    [Fact]
    public async Task Reports_targeting_the_user_are_deleted_and_reports_they_filed_are_scrubbed()
    {
        var (h, _) = await Host();
        var w = await Seed(h);

        await DeleteAndRun(h, w.U);

        await using var db = h.Db();
        var left = await db.Reports.OrderBy(r => r.CreatedAt).ToListAsync();
        Assert.Equal(2, left.Count); // the filed-by-U report on O's work (scrubbed) and T's report on O's work
        Assert.All(left, r => Assert.Equal(w.OWork, r.TargetId));
        var filedByU = left.Single(r => r.Note is null);
        Assert.Null(filedByU.ReporterId);
        Assert.DoesNotContain(left, r => r.Note == UNote);
    }

    [Fact]
    public async Task Sign_in_codes_for_the_users_emails_are_removed_and_strangers_codes_stay()
    {
        var (h, _) = await Host();
        var w = await Seed(h);

        await DeleteAndRun(h, w.U);

        await using var db = h.Db();
        Assert.Equal([StrangerEmail], await db.EmailCodes.Select(c => c.Email).ToListAsync());
        Assert.Equal([StrangerEmail], await db.VerifyAttempts.Select(c => c.Email).ToListAsync());
    }

    [Fact]
    public async Task The_evidence_hold_guard_is_an_unrestricted_no_op_for_now()
    {
        var (h, _) = await Host();
        await using var db = h.Db();
        Assert.Same(db.Reports, EvidenceHoldGuard.Deletable(db.Reports));
    }

    private static async Task<long> CountUsers(string cs) => long.Parse((await Query(cs, "SELECT count(*) FROM users"))[0][0]);

    [Fact]
    public async Task A_failing_step_is_retried_after_a_back_off_and_a_poison_user_does_not_starve_others()
    {
        var failing = new SelectiveFailingCdn();
        var (h, cs) = await Host(s => s.AddSingleton<ICdnPurger>(failing));
        var w = await Seed(h);
        failing.Poison = w.UWork;
        await RequestDelete(w.U);
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        await RequestDelete(w.T);
        var worker = h.Services.GetRequiredService<DeletionWorker>();

        await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None);

        await using (var db = h.Db())
        {
            var u = await db.Users.SingleAsync(x => x.Id == w.U.Id);
            Assert.NotNull(u.DeletedAt);
            Assert.Equal(1, u.DeletionFailures);
            Assert.Equal(h.Clock.GetUtcNow(), u.DeletionFailedAt);
            Assert.Equal(0, await db.Users.CountAsync(x => x.Id == w.T.Id)); // the next user was still processed
        }
        var calls = failing.Calls;
        await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None);
        Assert.Equal(calls, failing.Calls); // still inside the back-off window

        h.Clock.Advance(TimeSpan.FromMinutes(10));
        failing.Poison = null;
        await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None);
        Assert.Equal(1, await Count(cs, "users", "id", w.O.Id));
        Assert.Equal(0, await Count(cs, "users", "id", w.U.Id));
    }

    private sealed class SelectiveFailingCdn : ICdnPurger
    {
        public Guid? Poison { get; set; }
        public int Calls { get; private set; }

        public Task PurgeAsync(IReadOnlyList<string> urls)
        {
            Calls++;
            return Poison is { } p && urls.Any(u => u.Contains(p.ToString())) ? throw new InvalidOperationException("poison") : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task The_sweep_only_deletes_accounts_marked_deleted()
    {
        var (h, cs) = await Host();
        var w = await Seed(h);
        await RequestDelete(w.U);

        await h.Services.GetRequiredService<DeletionWorker>().RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None);

        Assert.Equal(2, await CountUsers(cs));
        Assert.Equal(0, await Count(cs, "users", "id", w.U.Id));
        Assert.Equal(1, await Count(cs, "users", "id", w.O.Id));
    }

    [Fact]
    public async Task Logs_during_deletion_contain_ids_and_counts_but_no_personal_content()
    {
        var capture = new CapturingLoggerProvider();
        var (h, _) = await Host(s => s.AddLogging(b => b.AddProvider(capture)));
        var w = await Seed(h);
        _cdn.OnPurge = () => throw new InvalidOperationException(ULogin);
        await RequestDelete(w.U);
        var worker = h.Services.GetRequiredService<DeletionWorker>();
        await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None);
        _cdn.OnPurge = null;
        h.Clock.Advance(TimeSpan.FromMinutes(10));
        await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None);

        var all = string.Join("\n", capture.Lines);
        Assert.DoesNotContain("secret-mail", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Una U", all);
        Assert.DoesNotContain("U story", all);
        Assert.DoesNotContain(UInquiryMessage, all);
        Assert.DoesNotContain(UNote, all);
        Assert.Contains("InvalidOperationException", all);
    }
}
