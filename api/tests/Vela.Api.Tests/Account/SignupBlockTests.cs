using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Account;
using Vela.Api.Admin;
using Vela.Api.Auth;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Account;

/// <summary>
/// Owner decision 2026-11-03: deleting an account while its content is under review blocks the same sign-in identity from
/// signing up again until the review closes, at most 30 days after the deletion request. The block row holds only the identity
/// hash and dates and is removed when it lapses. Admin bans are separate and permanent.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SignupBlockTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private static readonly DateTimeOffset Now = ProfileTestHost.Now;
    private ProfileTestHost H => _s.Host;

    public async Task InitializeAsync() => _s = await AdminSite.Create(pg, isolated: true);
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private static string Hash(string sub) => BanHasher.Hash(AdminSite.Pepper, "fake", sub);

    private async Task<(int Status, string? Code)> Sso(string sub)
    {
        var r = await H.Anonymous().PostAsJsonAsync("/v1/auth/sso", new { provider = "fake", idToken = $"fake:{sub}", nonce = "n" });
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var code = body.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object ? e.GetProperty("code").GetString() : null;
        return ((int)r.StatusCode, code);
    }

    /// <summary>An onboarded account signed in with a known fake subject.</summary>
    private async Task<(TestUser User, string Sub)> Account()
    {
        var sub = "blk-" + Guid.NewGuid();
        var client = H.Anonymous();
        var r = await client.PostAsJsonAsync("/v1/auth/sso", new { provider = "fake", idToken = $"fake:{sub}", nonce = "n" });
        var token = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        await using var db = H.Db();
        var u = new TestUser(client, (await db.Identities.SingleAsync(i => i.Subject == sub)).UserId);
        Assert.Equal(HttpStatusCode.OK, (await u.Onboard(ProfileTestHost.UniqueHandle())).Status);
        return (u, sub);
    }

    private async Task<Guid> PendingWork(Guid owner)
    {
        var id = Guid.NewGuid();
        await using var db = H.Db();
        db.Works.Add(new Work { Id = id, ArtistId = owner, Title = "t", Status = WorkStatus.Pending, ImageBlob = $"originals/works/{id}", CreatedAt = Now });
        await db.SaveChangesAsync();
        return id;
    }

    private Task<int> Sweep() => H.Services.GetRequiredService<DeletionWorker>().RunOnceAsync(H.Clock.GetUtcNow(), CancellationToken.None);

    private async Task<List<SignupBlock>> Blocks()
    {
        await using var db = H.Db();
        return await db.SignupBlocks.AsNoTracking().ToListAsync();
    }

    private async Task<(TestUser User, string Sub, Guid Work)> DeletedDuringReview()
    {
        var (u, sub) = await Account();
        var work = await PendingWork(u.Id);
        var r = await u.Delete("/v1/me");
        Assert.Equal("deferred_review", r.Data.GetProperty("state").GetString());
        return (u, sub, work);
    }

    [Fact]
    public async Task Deleting_during_review_blocks_the_same_identity_with_signup_blocked()
    {
        var (_, sub, _) = await DeletedDuringReview();

        Assert.Equal((403, "signup_blocked"), await Sso(sub));

        var block = Assert.Single(await Blocks());
        Assert.Equal(Hash(sub), block.SubjectHash);
        Assert.Equal(Now, block.CreatedAt);
        Assert.Equal(Now.AddDays(30), block.ExpiresAt);
    }

    [Fact]
    public async Task The_block_row_holds_only_the_identity_hash_and_dates()
    {
        await using var db = H.Db();
        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'signup_blocks' ORDER BY column_name").ToListAsync();

        Assert.Equal(["created_at", "expires_at", "subject_hash"], columns);
    }

    [Fact]
    public async Task Deleting_without_open_reviews_creates_no_block()
    {
        var (u, sub) = await Account();

        Assert.Equal("scheduled", (await u.Delete("/v1/me")).Data.GetProperty("state").GetString());

        Assert.Empty(await Blocks());
        Assert.Equal((403, "account_deleted"), await Sso(sub)); // until the hard delete a minute later, as before
        Assert.Equal(1, await Sweep());
        Assert.Equal(200, (await Sso(sub)).Status);
    }

    [Fact]
    public async Task The_block_lapses_and_its_row_is_removed_when_the_review_closes()
    {
        var (_, sub, work) = await DeletedDuringReview();
        await using (var db = H.Db())
            await db.Works.Where(w => w.Id == work).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkStatus.Published));
        Assert.Equal((403, "signup_blocked"), await Sso(sub)); // until the sweep notices the review is closed

        H.Clock.Advance(DeletionWorker.DeferredRecheck + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await Sweep());

        Assert.Empty(await Blocks());
        Assert.Equal(200, (await Sso(sub)).Status);
    }

    [Fact]
    public async Task The_block_lasts_at_most_30_days_while_the_review_stays_open()
    {
        var (_, sub, _) = await DeletedDuringReview();

        H.Clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));
        Assert.Equal(0, await Sweep());
        Assert.Equal((403, "signup_blocked"), await Sso(sub));

        H.Clock.Advance(TimeSpan.FromSeconds(1)); // exactly 30 days after the request
        Assert.Equal(1, await Sweep());

        Assert.Empty(await Blocks());
        Assert.Equal(200, (await Sso(sub)).Status);
    }

    [Fact]
    public async Task Sign_in_check_refuses_a_new_account_until_the_block_expires()
    {
        // The identity row is already gone (the block row stands on its own): checked at sign-in against its expiry.
        var sub = "orphan-" + Guid.NewGuid();
        await using (var db = H.Db())
        {
            db.SignupBlocks.Add(new SignupBlock { SubjectHash = Hash(sub), CreatedAt = Now, ExpiresAt = Now.AddHours(1) });
            await db.SaveChangesAsync();
        }

        H.Clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromSeconds(1));
        Assert.Equal((403, "signup_blocked"), await Sso(sub));
        await using (var db = H.Db()) Assert.False(await db.Identities.AnyAsync(i => i.Subject == sub));

        H.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(200, (await Sso(sub)).Status);
    }

    [Fact]
    public async Task The_hourly_retention_sweep_removes_lapsed_blocks_only()
    {
        await using (var db = H.Db())
        {
            db.SignupBlocks.AddRange(
                new SignupBlock { SubjectHash = new string('a', 64), CreatedAt = Now.AddDays(-30), ExpiresAt = Now },
                new SignupBlock { SubjectHash = new string('b', 64), CreatedAt = Now, ExpiresAt = Now.AddSeconds(1) });
            await db.SaveChangesAsync();
        }

        await H.Services.GetRequiredService<AuthDataRetentionJob>().RunOnceAsync(Now, CancellationToken.None);

        Assert.Equal([new string('b', 64)], (await Blocks()).Select(b => b.SubjectHash));
    }

    private async Task<HttpResponseMessage> EmailSignIn(HttpClient api, string email)
    {
        await api.PostAsJsonAsync("/v1/auth/email/request", new { email });
        return await api.PostAsJsonAsync("/v1/auth/email/verify", new { email, code = _s.NewBrowser().LatestCode(email) });
    }

    [Fact]
    public async Task Email_sign_in_with_the_same_address_is_blocked_too()
    {
        var email = $"blk-{Guid.NewGuid():N}@example.test";
        var api = H.Anonymous();
        var first = await (await EmailSignIn(api, email)).Content.ReadFromJsonAsync<JsonElement>();
        api.DefaultRequestHeaders.Authorization = new("Bearer", first.GetProperty("data").GetProperty("accessToken").GetString());
        Guid userId;
        await using (var db = H.Db()) userId = (await db.Identities.SingleAsync(i => i.Provider == "email" && i.Subject == email)).UserId;
        var u = new TestUser(api, userId);
        Assert.Equal(HttpStatusCode.OK, (await u.Onboard(ProfileTestHost.UniqueHandle())).Status);
        await PendingWork(userId);
        Assert.Equal("deferred_review", (await u.Delete("/v1/me")).Data.GetProperty("state").GetString());

        var verify = await EmailSignIn(H.Anonymous(), email);

        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        var body = await verify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("signup_blocked", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(BanHasher.Hash(AdminSite.Pepper, "email", email), Assert.Single(await Blocks()).SubjectHash);
    }

    [Fact]
    public async Task An_admin_ban_during_the_window_stays_permanent_after_the_block_lapses()
    {
        var admin = await _s.NewAdminUser();
        var (u, sub, _) = await DeletedDuringReview();
        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.SuspendUserAsync(admin, u.Id, "illegal")));

        H.Clock.Advance(TimeSpan.FromDays(30));
        await Sweep();

        Assert.Empty(await Blocks());
        Assert.Equal((403, "account_suspended"), await Sso(sub));
        H.Clock.Advance(TimeSpan.FromDays(365));
        Assert.Equal((403, "account_suspended"), await Sso(sub));
    }
}
