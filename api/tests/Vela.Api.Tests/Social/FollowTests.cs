using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Social;

public sealed class RecordingFollowHandler : IDomainEventHandler<Followed>
{
    public static readonly List<Followed> Seen = [];
    public Task HandleAsync(Followed evt, CancellationToken ct) { lock (Seen) Seen.Add(evt); return Task.CompletedTask; }
}

[Collection(PostgresCollection.Name)]
public class FollowTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    private ProfileTestHost Host(string? cs = null)
    {
        var h = new ProfileTestHost(pg, null, cs, s => s.AddScoped<IDomainEventHandler<Followed>, RecordingFollowHandler>());
        _hosts.Add(h);
        return h;
    }
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private static Task<TestUser> User(ProfileTestHost h) => h.SignInOnboarded(ProfileTestHost.UniqueHandle());

    private static async Task<int> FollowRows(ProfileTestHost h, Guid follower, Guid followee)
    {
        await using var db = h.Db();
        return await db.Follows.CountAsync(f => f.FollowerId == follower && f.FolloweeId == followee);
    }

    [Fact]
    public async Task Follow_is_idempotent_and_cannot_follow_self()
    {
        var h = Host(); var a = await User(h); var b = await User(h);
        RecordingFollowHandler.Seen.Clear();

        var first = await a.Put($"/v1/artists/{b.Id}/follow");
        var second = await a.Put($"/v1/artists/{b.Id}/follow");
        var self = await a.Put($"/v1/artists/{a.Id}/follow");

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(1, await FollowRows(h, a.Id, b.Id));
        Assert.Equal(HttpStatusCode.BadRequest, self.Status);
        Assert.Equal("cannot_follow_self", self.ErrorCode);
        Assert.Single(RecordingFollowHandler.Seen, e => e.FollowerId == a.Id && e.ArtistId == b.Id);
    }

    [Fact]
    public async Task Unfollow_is_idempotent_and_requires_sign_in()
    {
        var h = Host(); var a = await User(h); var b = await User(h);
        await a.Put($"/v1/artists/{b.Id}/follow");

        var first = await a.Delete($"/v1/artists/{b.Id}/follow");
        var second = await a.Delete($"/v1/artists/{b.Id}/follow");
        var guest = await h.Anonymous().PutAsync($"/v1/artists/{b.Id}/follow", null);

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(0, await FollowRows(h, a.Id, b.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, guest.StatusCode);
    }

    [Fact]
    public async Task Follow_of_unknown_deleted_suspended_or_unonboarded_user_is_404()
    {
        var h = Host(); var a = await User(h);
        var deleted = await User(h); var suspended = await User(h); var raw = await h.SignIn();
        await using (var db = h.Db())
        {
            await db.Users.Where(u => u.Id == deleted.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
            await db.Users.Where(u => u.Id == suspended.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));
        }

        foreach (var id in new[] { Guid.NewGuid(), deleted.Id, suspended.Id, raw.Id })
        {
            var r = await a.Put($"/v1/artists/{id}/follow");
            Assert.Equal(HttpStatusCode.NotFound, r.Status);
            Assert.Equal("artist_not_found", r.ErrorCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await a.Put("/v1/artists/not-a-guid/follow")).Status);
    }

    [Fact]
    public async Task Follow_with_a_block_in_either_direction_is_404_and_creates_nothing()
    {
        var h = Host(); var a = await User(h); var b = await User(h); var c = await User(h);
        await a.Put($"/v1/blocks/{b.Id}");
        await c.Put($"/v1/blocks/{a.Id}");

        var blocked = await a.Put($"/v1/artists/{b.Id}/follow");
        var blockedBy = await a.Put($"/v1/artists/{c.Id}/follow");
        var unknown = await a.Put($"/v1/artists/{Guid.NewGuid()}/follow");

        Assert.Equal(HttpStatusCode.NotFound, blocked.Status);
        Assert.Equal("artist_not_found", blocked.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, blockedBy.Status);
        Assert.Equal(unknown.Body.GetProperty("error").GetProperty("message").GetString(), blocked.Body.GetProperty("error").GetProperty("message").GetString());
        await using var db = h.Db();
        Assert.False(await db.Follows.AnyAsync(f => f.FollowerId == a.Id));
    }

    [Fact]
    public async Task Concurrent_block_and_follow_never_leave_a_follow_next_to_a_block()
    {
        var h = Host();
        for (var round = 0; round < 12; round++)
        {
            var a = await User(h); var b = await User(h);
            var tasks = new List<Task>();
            for (var i = 0; i < 3; i++)
            {
                tasks.Add(a.Put($"/v1/artists/{b.Id}/follow"));
                tasks.Add(b.Put($"/v1/artists/{a.Id}/follow"));
                tasks.Add(a.Put($"/v1/blocks/{b.Id}"));
                tasks.Add(b.Put($"/v1/blocks/{a.Id}"));
            }
            await Task.WhenAll(tasks);

            await using var db = h.Db();
            var blocked = await db.Blocks.AnyAsync(x => (x.BlockerId == a.Id && x.BlockedId == b.Id) || (x.BlockerId == b.Id && x.BlockedId == a.Id));
            var followed = await db.Follows.AnyAsync(f => (f.FollowerId == a.Id && f.FolloweeId == b.Id) || (f.FollowerId == b.Id && f.FolloweeId == a.Id));
            Assert.False(blocked && followed, $"round {round}: follow row next to a block row");
            Assert.True(blocked);
        }
    }

    [Fact]
    public async Task Followers_list_is_newest_first_paged_and_marks_is_following()
    {
        var h = Host(await pg.CreateIsolatedDatabaseAsync());
        var star = await User(h); var viewer = await User(h);
        var fans = new List<TestUser>();
        for (var i = 0; i < 5; i++) fans.Add(await User(h));
        await using (var db = h.Db())
        {
            for (var i = 0; i < 5; i++)
                db.Follows.Add(new Follow { FollowerId = fans[i].Id, FolloweeId = star.Id, CreatedAt = ProfileTestHost.Now.AddMinutes(i) });
            db.Follows.Add(new Follow { FollowerId = viewer.Id, FolloweeId = fans[1].Id, CreatedAt = ProfileTestHost.Now });
            await db.SaveChangesAsync();
        }

        var p1 = await viewer.Get($"/v1/artists/{star.Id}/followers?limit=3");
        var cursor = p1.Body.GetProperty("page").GetProperty("nextCursor").GetString();
        var p2 = await viewer.Get($"/v1/artists/{star.Id}/followers?limit=3&cursor={cursor}");

        var ids = p1.Data.EnumerateArray().Concat(p2.Data.EnumerateArray()).Select(e => e.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(fans.AsEnumerable().Reverse().Select(f => f.Id), ids);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, p2.Body.GetProperty("page").ValueKind);
        var flagged = p1.Data.EnumerateArray().Concat(p2.Data.EnumerateArray())
            .Where(e => e.GetProperty("isFollowing").ValueKind == System.Text.Json.JsonValueKind.True).Select(e => e.GetProperty("id").GetGuid());
        Assert.Equal([fans[1].Id], flagged);
    }

    [Fact]
    public async Task Followers_list_excludes_hidden_suspended_and_deleted_users_and_rejects_bad_cursor()
    {
        var h = Host(await pg.CreateIsolatedDatabaseAsync());
        var star = await User(h); var viewer = await User(h);
        var ok = await User(h); var blocked = await User(h); var suspended = await User(h); var gone = await User(h);
        await using (var db = h.Db())
        {
            foreach (var f in new[] { ok, blocked, suspended, gone })
                db.Follows.Add(new Follow { FollowerId = f.Id, FolloweeId = star.Id, CreatedAt = ProfileTestHost.Now });
            await db.SaveChangesAsync();
            await db.Users.Where(u => u.Id == suspended.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));
            await db.Users.Where(u => u.Id == gone.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        }
        await viewer.Put($"/v1/blocks/{blocked.Id}");

        var r = await viewer.Get($"/v1/artists/{star.Id}/followers");
        var bad = await viewer.Get($"/v1/artists/{star.Id}/followers?cursor=garbage");
        var guest = await h.Anonymous().GetAsync($"/v1/artists/{star.Id}/followers");

        Assert.Equal([ok.Id], r.Data.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal("invalid_cursor", bad.ErrorCode);
        Assert.Equal(HttpStatusCode.Unauthorized, guest.StatusCode);
    }

    [Fact]
    public async Task Followers_of_a_blocked_or_unknown_artist_is_404()
    {
        var h = Host(); var a = await User(h); var b = await User(h);
        await b.Put($"/v1/blocks/{a.Id}");

        Assert.Equal(HttpStatusCode.NotFound, (await a.Get($"/v1/artists/{b.Id}/followers")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Get($"/v1/artists/{Guid.NewGuid()}/followers")).Status);
    }
}
