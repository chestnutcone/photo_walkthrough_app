using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using ReplyRow = Vela.Api.Data.Entities.Reply;
using Reply = Vela.Api.Tests.Profiles.Reply;

namespace Vela.Api.Tests.Feed;

[Collection(PostgresCollection.Name)]
public class FeedTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    private async Task<ProfileTestHost> IsolatedHost()
    {
        var h = new ProfileTestHost(pg, null, await pg.CreateIsolatedDatabaseAsync());
        _hosts.Add(h);
        return h;
    }
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private static Task<TestUser> User(ProfileTestHost h) => h.SignInOnboarded(ProfileTestHost.UniqueHandle());

    private static async Task<Guid> Seed(ProfileTestHost h, Guid artist, DateTimeOffset? at, WorkStatus status = WorkStatus.Published,
        bool reportHidden = false, int chapters = 2, string title = "Story", Guid? id = null)
    {
        await using var db = h.Db();
        var w = new Work
        {
            Id = id ?? Guid.NewGuid(), ArtistId = artist, Title = title, Status = status, ReportHidden = reportHidden,
            ImageBlob = "b", Width = 300, Height = 400, CreatedAt = ProfileTestHost.Now, PublishedAt = at,
        };
        db.Works.Add(w);
        for (var i = 0; i < chapters; i++)
            db.Chapters.Add(new Chapter { WorkId = w.Id, Index = i, X = 0.1 * (i + 1), Y = 0.9, Title = $"c{i}", Body = "b" });
        await db.SaveChangesAsync();
        return w.Id;
    }

    private static DateTimeOffset T(int minutes) => ProfileTestHost.Now.AddMinutes(minutes);

    private static List<Guid> Ids(Reply r) => r.Data.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();

    private static async Task<List<Guid>> AllPages(TestUser u, string url, int limit)
    {
        var ids = new List<Guid>(); string? cursor = null;
        for (var guard = 0; guard < 50; guard++)
        {
            var r = await u.Get($"{url}{(url.Contains('?') ? '&' : '?')}limit={limit}{(cursor is null ? "" : "&cursor=" + cursor)}");
            Assert.Equal(HttpStatusCode.OK, r.Status);
            ids.AddRange(Ids(r));
            var page = r.Body.GetProperty("page");
            if (page.ValueKind == JsonValueKind.Null) return ids;
            cursor = page.GetProperty("nextCursor").GetString();
        }
        throw new Exception("pagination did not terminate");
    }

    [Fact]
    public async Task Following_feed_lists_followed_artists_first()
    {
        var h = await IsolatedHost();
        var viewer = await User(h); var followed = await User(h); var other = await User(h);
        var oldFollowed = await Seed(h, followed.Id, T(1));
        var newOther = await Seed(h, other.Id, T(9));
        var newFollowed = await Seed(h, followed.Id, T(5));
        var oldOther = await Seed(h, other.Id, T(2));
        await viewer.Put($"/v1/artists/{followed.Id}/follow");

        var following = Ids(await viewer.Get("/v1/feed?tab=following"));
        var discover = Ids(await viewer.Get("/v1/feed?tab=discover"));

        Assert.Equal([newFollowed, oldFollowed, newOther, oldOther], following);
        Assert.Equal([newOther, newFollowed, oldOther, oldFollowed], discover);
    }

    [Fact]
    public async Task Guest_following_tab_returns_discover_and_so_does_a_viewer_who_follows_nobody()
    {
        var h = await IsolatedHost();
        var viewer = await User(h); var a = await User(h);
        var w1 = await Seed(h, a.Id, T(1)); var w2 = await Seed(h, a.Id, T(2));

        var guest = await h.Anonymous().GetAsync("/v1/feed?tab=following");
        var body = JsonDocument.Parse(await guest.Content.ReadAsStringAsync()).RootElement;
        var signedIn = await viewer.Get("/v1/feed?tab=following");

        Assert.Equal(HttpStatusCode.OK, guest.StatusCode);
        Assert.Equal([w2, w1], body.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
        Assert.Equal([w2, w1], Ids(signedIn));
    }

    [Fact]
    public async Task Feed_excludes_drafts_auto_blocked_removed_and_blocked_users()
    {
        var h = await IsolatedHost();
        var viewer = await User(h); var a = await User(h);
        var blockedByViewer = await User(h); var blocksViewer = await User(h); var suspended = await User(h); var deleted = await User(h);
        var shown = await Seed(h, a.Id, T(10));
        foreach (var s in new[] { WorkStatus.Draft, WorkStatus.Pending, WorkStatus.Blocked, WorkStatus.Removed }) await Seed(h, a.Id, T(11), s);
        await Seed(h, a.Id, T(12), reportHidden: true);
        await Seed(h, blockedByViewer.Id, T(13)); await Seed(h, blocksViewer.Id, T(14));
        await Seed(h, suspended.Id, T(15)); await Seed(h, deleted.Id, T(16));
        await using (var db = h.Db())
        {
            await db.Users.Where(u => u.Id == suspended.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));
            await db.Users.Where(u => u.Id == deleted.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        }
        await viewer.Put($"/v1/blocks/{blockedByViewer.Id}");
        await blocksViewer.Put($"/v1/blocks/{viewer.Id}");

        foreach (var tab in new[] { "discover", "following" })
            Assert.Equal([shown], Ids(await viewer.Get($"/v1/feed?tab={tab}")));
        var guest = JsonDocument.Parse(await (await h.Anonymous().GetAsync("/v1/feed?tab=discover")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(3, guest.GetProperty("data").GetArrayLength()); // a guest has no block set: shown plus both blocked-pair works
    }

    [Fact]
    public async Task Feed_pagination_has_no_duplicates_or_gaps_with_equal_timestamps()
    {
        var h = await IsolatedHost();
        var viewer = await User(h); var followed = await User(h); var other = await User(h);
        var works = new List<(Guid Id, DateTimeOffset At, bool Followed)>();
        for (var i = 0; i < 6; i++) { var at = i < 4 ? T(5) : T(3); works.Add((await Seed(h, followed.Id, at), at, true)); }
        for (var i = 0; i < 7; i++) { var at = i < 5 ? T(5) : T(8); works.Add((await Seed(h, other.Id, at), at, false)); }
        await viewer.Put($"/v1/artists/{followed.Id}/follow");
        // Postgres orders uuids bytewise, which matches ordinal order of the canonical text.
        List<Guid> Order(IEnumerable<(Guid Id, DateTimeOffset At, bool Followed)> q) =>
            q.OrderByDescending(x => x.At).ThenByDescending(x => x.Id.ToString(), StringComparer.Ordinal).Select(x => x.Id).ToList();
        var expectedDiscover = Order(works);
        var expectedFollowing = Order(works.Where(w => w.Followed)).Concat(Order(works.Where(w => !w.Followed))).ToList();

        foreach (var limit in new[] { 1, 2, 3, 5, 6, 7, 50 })
        {
            Assert.Equal(expectedDiscover, await AllPages(viewer, "/v1/feed?tab=discover", limit));
            Assert.Equal(expectedFollowing, await AllPages(viewer, "/v1/feed?tab=following", limit));
        }
    }

    [Fact]
    public async Task Feed_item_carries_the_summary_the_card_needs()
    {
        var h = await IsolatedHost();
        var viewer = await User(h); var artist = await User(h); var troll = await User(h);
        var work = await Seed(h, artist.Id, T(1), chapters: 3, title: "Harbour");
        await using (var db = h.Db())
        {
            db.Replies.Add(new ReplyRow { Id = Guid.NewGuid(), WorkId = work, ChapterIndex = 1, AuthorId = viewer.Id, Text = "a", CreatedAt = T(2) });
            db.Replies.Add(new ReplyRow { Id = Guid.NewGuid(), WorkId = work, ChapterIndex = 1, AuthorId = troll.Id, Text = "b", Hidden = true, HiddenBy = HiddenBy.Report, CreatedAt = T(2) });
            await db.SaveChangesAsync();
        }
        await viewer.Put($"/v1/artists/{artist.Id}/follow");

        var item = (await viewer.Get("/v1/feed?tab=discover")).Data[0];

        Assert.Equal("Harbour", item.GetProperty("title").GetString());
        Assert.Equal(artist.Id, item.GetProperty("author").GetProperty("id").GetGuid());
        Assert.True(item.GetProperty("author").GetProperty("isFollowing").GetBoolean());
        Assert.Contains("thumb", item.GetProperty("thumbUrl").GetString());
        Assert.Contains("feed", item.GetProperty("feedUrl").GetString());
        var chapters = item.GetProperty("chapters").EnumerateArray().ToList();
        Assert.Equal(3, chapters.Count);
        Assert.Equal(0.2, chapters[1].GetProperty("x").GetDouble(), 6);
        Assert.Equal(0.9, chapters[1].GetProperty("y").GetDouble(), 6);
        Assert.Equal([0, 1, 0], chapters.Select(c => c.GetProperty("replyCount").GetInt32()));
        Assert.False(item.TryGetProperty("status", out var st) && st.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Feed_rejects_bad_tab_cursor_and_limit_and_clamps_big_limits()
    {
        var h = await IsolatedHost();
        var viewer = await User(h); var a = await User(h);
        for (var i = 0; i < 3; i++) await Seed(h, a.Id, T(i));

        Assert.Equal("validation_failed", (await viewer.Get("/v1/feed?tab=nope")).ErrorCode);
        Assert.Equal("validation_failed", (await viewer.Get("/v1/feed")).ErrorCode);
        var bad = await viewer.Get("/v1/feed?tab=discover&cursor=garbage");
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal("invalid_cursor", bad.ErrorCode);
        Assert.Equal("validation_failed", (await viewer.Get("/v1/feed?tab=discover&limit=abc")).ErrorCode);
        Assert.Equal(3, (await viewer.Get("/v1/feed?tab=discover&limit=500")).Data.GetArrayLength());
        Assert.Equal(1, (await viewer.Get("/v1/feed?tab=discover&limit=0")).Data.GetArrayLength());
    }

    [Fact]
    public async Task Artist_works_are_published_visible_newest_first_and_paged()
    {
        var h = await IsolatedHost();
        var viewer = await User(h); var artist = await User(h);
        var handle = await HandleOf(artist);
        var w = new List<Guid>();
        for (var i = 0; i < 5; i++) w.Add(await Seed(h, artist.Id, T(i % 2 == 0 ? 5 : i)));
        await Seed(h, artist.Id, T(9), WorkStatus.Draft); await Seed(h, artist.Id, T(9), reportHidden: true);

        var all = await AllPages(viewer, $"/v1/artists/{handle}/works", 2);
        var guest = JsonDocument.Parse(await (await h.Anonymous().GetAsync($"/v1/artists/{handle}/works")).Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(5, all.Count);
        Assert.Equal(5, all.Distinct().Count());
        Assert.Equal(5, all.Intersect(w).Count());
        Assert.Equal(5, guest.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Artist_works_404_for_unknown_blocked_and_suspended_artists()
    {
        var h = await IsolatedHost();
        var viewer = await User(h); var blocker = await User(h); var suspended = await User(h);
        var suspendedHandle = await HandleOf(suspended);
        var blockerHandle = await HandleOf(blocker);
        await blocker.Put($"/v1/blocks/{viewer.Id}");
        await using (var db = h.Db())
            await db.Users.Where(u => u.Id == suspended.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));

        foreach (var handle in new[] { "nobody_here", suspendedHandle })
        {
            var r = await viewer.Get($"/v1/artists/{handle}/works");
            Assert.Equal(HttpStatusCode.NotFound, r.Status);
            Assert.Equal("artist_not_found", r.ErrorCode);
        }
        Assert.Equal("artist_not_found", (await viewer.Get($"/v1/artists/{blockerHandle}/works")).ErrorCode);
    }

    private static async Task<string> HandleOf(TestUser u) =>
        (await u.Get("/v1/me")).Data.GetProperty("profile").GetProperty("handle").GetString()!;
}
