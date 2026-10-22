using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using Reply = Vela.Api.Tests.Profiles.Reply;

namespace Vela.Api.Tests.Search;

[Collection(PostgresCollection.Name)]
public class SearchTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    private async Task<ProfileTestHost> IsolatedHost()
    {
        var h = new ProfileTestHost(pg, null, await pg.CreateIsolatedDatabaseAsync());
        _hosts.Add(h);
        return h;
    }
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private static async Task<TestUser> Artist(ProfileTestHost h, string handle, string name = "Test Artist", string city = "", string discipline = "")
    {
        var u = await h.SignInOnboarded(handle, name);
        await using var db = h.Db();
        await db.Profiles.Where(p => p.UserId == u.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.City, city).SetProperty(p => p.Discipline, discipline));
        return u;
    }

    private static async Task<Guid> Story(ProfileTestHost h, Guid artist, string title, string intro = "", string[]? chapters = null,
        WorkStatus status = WorkStatus.Published, bool reportHidden = false)
    {
        await using var db = h.Db();
        var w = new Work
        {
            Id = Guid.NewGuid(), ArtistId = artist, Title = title, Intro = intro, Status = status, ReportHidden = reportHidden,
            ImageBlob = "b", Width = 300, Height = 400, CreatedAt = ProfileTestHost.Now,
            PublishedAt = status == WorkStatus.Draft ? null : ProfileTestHost.Now,
        };
        db.Works.Add(w);
        await db.SaveChangesAsync();
        var i = 0;
        foreach (var c in chapters ?? ["ch"])
            db.Chapters.Add(new Chapter { WorkId = w.Id, Index = i++, X = 0.5, Y = 0.5, Title = c, Body = "b" });
        await db.SaveChangesAsync();
        return w.Id;
    }

    private static List<Guid> Ids(Reply r, string list) => r.Data.GetProperty(list).EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();

    private static async Task<List<Guid>> AllPages(TestUser u, string q, string type, int limit)
    {
        var ids = new List<Guid>(); string? cursor = null;
        for (var guard = 0; guard < 60; guard++)
        {
            var r = await u.Get($"/v1/search?q={Uri.EscapeDataString(q)}&type={type}&limit={limit}{(cursor is null ? "" : "&cursor=" + cursor)}");
            Assert.Equal(HttpStatusCode.OK, r.Status);
            ids.AddRange(Ids(r, type));
            var page = r.Body.GetProperty("page");
            if (page.ValueKind == JsonValueKind.Null) return ids;
            cursor = page.GetProperty("nextCursor").GetString();
        }
        throw new Exception("pagination did not terminate");
    }

    [Fact]
    public async Task Finds_artist_by_handle_name_city_and_discipline()
    {
        var h = await IsolatedHost();
        var viewer = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var a = await Artist(h, "quillmaker", "Ines Marquez", "Lisbon", "Printmaker");
        await Artist(h, ProfileTestHost.UniqueHandle("z"), "Someone Else", "Oslo", "Potter");

        foreach (var q in new[] { "quillmaker", "ines", "MARQUEZ", "lisbon", "printmaker", "ines lisbon" })
            Assert.Equal([a.Id], Ids(await viewer.Get($"/v1/search?q={q}&type=artists"), "artists"));
        Assert.Empty(Ids(await viewer.Get("/v1/search?q=ines+oslo&type=artists"), "artists"));
    }

    [Fact]
    public async Task Typo_tolerant_match_via_trigram()
    {
        var h = await IsolatedHost();
        var viewer = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var a = await Artist(h, ProfileTestHost.UniqueHandle("q"), "Hieronymus Bosch", "Rotterdam", "Painter");
        var w = await Story(h, a.Id, "The Garden of Earthly Delights");

        Assert.Equal([a.Id], Ids(await viewer.Get("/v1/search?q=hieronimus&type=artists"), "artists"));
        Assert.Equal([w], Ids(await viewer.Get("/v1/search?q=gardin+earthly&type=stories"), "stories"));
        Assert.Empty(Ids(await viewer.Get("/v1/search?q=zzzzqqqq&type=artists"), "artists"));
    }

    [Fact]
    public async Task Finds_story_by_chapter_title()
    {
        var h = await IsolatedHost();
        var a = await Artist(h, ProfileTestHost.UniqueHandle("c"));
        var byChapter = await Story(h, a.Id, "Untitled", "plain intro", ["The lighthouse keeper", "Low tide"]);
        var byIntro = await Story(h, a.Id, "Harbour", "a quiet morning at the pier");
        var guest = h.Anonymous();

        async Task<List<Guid>> Search(string q) =>
            JsonDocument.Parse(await (await guest.GetAsync($"/v1/search?q={Uri.EscapeDataString(q)}&type=stories")).Content.ReadAsStringAsync())
                .RootElement.GetProperty("data").GetProperty("stories").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();

        Assert.Equal([byChapter], await Search("lighthouse"));
        Assert.Equal([byChapter], await Search("low tide"));
        Assert.Equal([byIntro], await Search("quiet morning"));
        Assert.Equal([byIntro], await Search("harbour"));
    }

    [Fact]
    public async Task Search_text_follows_published_content_and_ranks_title_matches_first()
    {
        var h = await IsolatedHost();
        var a = await Artist(h, ProfileTestHost.UniqueHandle("r"));
        var chapterOnly = await Story(h, a.Id, "Alpha", "", ["Moonrise over the bay"]);
        var titleSub = await Story(h, a.Id, "Bay of moonrise", "");
        var titlePrefix = await Story(h, a.Id, "Moonrise", "");
        var guest = h.Anonymous();

        var r = JsonDocument.Parse(await (await guest.GetAsync("/v1/search?q=moonrise&type=stories")).Content.ReadAsStringAsync()).RootElement;

        Assert.Equal([titlePrefix, titleSub, chapterOnly], r.GetProperty("data").GetProperty("stories").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Artist_ranking_prefers_exact_handle_then_prefix_then_substring_then_fuzzy()
    {
        var h = await IsolatedHost();
        var viewer = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var fuzzy = await Artist(h, "zz_fuzzy", "Clemente Rossi");
        var substring = await Artist(h, "zz_sub", "The Clementine Studio");
        var prefix = await Artist(h, "zz_pre", "Clementine Rose");
        var exact = await Artist(h, "clementine", "Zed");

        var ids = Ids(await viewer.Get("/v1/search?q=clementine&type=artists"), "artists");

        Assert.Equal([exact.Id, prefix.Id, substring.Id], ids.Take(3));
        Assert.Equal(fuzzy.Id, ids[3]);
    }

    [Fact]
    public async Task Excludes_drafts_auto_blocked_removed_and_blocked_users()
    {
        var h = await IsolatedHost();
        var viewer = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var a = await Artist(h, ProfileTestHost.UniqueHandle("s"), "Shown Sculptor");
        var blockedByViewer = await Artist(h, ProfileTestHost.UniqueHandle("s"), "Hidden Sculptor");
        var blocksViewer = await Artist(h, ProfileTestHost.UniqueHandle("s"), "Hider Sculptor");
        var suspended = await Artist(h, ProfileTestHost.UniqueHandle("s"), "Suspended Sculptor");
        var deleted = await Artist(h, ProfileTestHost.UniqueHandle("s"), "Deleted Sculptor");
        var notOnboarded = await h.SignIn();
        var shown = await Story(h, a.Id, "Sculpt shown");
        foreach (var s in new[] { WorkStatus.Draft, WorkStatus.Pending, WorkStatus.Blocked, WorkStatus.Removed })
            await Story(h, a.Id, "Sculpt " + s, status: s);
        await Story(h, a.Id, "Sculpt reported", reportHidden: true);
        await Story(h, blockedByViewer.Id, "Sculpt by blocked"); await Story(h, blocksViewer.Id, "Sculpt by blocker");
        await Story(h, suspended.Id, "Sculpt by suspended"); await Story(h, deleted.Id, "Sculpt by deleted");
        await using (var db = h.Db())
        {
            await db.Users.Where(u => u.Id == suspended.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));
            await db.Users.Where(u => u.Id == deleted.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        }
        await viewer.Put($"/v1/blocks/{blockedByViewer.Id}");
        await blocksViewer.Put($"/v1/blocks/{viewer.Id}");

        Assert.Equal([a.Id], Ids(await viewer.Get("/v1/search?q=sculptor&type=artists"), "artists"));
        Assert.Equal([shown], Ids(await viewer.Get("/v1/search?q=sculpt&type=stories"), "stories"));
        Assert.DoesNotContain(notOnboarded.Id, Ids(await viewer.Get("/v1/search?q=sculptor&type=artists"), "artists"));
    }

    [Fact]
    public async Task Single_character_query_is_400()
    {
        var h = await IsolatedHost();
        var viewer = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());

        foreach (var url in new[] { "/v1/search?q=a", "/v1/search?q=%20a%20", "/v1/search?q=", "/v1/search" })
        {
            var r = await viewer.Get(url);
            Assert.Equal(HttpStatusCode.BadRequest, r.Status);
            Assert.Equal("query_too_short", r.ErrorCode);
        }
        var tooLong = await viewer.Get("/v1/search?q=" + new string('a', 81));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.Status);
        Assert.Equal("validation_failed", tooLong.ErrorCode);
        Assert.Equal("validation_failed", (await viewer.Get("/v1/search?q=ab&type=bogus")).ErrorCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.Get("/v1/search?q=" + new string('a', 80))).Status);
    }

    [Fact]
    public async Task Query_with_sql_wildcards_and_quotes_is_safe()
    {
        var h = await IsolatedHost();
        var viewer = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var literal = await Artist(h, ProfileTestHost.UniqueHandle("p"), "5% Off", "a_b", "c\\d");
        await Artist(h, ProfileTestHost.UniqueHandle("p"), "5x Off", "axb", "cxd");

        Assert.Equal([literal.Id], Ids(await viewer.Get("/v1/search?q=" + Uri.EscapeDataString("5%")), "artists"));
        Assert.Equal([literal.Id], Ids(await viewer.Get("/v1/search?q=" + Uri.EscapeDataString("a_")), "artists"));
        Assert.Equal([literal.Id], Ids(await viewer.Get("/v1/search?q=" + Uri.EscapeDataString("c\\")), "artists"));
        Assert.Empty(Ids(await viewer.Get("/v1/search?q=" + Uri.EscapeDataString("%%")), "artists"));
        Assert.Empty(Ids(await viewer.Get("/v1/search?q=" + Uri.EscapeDataString("__")), "artists"));

        string[] hostile =
        [
            "%", "_", "\\", "\\\\", "%_%", "' OR 1=1 --", "'; drop table works; --", "\"quoted\"", "a'b\"c", "))((", "ab\0cd",
            new string('é', 81), string.Concat(Enumerable.Repeat("\U0001F600", 41)), string.Concat(Enumerable.Repeat("\U0001F600", 30)), "مرحبا بالعالم", "<script>alert(1)</script>",
            "a b c d e f g h i j", "ab‮cd",
        ];
        foreach (var q in hostile)
            foreach (var type in new[] { "artists", "stories" })
            {
                var r = await viewer.Get($"/v1/search?q={Uri.EscapeDataString(q)}&type={type}");
                Assert.True(r.Status is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"{q.Length}:{type} -> {r.Status}");
            }
        var nul = await viewer.Get("/v1/search?q=ab%00cd");
        Assert.Equal(HttpStatusCode.BadRequest, nul.Status);
        await using var db = h.Db();
        Assert.True(await db.Works.CountAsync() >= 0 && await db.Users.CountAsync() > 0);
    }

    [Fact]
    public async Task Pagination_is_stable()
    {
        var h = await IsolatedHost();
        var viewer = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var owner = await Artist(h, ProfileTestHost.UniqueHandle("pg"), "Pager Owner");
        var artists = new List<Guid>();
        for (var i = 0; i < 9; i++)
            artists.Add((await Artist(h, i % 3 == 0 ? $"pg_{i}" : $"x_{i}", i % 2 == 0 ? "Paging Person" : "The Paging Studio")).Id);
        var stories = new List<Guid>();
        for (var i = 0; i < 8; i++)
            stories.Add(await Story(h, owner.Id, i % 2 == 0 ? $"Paging tale {i}" : $"A tale about paging {i}"));

        var fullArtists = await AllPages(viewer, "paging", "artists", 50);
        var fullStories = await AllPages(viewer, "paging", "stories", 50);

        Assert.Equal(artists.Count, fullArtists.Count(artists.Contains));
        Assert.Equal(stories.Count, fullStories.Count);
        foreach (var limit in new[] { 1, 2, 3, 4, 7 })
        {
            Assert.Equal(fullArtists, await AllPages(viewer, "paging", "artists", limit));
            Assert.Equal(fullStories, await AllPages(viewer, "paging", "stories", limit));
        }
        Assert.Equal(fullStories.Count, fullStories.Distinct().Count());
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.Get("/v1/search?q=paging&cursor=garbage")).Status);
        Assert.Equal("invalid_cursor", (await viewer.Get("/v1/search?q=paging&cursor=garbage")).ErrorCode);
    }

    [Fact]
    public async Task Artist_results_carry_is_following_for_signed_in_viewers_only()
    {
        var h = await IsolatedHost();
        var viewer = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var a = await Artist(h, "followable", "Followable One");
        await viewer.Put($"/v1/artists/{a.Id}/follow");

        var signedIn = await viewer.Get("/v1/search?q=followable&type=artists");
        var guest = JsonDocument.Parse(await (await h.Anonymous().GetAsync("/v1/search?q=followable&type=artists")).Content.ReadAsStringAsync()).RootElement;

        Assert.True(signedIn.Data.GetProperty("artists")[0].GetProperty("isFollowing").GetBoolean());
        Assert.Equal(JsonValueKind.Null, guest.GetProperty("data").GetProperty("artists")[0].GetProperty("isFollowing").ValueKind);
        Assert.Empty(signedIn.Data.GetProperty("stories").EnumerateArray());
    }
}
