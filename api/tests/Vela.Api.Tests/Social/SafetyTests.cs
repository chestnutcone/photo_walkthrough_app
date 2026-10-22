using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Social;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using ReplyEntity = Vela.Api.Data.Entities.Reply;

namespace Vela.Api.Tests.Social;

[Collection(PostgresCollection.Name)]
public class SafetyTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    private ProfileTestHost Host(int? limit = null)
    {
        var h = new ProfileTestHost(pg, limit is null ? null : new Dictionary<string, string> { ["Reports:MaxPerUserPerDay"] = limit.ToString()! });
        _hosts.Add(h);
        return h;
    }
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private async Task<TestUser> User(ProfileTestHost h) => await h.SignInOnboarded(ProfileTestHost.UniqueHandle());

    private static async Task<Guid> SeedWork(ProfileTestHost h, Guid artist)
    {
        await using var db = h.Db();
        var w = new Work { Id = Guid.NewGuid(), ArtistId = artist, Title = "t", Status = WorkStatus.Published, ImageBlob = "b", Width = 3, Height = 4, CreatedAt = ProfileTestHost.Now };
        db.Works.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    }

    private static async Task<Guid> SeedReply(ProfileTestHost h, Guid workId, Guid author, bool hidden = false, HiddenBy? by = null)
    {
        await using var db = h.Db();
        var r = new ReplyEntity { Id = Guid.NewGuid(), WorkId = workId, ChapterIndex = 0, AuthorId = author, Text = "hi", Hidden = hidden, HiddenBy = by, CreatedAt = ProfileTestHost.Now };
        db.Replies.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    private static object Rep(string type, Guid id, string reason = "spam", string? note = null) =>
        new { targetType = type, targetId = id, reason, note };

    private static async Task Follow(ProfileTestHost h, Guid a, Guid b)
    {
        await using var db = h.Db();
        db.Follows.Add(new Follow { FollowerId = a, FolloweeId = b, CreatedAt = ProfileTestHost.Now });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Block_removes_follows_in_both_directions()
    {
        var h = Host(); var a = await User(h); var b = await User(h);
        await Follow(h, a.Id, b.Id); await Follow(h, b.Id, a.Id);

        var r = await a.Put($"/v1/blocks/{b.Id}");

        Assert.Equal(HttpStatusCode.OK, r.Status);
        await using var db = h.Db();
        Assert.False(await db.Follows.AnyAsync(f => f.FollowerId == a.Id || f.FollowerId == b.Id));
        Assert.True(await db.Blocks.AnyAsync(x => x.BlockerId == a.Id && x.BlockedId == b.Id));
    }

    [Fact]
    public async Task Block_is_idempotent_and_unblock_keeps_follows_removed()
    {
        var h = Host(); var a = await User(h); var b = await User(h);
        await Follow(h, a.Id, b.Id);
        Assert.Equal(HttpStatusCode.OK, (await a.Put($"/v1/blocks/{b.Id}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Put($"/v1/blocks/{b.Id}")).Status);

        var u1 = await a.Delete($"/v1/blocks/{b.Id}");
        var u2 = await a.Delete($"/v1/blocks/{b.Id}");

        Assert.Equal(HttpStatusCode.OK, u1.Status);
        Assert.Equal(HttpStatusCode.OK, u2.Status);
        await using var db = h.Db();
        Assert.Empty(await db.Blocks.Where(x => x.BlockerId == a.Id).ToListAsync());
        Assert.Empty(await db.Follows.Where(f => f.FollowerId == a.Id).ToListAsync());
    }

    [Fact]
    public async Task Cannot_block_self()
    {
        var h = Host(); var a = await User(h);
        var r = await a.Put($"/v1/blocks/{a.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("cannot_block_self", r.ErrorCode);
    }

    [Fact]
    public async Task Block_unknown_or_deleted_user_is_404()
    {
        var h = Host(); var a = await User(h); var b = await User(h);
        await using (var db = h.Db()) await db.Users.Where(u => u.Id == b.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        Assert.Equal("user_not_found", (await a.Put($"/v1/blocks/{Guid.NewGuid()}")).ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Put($"/v1/blocks/{b.Id}")).Status);
    }

    [Fact]
    public async Task Block_and_report_require_sign_in()
    {
        var h = Host();
        var c = h.Anonymous();
        var r = await c.PutAsync($"/v1/blocks/{Guid.NewGuid()}", null);
        var r2 = await c.PostAsJsonAsyncCompat("/v1/reports", new { targetType = "user", targetId = Guid.NewGuid(), reason = "spam" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
    }

    [Fact]
    public async Task Hidden_ids_include_blockers_and_blocked()
    {
        var h = Host(); var a = await User(h); var b = await User(h); var c = await User(h); var d = await User(h);
        await a.Put($"/v1/blocks/{b.Id}");
        await c.Put($"/v1/blocks/{a.Id}");

        var viewerSet = await HiddenFor(h, a.Id);
        var guestSet = await HiddenFor(h, null);

        Assert.Equal(new[] { b.Id, c.Id }.Order(), viewerSet.Order());
        Assert.DoesNotContain(d.Id, viewerSet);
        Assert.Empty(guestSet);
    }

    private static async Task<IReadOnlySet<Guid>> HiddenFor(ProfileTestHost h, Guid? viewer)
    {
        await using var db = h.Db();
        return await new Visibility(db).HiddenUserIdsAsync(viewer);
    }

    [Fact]
    public async Task Visibility_is_cached_per_instance()
    {
        var h = Host(); var a = await User(h); var b = await User(h);
        await using var db = h.Db();
        var vis = new Visibility(db);
        Assert.Empty(await vis.HiddenUserIdsAsync(a.Id));
        await a.Put($"/v1/blocks/{b.Id}");
        Assert.Empty(await vis.HiddenUserIdsAsync(a.Id));
    }

    [Fact]
    public async Task Blocked_relation_hides_artist_profile_in_both_directions()
    {
        var h = Host();
        var ha = ProfileTestHost.UniqueHandle(); var hb = ProfileTestHost.UniqueHandle(); var hc = ProfileTestHost.UniqueHandle();
        var a = await h.SignInOnboarded(ha); var b = await h.SignInOnboarded(hb); var c = await h.SignInOnboarded(hc);
        await a.Put($"/v1/blocks/{b.Id}");

        Assert.Equal("artist_not_found", (await a.Get($"/v1/artists/{hb}")).ErrorCode);
        Assert.Equal("artist_not_found", (await b.Get($"/v1/artists/{ha}")).ErrorCode);
        Assert.Equal(HttpStatusCode.OK, (await c.Get($"/v1/artists/{ha}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await h.Anonymous().GetAsync($"/v1/artists/{hb}")).StatusCode);
    }

    [Fact]
    public async Task Suggested_excludes_blocked_and_blockers()
    {
        var h = Host();
        var a = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var b = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var c = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        await a.Put($"/v1/blocks/{b.Id}");
        await c.Put($"/v1/blocks/{a.Id}");

        var ids = (await a.Get("/v1/artists/suggested")).Data.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.DoesNotContain(b.Id, ids);
        Assert.DoesNotContain(c.Id, ids);
    }

    [Fact]
    public async Task Reported_reply_is_hidden()
    {
        var h = Host(); var artist = await User(h); var author = await User(h); var rep = await User(h);
        var rid = await SeedReply(h, await SeedWork(h, artist.Id), author.Id);

        var r = await rep.Post("/v1/reports", Rep("reply", rid, "harassment", "mean"));

        Assert.Equal(HttpStatusCode.OK, r.Status);
        await using var db = h.Db();
        var reply = await db.Replies.SingleAsync(x => x.Id == rid);
        Assert.True(reply.Hidden);
        Assert.Equal(HiddenBy.Report, reply.HiddenBy);
        var row = await db.Reports.SingleAsync(x => x.TargetId == rid);
        Assert.Equal("user_report", row.Source);
        Assert.Equal("pending", row.Status);
        Assert.Equal("harassment", row.Reason);
        Assert.Equal("mean", row.Note);
        Assert.Equal(rep.Id, row.ReporterId);
    }

    [Fact]
    public async Task Reported_reply_hidden_by_admin_stays_admin()
    {
        var h = Host(); var artist = await User(h); var author = await User(h); var rep = await User(h);
        var rid = await SeedReply(h, await SeedWork(h, artist.Id), author.Id, true, HiddenBy.Admin);
        await rep.Post("/v1/reports", Rep("reply", rid));
        await using var db = h.Db();
        Assert.Equal(HiddenBy.Admin, (await db.Replies.SingleAsync(x => x.Id == rid)).HiddenBy);
    }

    [Fact]
    public async Task Reported_work_is_report_hidden_and_reported_user_hides_nothing()
    {
        var h = Host(); var artist = await User(h); var rep = await User(h);
        var wid = await SeedWork(h, artist.Id);

        Assert.Equal(HttpStatusCode.OK, (await rep.Post("/v1/reports", Rep("work", wid, "copyright"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await rep.Post("/v1/reports", Rep("user", artist.Id))).Status);

        await using var db = h.Db();
        Assert.True((await db.Works.SingleAsync(w => w.Id == wid)).ReportHidden);
        Assert.Equal(WorkStatus.Published, (await db.Works.SingleAsync(w => w.Id == wid)).Status);
        Assert.Equal(2, await db.Reports.CountAsync(x => x.ReporterId == rep.Id));
    }

    [Fact]
    public async Task Report_validation_and_ownership_rules()
    {
        var h = Host(); var artist = await User(h); var rep = await User(h);
        var wid = await SeedWork(h, artist.Id);
        var rid = await SeedReply(h, wid, rep.Id);

        Assert.Equal("target_not_found", (await rep.Post("/v1/reports", Rep("work", Guid.NewGuid()))).ErrorCode);
        Assert.Equal("target_not_found", (await rep.Post("/v1/reports", Rep("user", Guid.NewGuid()))).ErrorCode);
        Assert.Equal("cannot_report_own", (await artist.Post("/v1/reports", Rep("work", wid))).ErrorCode);
        Assert.Equal("cannot_report_own", (await rep.Post("/v1/reports", Rep("reply", rid))).ErrorCode);
        Assert.Equal("cannot_report_own", (await rep.Post("/v1/reports", Rep("user", rep.Id))).ErrorCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await rep.Post("/v1/reports", Rep("work", wid, "nonsense"))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await rep.Post("/v1/reports", Rep("comment", wid))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await rep.Post("/v1/reports", Rep("work", wid, "spam", new string('x', 501)))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await rep.Post("/v1/reports", "not json")).Status);
    }

    [Fact]
    public async Task Duplicate_report_by_same_user_is_idempotent()
    {
        var h = Host(1); var artist = await User(h); var rep = await User(h);
        var wid = await SeedWork(h, artist.Id);

        var first = await rep.Post("/v1/reports", Rep("work", wid));
        var second = await rep.Post("/v1/reports", Rep("work", wid));

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        await using var db = h.Db();
        Assert.Equal(1, await db.Reports.CountAsync(x => x.TargetId == wid));
    }

    [Fact]
    public async Task Auto_flag_has_no_reporter_and_source_auto_flag()
    {
        var h = Host();
        var target = Guid.NewGuid();
        await using var db = h.Db();
        var svc = new ReportService(db, TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new ReportOptions()));

        await svc.CreateAutoFlagAsync("reply", target, "hate", 6);
        await svc.CreateAutoFlagAsync("reply", target, "hate", 6);

        await using var check = h.Db();
        var row = await check.Reports.SingleAsync(x => x.TargetId == target);
        Assert.Equal("auto_flag", row.Source);
        Assert.Null(row.ReporterId);
        Assert.Equal("pending", row.Status);
        Assert.Equal("auto:hate:6", row.Reason);
    }

    [Fact]
    public async Task Eleventh_report_in_a_day_is_429_report_limit_reached()
    {
        var h = Host(); var rep = await User(h);
        var targets = new List<Guid>();
        for (var i = 0; i < 11; i++) targets.Add((await User(h)).Id);
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, (await rep.Post("/v1/reports", Rep("user", targets[i]))).Status);

        var r = await rep.Post("/v1/reports", Rep("user", targets[10]));

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
        Assert.Equal("report_limit_reached", r.ErrorCode);
        // Duplicate of an existing report does not count and still succeeds at the limit.
        Assert.Equal(HttpStatusCode.OK, (await rep.Post("/v1/reports", Rep("user", targets[0]))).Status);
    }

    [Fact]
    public async Task Limit_response_carries_retry_after_until_next_utc_midnight()
    {
        var h = Host(1); var rep = await User(h); var a = await User(h); var b = await User(h);
        await rep.Post("/v1/reports", Rep("user", a.Id));
        var raw = await RawPost(h, rep, Rep("user", b.Id));
        Assert.Equal(HttpStatusCode.TooManyRequests, raw.StatusCode);
        Assert.Equal("43200", raw.Headers.GetValues("Retry-After").Single()); // clock is 12:00 UTC
    }

    private static async Task<HttpResponseMessage> RawPost(ProfileTestHost h, TestUser u, object body)
    {
        var c = u.Client;
        return await c.PostAsJsonAsyncCompat("/v1/reports", body);
    }

    [Fact]
    public async Task Report_limit_is_per_account_and_resets_next_utc_day()
    {
        var h = Host(1);
        h.Clock.Advance(TimeSpan.FromMinutes(715)); // 23:55 UTC, so the access tokens outlive midnight
        var r1 = await User(h); var r2 = await User(h); var a = await User(h); var b = await User(h);
        Assert.Equal(HttpStatusCode.OK, (await r1.Post("/v1/reports", Rep("user", a.Id))).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await r1.Post("/v1/reports", Rep("user", b.Id))).Status);
        Assert.Equal(HttpStatusCode.OK, (await r2.Post("/v1/reports", Rep("user", b.Id))).Status);

        h.Clock.Advance(TimeSpan.FromMinutes(6)); // past UTC midnight

        Assert.Equal(HttpStatusCode.OK, (await r1.Post("/v1/reports", Rep("user", b.Id))).Status);
    }

    [Fact]
    public async Task Report_limit_is_configurable()
    {
        var h = Host(2); var rep = await User(h);
        var t = new List<Guid>(); for (var i = 0; i < 3; i++) t.Add((await User(h)).Id);
        Assert.Equal(HttpStatusCode.OK, (await rep.Post("/v1/reports", Rep("user", t[0]))).Status);
        Assert.Equal(HttpStatusCode.OK, (await rep.Post("/v1/reports", Rep("user", t[1]))).Status);
        Assert.Equal("report_limit_reached", (await rep.Post("/v1/reports", Rep("user", t[2]))).ErrorCode);
    }
}

internal static class HttpCompat
{
    public static Task<HttpResponseMessage> PostAsJsonAsyncCompat(this HttpClient c, string url, object body) =>
        System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(c, url, body);
}
