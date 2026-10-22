using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Moderation;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using ReplyRow = Vela.Api.Data.Entities.Reply;
using Reply = Vela.Api.Tests.Profiles.Reply;

namespace Vela.Api.Tests.Social;

public sealed class RecordingReplyHandler : IDomainEventHandler<ReplyAdded>
{
    public static readonly List<ReplyAdded> Seen = [];
    public Task HandleAsync(ReplyAdded evt, CancellationToken ct) { lock (Seen) Seen.Add(evt); return Task.CompletedTask; }
}

public sealed class ThrowingReplyHandler : IDomainEventHandler<ReplyAdded>
{
    public Task HandleAsync(ReplyAdded evt, CancellationToken ct) => throw new InvalidOperationException("handler failed");
}

[Collection(PostgresCollection.Name)]
public class ReplyTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    private ProfileTestHost Host(Action<IServiceCollection>? extra = null)
    {
        var h = new ProfileTestHost(pg, null, null, s =>
        {
            s.AddScoped<IDomainEventHandler<ReplyAdded>, RecordingReplyHandler>();
            extra?.Invoke(s);
        });
        _hosts.Add(h);
        return h;
    }
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private static Task<TestUser> User(ProfileTestHost h) => h.SignInOnboarded(ProfileTestHost.UniqueHandle());

    private static async Task<Guid> SeedWork(ProfileTestHost h, Guid artist, WorkStatus status = WorkStatus.Published, bool reportHidden = false)
    {
        await using var db = h.Db();
        var w = new Work { Id = Guid.NewGuid(), ArtistId = artist, Title = "t", Status = status, ReportHidden = reportHidden, ImageBlob = "b", Width = 3, Height = 4, CreatedAt = ProfileTestHost.Now, PublishedAt = ProfileTestHost.Now };
        db.Works.Add(w);
        for (var i = 0; i < 2; i++) db.Chapters.Add(new Chapter { WorkId = w.Id, Index = i, X = 0.5, Y = 0.5, Title = "c", Body = "b" });
        await db.SaveChangesAsync();
        return w.Id;
    }

    private static string Url(Guid work, int chapter = 0) => $"/v1/works/{work}/chapters/{chapter}/replies";

    private static async Task<ReplyRow> Stored(ProfileTestHost h, Guid replyId)
    {
        await using var db = h.Db();
        return await db.Replies.AsNoTracking().SingleAsync(r => r.Id == replyId);
    }

    private static List<string> Bodies(Reply r) => r.Data.EnumerateArray().Select(e => e.GetProperty("body").GetString()!).ToList();

    [Fact]
    public async Task Reply_empty_or_over_1000_chars_is_refused()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id);
        var secret = "SECRET-" + new string('x', 1001);

        foreach (var bad in new[] { "", "   \n ", secret })
        {
            var r = await fan.Post(Url(work), new { body = bad });
            Assert.Equal(HttpStatusCode.BadRequest, r.Status);
            Assert.Equal("validation_failed", r.ErrorCode);
            Assert.Contains("body", r.Body.GetProperty("error").GetProperty("message").GetString());
            Assert.DoesNotContain("SECRET", r.Body.GetRawText());
        }
        Assert.Equal(HttpStatusCode.Created, (await fan.Post(Url(work), new { body = new string('y', 1000) })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await fan.Post(Url(work), new { body = new string('y', 1001) })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await fan.Post(Url(work), new { })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await fan.Post(Url(work), new { body = 5 })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await fan.Post(Url(work), "not json")).Status);
    }

    [Fact]
    public async Task Control_characters_other_than_newline_are_refused_and_newline_is_kept()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id);

        foreach (var bad in new[] { "a\u0000b", "a\tb", "a\rb", "a\u001bb", "a\u0085b" })
            Assert.Equal("validation_failed", (await fan.Post(Url(work), new { body = bad })).ErrorCode);
        Assert.Equal("validation_failed", (await fan.Post(Url(work), "{\"body\":\"lone \\ud800 surrogate\"}")).ErrorCode);
        var ok = await fan.Post(Url(work), new { body = "  line one\n\nline  two  " });

        Assert.Equal(HttpStatusCode.Created, ok.Status);
        Assert.Equal("line one\n\nline  two", ok.Data.GetProperty("body").GetString());
        Assert.Equal("line one\n\nline  two", (await Stored(h, ok.Data.GetProperty("id").GetGuid())).Text);
    }

    [Fact]
    public async Task Emoji_and_rtl_text_round_trips()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id);
        var texts = new[] { "Love this \U0001F3A8\U0001F44D\U0001F3FD", "שלום עולם مرحبا", "<script>alert(1)</script> & \"quotes\"" };

        foreach (var t in texts)
        {
            h.Clock.Advance(TimeSpan.FromSeconds(1)); // the list is oldest first, so give each reply its own time
            Assert.Equal(HttpStatusCode.Created, (await fan.Post(Url(work), new { body = t })).Status);
        }

        Assert.Equal(texts, Bodies(await h.Anonymous().GetAsync(Url(work)).ContinueWith(async r => await ToReply(r.Result)).Unwrap()));
    }

    private static async Task<Reply> ToReply(HttpResponseMessage r) =>
        new(r.StatusCode, JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone());

    [Fact]
    public async Task Created_reply_has_the_contract_shape_raises_the_event_and_counts_on_the_work()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id);
        RecordingReplyHandler.Seen.Clear();

        var r = await fan.Post(Url(work, 1), new { body = "hello" });

        Assert.Equal(HttpStatusCode.Created, r.Status);
        Assert.Equal(work, r.Data.GetProperty("workId").GetGuid());
        Assert.Equal(1, r.Data.GetProperty("chapterIndex").GetInt32());
        Assert.Equal(fan.Id, r.Data.GetProperty("author").GetProperty("id").GetGuid());
        Assert.False(r.Data.TryGetProperty("hiddenForOthers", out var flag) && flag.GetBoolean());
        var id = r.Data.GetProperty("id").GetGuid();
        Assert.Single(RecordingReplyHandler.Seen, e => e == new ReplyAdded(id, work, fan.Id));
        var counts = (await owner.Get($"/v1/works/{work}")).Data.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("replyCount").GetInt32());
        Assert.Equal([0, 1], counts);
    }

    [Fact]
    public async Task Reply_flagged_by_content_safety_is_hidden_and_queued_for_review()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var other = await User(h); var work = await SeedWork(h, owner.Id);
        RecordingReplyHandler.Seen.Clear();

        var r = await fan.Post(Url(work), new { body = "bad [[unsafe]] words" });

        Assert.Equal(HttpStatusCode.Created, r.Status);
        Assert.True(r.Data.GetProperty("hiddenForOthers").GetBoolean());
        var id = r.Data.GetProperty("id").GetGuid();
        var row = await Stored(h, id);
        Assert.True(row.Hidden);
        Assert.Equal(HiddenBy.Auto, row.HiddenBy);
        await using (var db = h.Db())
        {
            var flag = await db.Reports.AsNoTracking().SingleAsync(x => x.TargetId == id);
            Assert.Equal(("auto_flag", "reply", "auto:unsafe_text:6", "pending"), (flag.Source, flag.TargetType, flag.Reason, flag.Status));
        }
        Assert.DoesNotContain(RecordingReplyHandler.Seen, e => e.ReplyId == id);
        Assert.Empty(Bodies(await other.Get(Url(work))));
        Assert.Empty(Bodies(await owner.Get(Url(work))));
        var mine = await fan.Get(Url(work));
        Assert.Single(Bodies(mine));
        Assert.True(mine.Data[0].GetProperty("hiddenForOthers").GetBoolean());
    }

    [Fact]
    public async Task Scan_outage_stores_the_reply_visible_and_queues_scan_unavailable()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id);
        var safety = h.Services.GetRequiredService<FakeContentSafety>();
        RecordingReplyHandler.Seen.Clear();
        safety.Throw = true;
        Reply r;
        try { r = await fan.Post(Url(work), new { body = "hello" }); } finally { safety.Throw = false; }

        Assert.Equal(HttpStatusCode.Created, r.Status);
        var id = r.Data.GetProperty("id").GetGuid();
        Assert.False((await Stored(h, id)).Hidden);
        await using var db = h.Db();
        Assert.Equal("auto:scan_unavailable:0", (await db.Reports.AsNoTracking().SingleAsync(x => x.TargetId == id)).Reason);
        Assert.Contains(RecordingReplyHandler.Seen, e => e.ReplyId == id);
        Assert.Equal(["hello"], Bodies(await owner.Get(Url(work))));
    }

    [Fact]
    public async Task Reply_reported_by_a_user_is_hidden_from_others_but_visible_to_author()
    {
        var h = Host(); var owner = await User(h); var author = await User(h); var reporter = await User(h); var other = await User(h);
        var work = await SeedWork(h, owner.Id);
        var id = (await author.Post(Url(work), new { body = "borderline" })).Data.GetProperty("id").GetGuid();

        var report = await reporter.Post("/v1/reports", new { targetType = "reply", targetId = id, reason = "spam" });

        Assert.Equal(HttpStatusCode.OK, report.Status);
        Assert.Empty(Bodies(await other.Get(Url(work))));
        Assert.Empty(Bodies(await reporter.Get(Url(work))));
        var mine = await author.Get(Url(work));
        Assert.Equal(["borderline"], Bodies(mine));
        Assert.True(mine.Data[0].GetProperty("hiddenForOthers").GetBoolean());
        var counts = (await other.Get($"/v1/works/{work}")).Data.GetProperty("chapters")[0].GetProperty("replyCount").GetInt32();
        Assert.Equal(0, counts);
    }

    [Fact]
    public async Task Reply_on_nonexistent_chapter_is_404_and_so_is_an_unviewable_work()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var blocker = await User(h);
        var work = await SeedWork(h, owner.Id); var draft = await SeedWork(h, owner.Id, WorkStatus.Draft);
        var hiddenWork = await SeedWork(h, owner.Id, reportHidden: true); var blockerWork = await SeedWork(h, blocker.Id);
        await blocker.Put($"/v1/blocks/{fan.Id}");

        foreach (var index in new[] { 2, 40, 999 })
        {
            Assert.Equal("chapter_not_found", (await fan.Post(Url(work, index), new { body = "x" })).ErrorCode);
            Assert.Equal("chapter_not_found", (await fan.Get(Url(work, index))).ErrorCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await fan.Post(Url(work, 2), new { body = "x" })).Status);
        foreach (var w in new[] { Guid.NewGuid(), draft, hiddenWork, blockerWork })
        {
            Assert.Equal("work_not_found", (await fan.Post(Url(w), new { body = "x" })).ErrorCode);
            Assert.Equal("work_not_found", (await fan.Get(Url(w))).ErrorCode);
        }
        Assert.Equal("work_not_found", (await fan.Get("/v1/works/nope/chapters/0/replies")).ErrorCode);
        Assert.Equal("chapter_not_found", (await fan.Get($"/v1/works/{work}/chapters/abc/replies")).ErrorCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Anonymous().PostAsync(Url(work), null)).StatusCode);
    }

    [Fact]
    public async Task List_is_oldest_first_paged_and_hides_blocked_users_and_hidden_replies()
    {
        var h = Host(); var owner = await User(h); var a = await User(h); var b = await User(h); var viewer = await User(h);
        var work = await SeedWork(h, owner.Id);
        await using (var db = h.Db())
        {
            for (var i = 0; i < 5; i++)
                db.Replies.Add(new ReplyRow { Id = Guid.NewGuid(), WorkId = work, ChapterIndex = 0, AuthorId = a.Id, Text = $"a{i}", CreatedAt = ProfileTestHost.Now.AddMinutes(i % 2) });
            db.Replies.Add(new ReplyRow { Id = Guid.NewGuid(), WorkId = work, ChapterIndex = 0, AuthorId = b.Id, Text = "from-b", CreatedAt = ProfileTestHost.Now });
            db.Replies.Add(new ReplyRow { Id = Guid.NewGuid(), WorkId = work, ChapterIndex = 0, AuthorId = a.Id, Text = "admin-hidden", Hidden = true, HiddenBy = HiddenBy.Admin, CreatedAt = ProfileTestHost.Now });
            db.Replies.Add(new ReplyRow { Id = Guid.NewGuid(), WorkId = work, ChapterIndex = 1, AuthorId = a.Id, Text = "other-chapter", CreatedAt = ProfileTestHost.Now });
            await db.SaveChangesAsync();
        }
        await viewer.Put($"/v1/blocks/{b.Id}");
        var all = new List<string>(); string? cursor = null;
        do
        {
            var r = await viewer.Get($"{Url(work)}?limit=2{(cursor is null ? "" : "&cursor=" + cursor)}");
            all.AddRange(Bodies(r));
            cursor = r.Body.GetProperty("page").ValueKind == JsonValueKind.Null ? null : r.Body.GetProperty("page").GetProperty("nextCursor").GetString();
        } while (cursor is not null);

        Assert.Equal(5, all.Count);
        Assert.Equal(5, all.Distinct().Count());
        Assert.DoesNotContain("from-b", all);
        Assert.DoesNotContain("admin-hidden", all);
        Assert.DoesNotContain("other-chapter", all);
        Assert.Equal(["a0", "a2", "a4"], all.Take(3).Order().ToList()); // the three created first come before the later two
        Assert.Equal("invalid_cursor", (await viewer.Get($"{Url(work)}?cursor=zzz")).ErrorCode);
    }

    [Fact]
    public async Task Author_can_delete_own_reply_and_work_owner_can_delete_any_on_their_work()
    {
        var h = Host(); var owner = await User(h); var author = await User(h); var stranger = await User(h); var work = await SeedWork(h, owner.Id);
        var mine = (await author.Post(Url(work), new { body = "one" })).Data.GetProperty("id").GetGuid();
        var other = (await author.Post(Url(work), new { body = "two" })).Data.GetProperty("id").GetGuid();
        var third = (await author.Post(Url(work), new { body = "three" })).Data.GetProperty("id").GetGuid();

        var strangerTry = await stranger.Delete($"/v1/replies/{mine}");
        var missing = await stranger.Delete($"/v1/replies/{Guid.NewGuid()}");
        var byAuthor = await author.Delete($"/v1/replies/{mine}");
        var byOwner = await owner.Delete($"/v1/replies/{other}");
        var again = await author.Delete($"/v1/replies/{mine}");

        Assert.Equal(HttpStatusCode.NotFound, strangerTry.Status);
        Assert.Equal("reply_not_found", strangerTry.ErrorCode);
        Assert.Equal(missing.Body.GetRawText(), strangerTry.Body.GetRawText());
        Assert.Equal(HttpStatusCode.OK, byAuthor.Status);
        Assert.Equal(HttpStatusCode.OK, byOwner.Status);
        Assert.Equal("reply_not_found", again.ErrorCode);
        Assert.Equal(["three"], Bodies(await stranger.Get(Url(work))));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Delete($"/v1/replies/{third}")).Status);
        Assert.Equal("reply_not_found", (await stranger.Delete("/v1/replies/not-a-guid")).ErrorCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Anonymous().DeleteAsync($"/v1/replies/{third}")).StatusCode);
    }

    [Fact]
    public async Task A_failing_event_handler_fails_the_request_and_stores_nothing()
    {
        var h = Host(s => s.AddScoped<IDomainEventHandler<ReplyAdded>, ThrowingReplyHandler>());
        var owner = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id);

        await Assert.ThrowsAnyAsync<Exception>(() => fan.Post(Url(work), new { body = "will roll back" }));

        await using var db = h.Db();
        Assert.False(await db.Replies.AnyAsync(r => r.WorkId == work));
    }

    [Fact]
    public async Task Replies_by_a_suspended_author_are_not_listed()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id);
        await fan.Post(Url(work), new { body = "hi" });
        await using (var db = h.Db())
            await db.Users.Where(u => u.Id == fan.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));

        Assert.Empty(Bodies(await owner.Get(Url(work))));
    }
}
