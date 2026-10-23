using Reply = Vela.Api.Tests.Profiles.Reply;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Common;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Notifications.NotificationKit;

namespace Vela.Api.Tests.Notifications;

[Collection(PostgresCollection.Name)]
public class NotificationTests(PostgresFixture pg) : IDisposable
{
    private readonly List<Profiles.ProfileTestHost> _hosts = [];
    private Profiles.ProfileTestHost Host(string? cs = null, Action<IServiceCollection>? extra = null)
    {
        var h = NewHost(pg, cs, extra);
        _hosts.Add(h);
        return h;
    }
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    [Fact]
    public async Task Reply_creates_notification_for_work_owner_but_not_for_self_reply()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id, "Harbor");

        await fan.Post(ReplyUrl(work), new { body = "lovely" });
        await owner.Post(ReplyUrl(work), new { body = "thanks" });

        var rows = await Rows(h, owner.Id);
        var row = Assert.Single(rows);
        Assert.Equal("Reply", row.Type);
        Assert.Equal(fan.Id, row.ActorId);
        Assert.Equal(work, row.WorkId);
        Assert.Equal(0, row.ChapterIndex);
        Assert.NotNull(row.ReplyId);
        Assert.Empty(await Rows(h, fan.Id));
    }

    [Fact]
    public async Task Reply_does_not_notify_a_blocked_pair_or_a_suspended_owner()
    {
        var h = Host(); var owner = await User(h); var blocker = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id);
        await owner.Put($"/v1/blocks/{blocker.Id}");

        var blocked = await blocker.Post(ReplyUrl(work), new { body = "hi" });
        await SetUser(h, owner.Id, u => u.SuspendedAt = Profiles.ProfileTestHost.Now);
        var other = await fan.Post(ReplyUrl(work), new { body = "hi" });

        Assert.Equal(HttpStatusCode.NotFound, blocked.Status);
        Assert.Equal(HttpStatusCode.NotFound, other.Status);
        Assert.Empty(await Rows(h, owner.Id));
    }

    [Fact]
    public async Task Handlers_skip_a_missing_recipient_without_throwing()
    {
        var h = Host(await pg.CreateIsolatedDatabaseAsync()); var fan = await User(h);
        using var scope = h.Scope();
        var bus = scope.ServiceProvider.GetRequiredService<IDomainEventBus>();

        await bus.PublishAsync(new ReplyAdded(Guid.NewGuid(), Guid.NewGuid(), fan.Id), CancellationToken.None);
        await bus.PublishAsync(new Followed(fan.Id, Guid.NewGuid()), CancellationToken.None);
        await bus.PublishAsync(new WorkPublished(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await using var db = h.Db();
        Assert.False(await db.Notifications.AnyAsync());
    }

    [Fact]
    public async Task Follow_creates_follower_notification_once_and_not_for_blocked()
    {
        var h = Host(); var artist = await User(h); var fan = await User(h);

        await fan.Put($"/v1/artists/{artist.Id}/follow");
        await fan.Put($"/v1/artists/{artist.Id}/follow");

        var row = Assert.Single(await Rows(h, artist.Id));
        Assert.Equal("Follower", row.Type);
        Assert.Equal(fan.Id, row.ActorId);
        var item = Assert.Single(Array(await Items(artist)));
        Assert.Equal("follow", item.GetProperty("type").GetString());
        Assert.Equal(fan.Id, item.GetProperty("actor").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task New_story_notifies_followers_not_blocked_users()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var h = Host(cs); var artist = await User(h);
        var ok = await User(h); var blocked = await User(h); var suspended = await User(h); var deleted = await User(h); var stranger = await User(h);
        foreach (var f in new[] { ok, blocked, suspended, deleted }) await f.Put($"/v1/artists/{artist.Id}/follow");
        await artist.Put($"/v1/blocks/{blocked.Id}");
        await blocked.Put($"/v1/artists/{artist.Id}/follow");
        await SetUser(h, suspended.Id, u => u.SuspendedAt = Profiles.ProfileTestHost.Now);
        await SetUser(h, deleted.Id, u => u.DeletedAt = Profiles.ProfileTestHost.Now);
        var work = await SeedWork(h, artist.Id, "Fresh");

        await PublishEvent(h, work, artist.Id);
        await PublishEvent(h, work, artist.Id);

        await using var db = h.Db();
        var story = await db.Notifications.Where(n => n.Type == "NewStory").ToListAsync();
        Assert.Equal([ok.Id], story.Select(n => n.UserId).ToList());
        Assert.Equal(work, story[0].WorkId);
        Assert.Equal(artist.Id, story[0].ActorId);
        Assert.Empty(await Rows(h, stranger.Id));
        var item = Assert.Single(Array(await Items(ok)));
        Assert.Equal("new_story", item.GetProperty("type").GetString());
        Assert.Equal("Fresh", item.GetProperty("workTitle").GetString());
    }

    [Fact]
    public async Task Unread_count_matches_and_mark_read_clears_it()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id);
        await fan.Post(ReplyUrl(work), new { body = "one" });
        await fan.Post(ReplyUrl(work, 1), new { body = "two" });
        await fan.Put($"/v1/artists/{owner.Id}/follow");
        var ids = (await Rows(h, owner.Id)).Select(n => n.Id).ToList();
        Assert.Equal(3, await Unread(owner));

        var foreign = await fan.Post("/v1/notifications/read", new { ids });
        var some = await owner.Post("/v1/notifications/read", new { ids = new[] { ids[0], Guid.NewGuid() } });
        var again = await owner.Post("/v1/notifications/read", new { ids = new[] { ids[0] } });

        Assert.Equal(HttpStatusCode.OK, foreign.Status);
        Assert.Equal(HttpStatusCode.OK, some.Status);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(2, await Unread(owner));
        Assert.All(await Rows(h, fan.Id), n => Assert.Null(n.ReadAt));
        var first = (await Rows(h, owner.Id)).Single(n => n.Id == ids[0]);
        Assert.Equal(Profiles.ProfileTestHost.Now, first.ReadAt);

        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await owner.Post("/v1/notifications/read", new { });
        Assert.Equal(0, await Unread(owner));
        Assert.Equal(Profiles.ProfileTestHost.Now, (await Rows(h, owner.Id)).Single(n => n.Id == ids[0]).ReadAt);
        Assert.All(Array(await Items(owner)), i => Assert.True(i.GetProperty("read").GetBoolean()));
    }

    [Fact]
    public async Task Mark_read_rejects_malformed_bodies_and_guests()
    {
        var h = Host(); var u = await User(h);

        Assert.Equal(HttpStatusCode.BadRequest, (await u.Post("/v1/notifications/read", "not json")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await u.Post("/v1/notifications/read", new { ids = new[] { "nope" } })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Anonymous().GetAsync("/v1/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Anonymous().GetAsync("/v1/notifications/unread-count")).StatusCode);
    }

    [Fact]
    public async Task Consecutive_replies_on_same_chapter_group_into_one_item()
    {
        var h = Host(); var owner = await User(h); var work = await SeedWork(h, owner.Id, "Harbor");
        var fans = new List<Profiles.TestUser>();
        for (var i = 0; i < 3; i++) { fans.Add(await User(h)); h.Clock.Advance(TimeSpan.FromMinutes(1)); await fans[i].Post(ReplyUrl(work, 0), new { body = "r" + i }); }
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await fans[0].Post(ReplyUrl(work, 1), new { body = "other chapter" });

        var items = Array(await Items(owner));

        Assert.Equal(2, items.Count);
        Assert.Equal(1, items[0].GetProperty("count").GetInt32());
        Assert.Equal(1, items[0].GetProperty("chapterIndex").GetInt32());
        var grouped = items[1];
        Assert.Equal(3, grouped.GetProperty("count").GetInt32());
        Assert.Equal(3, grouped.GetProperty("ids").GetArrayLength());
        Assert.Equal("Harbor", grouped.GetProperty("workTitle").GetString());
        Assert.Equal(fans[2].Id, grouped.GetProperty("actor").GetProperty("id").GetGuid());
        Assert.Equal(4, await Unread(owner));

        var ids = grouped.GetProperty("ids").EnumerateArray().Select(e => e.GetGuid()).ToList();
        await owner.Post("/v1/notifications/read", new { ids });
        Assert.Equal(1, await Unread(owner));
        Assert.All(Array(await Items(owner)), i => Assert.Equal(1, i.GetProperty("count").GetInt32()));
    }

    [Fact]
    public async Task Unread_replies_separated_by_another_notification_do_not_group()
    {
        var h = Host(); var owner = await User(h); var work = await SeedWork(h, owner.Id);
        var a = await User(h); var b = await User(h);
        await a.Post(ReplyUrl(work), new { body = "1" });
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await b.Put($"/v1/artists/{owner.Id}/follow");
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await b.Post(ReplyUrl(work), new { body = "2" });

        Assert.Equal(3, Array(await Items(owner)).Count);
    }

    [Fact]
    public async Task Rows_with_missing_hidden_or_blocked_targets_are_filtered_from_list_and_count()
    {
        var h = Host(); var owner = await User(h); var fan = await User(h); var gone = await User(h); var blocked = await User(h);
        var shown = await SeedWork(h, owner.Id, "Shown"); var hiddenWork = await SeedWork(h, owner.Id, "Hidden");
        await fan.Post(ReplyUrl(shown), new { body = "a" });
        await fan.Post(ReplyUrl(hiddenWork), new { body = "b" });
        await gone.Post(ReplyUrl(shown, 1), new { body = "c" });
        await blocked.Post(ReplyUrl(shown, 1), new { body = "d" });
        Assert.Equal(4, await Unread(owner));

        await using (var db = h.Db())
            await db.Works.Where(w => w.Id == hiddenWork).ExecuteUpdateAsync(s => s.SetProperty(w => w.ReportHidden, true));
        await SetUser(h, gone.Id, u => u.SuspendedAt = Profiles.ProfileTestHost.Now);
        await owner.Put($"/v1/blocks/{blocked.Id}");

        var items = Array(await Items(owner));
        Assert.Single(items);
        Assert.Equal("Shown", items[0].GetProperty("workTitle").GetString());
        Assert.Equal(1, await Unread(owner));

        await using var db2 = h.Db();
        await db2.Works.Where(w => w.Id == shown).ExecuteDeleteAsync();
        Assert.Empty(Array(await Items(owner)));
        Assert.Equal(0, await Unread(owner));
    }

    [Fact]
    public async Task List_pages_newest_first_with_cursor_and_rejects_bad_cursor()
    {
        var h = Host(); var owner = await User(h);
        for (var i = 0; i < 5; i++) { var f = await User(h); h.Clock.Advance(TimeSpan.FromMinutes(1)); await f.Put($"/v1/artists/{owner.Id}/follow"); }

        var p1 = await Items(owner, "?limit=2");
        var cursor = p1.Body.GetProperty("page").GetProperty("nextCursor").GetString();
        var p2 = await Items(owner, $"?limit=2&cursor={cursor}");
        var p3 = await Items(owner, $"?limit=2&cursor={p2.Body.GetProperty("page").GetProperty("nextCursor").GetString()}");

        var all = Array(p1).Concat(Array(p2)).Concat(Array(p3)).Select(i => i.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(5, all.Count);
        Assert.Equal(all.OrderByDescending(x => x).ToList(), all);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, p3.Body.GetProperty("page").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await Items(owner, "?cursor=garbage")).Status);
    }

    [Fact]
    public async Task Notification_not_created_if_transaction_rolls_back()
    {
        var h = Host(null, s => s.AddScoped<IDomainEventHandler<ReplyAdded>, ThrowAfterNotifyHandler>());
        var owner = await User(h); var fan = await User(h); var work = await SeedWork(h, owner.Id);

        var failed = await fan.Post(ReplyUrl(work), new { body = "will roll back" });

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.Equal("internal_error", failed.ErrorCode);

        Assert.Empty(await Rows(h, owner.Id));
    }
}
