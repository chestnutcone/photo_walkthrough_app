using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Security.SecurityWorld;

namespace Vela.Api.Tests.Security;

/// <summary>Clients may send extra fields; none of them may set privileged state (admin flag, tier and quota, handle lock, moderation flags, ownership).</summary>
[Collection(PostgresCollection.Name)]
public class MassAssignmentTests(PostgresFixture pg)
{
    private const string Privileged = """
        "isAdmin":true,"IsAdmin":true,"tier":"pro","tierId":"00000000-0000-0000-0000-000000000009","maxWorks":9999,"usedWorks":0,"quota":1000,
        "handleChangedAt":"2000-01-01T00:00:00Z","handleLocked":false,"suspendedAt":null,"deletedAt":null,"id":"00000000-0000-0000-0000-000000000001",
        "userId":"00000000-0000-0000-0000-000000000001","followerCount":99999,"onboardingComplete":true,"ageConfirmedAt":"2000-01-01T00:00:00Z"
        """;

    [Fact]
    public async Task Patch_profile_cannot_set_admin_flag_tier_quota_or_the_handle_lock()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var me = await w.NewUserAsync();
        string handleBefore;
        DateTimeOffset? lockBefore;
        Guid tierBefore;
        await using (var db = w.Host.Db())
        {
            handleBefore = (await db.Profiles.AsNoTracking().SingleAsync(p => p.UserId == me.Id)).Handle;
            lockBefore = (await db.Profiles.AsNoTracking().SingleAsync(p => p.UserId == me.Id)).HandleChangedAt;
            tierBefore = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == me.Id)).TierId;
        }

        var (status, text, _) = await SendAsync(me.Client, HttpMethod.Patch, "/v1/me/profile", "{\"bio\":\"new bio\"," + Privileged + "}");

        Assert.Equal(HttpStatusCode.OK, status);
        await using var after = w.Host.Db();
        var user = await after.Users.AsNoTracking().SingleAsync(u => u.Id == me.Id);
        var profile = await after.Profiles.AsNoTracking().SingleAsync(p => p.UserId == me.Id);
        Assert.False(user.IsAdmin);
        Assert.Equal(tierBefore, user.TierId);
        Assert.Equal(me.Id, user.Id);
        Assert.Equal(handleBefore, profile.Handle);
        Assert.Equal(lockBefore, profile.HandleChangedAt);
        Assert.Equal("new bio", profile.Bio);
        var me2 = JsonDocument.Parse(text).RootElement.GetProperty("data");
        Assert.NotEqual(9999, me2.GetProperty("usedWorks").GetInt32());
        w.Dispose();
    }

    [Fact]
    public async Task A_second_handle_change_inside_30_days_is_refused_even_when_the_body_claims_the_lock_is_open()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var me = await w.NewUserAsync();
        Assert.Equal(HttpStatusCode.OK, (await me.Patch("/v1/me/profile", new { handle = Vela.Api.Tests.Profiles.ProfileTestHost.UniqueHandle("c1") })).Status);

        var (status, _, _) = await SendAsync(me.Client, HttpMethod.Patch, "/v1/me/profile",
            "{\"handle\":\"" + Vela.Api.Tests.Profiles.ProfileTestHost.UniqueHandle("c2") + "\",\"handleChangedAt\":\"2000-01-01T00:00:00Z\",\"handleLocked\":false}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        w.Dispose();
    }

    [Fact]
    public async Task Onboarding_cannot_grant_admin_or_a_tier()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var fresh = await w.Host.SignIn();
        var handle = Vela.Api.Tests.Profiles.ProfileTestHost.UniqueHandle("ob");
        var (status, _, _) = await SendAsync(fresh.Client, HttpMethod.Post, "/v1/me/onboarding",
            "{\"displayName\":\"X\",\"handle\":\"" + handle + "\",\"ageConfirmed\":true,\"termsAccepted\":true," + Privileged + "}");

        Assert.Equal(HttpStatusCode.OK, status);
        await using var db = w.Host.Db();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == fresh.Id);
        Assert.False(user.IsAdmin);
        Assert.NotEqual(Guid.Parse("00000000-0000-0000-0000-000000000009"), user.TierId);
        w.Dispose();
    }

    [Fact]
    public async Task Work_and_reply_bodies_cannot_set_status_owner_moderation_flags_or_expiry()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var artist = await w.NewUserAsync();
        var created = await SendAsync(artist.Client, HttpMethod.Post, "/v1/works",
            "{\"contentType\":\"image/jpeg\",\"status\":\"published\",\"artistId\":\"" + w.Bob.Id + "\",\"reportHidden\":false,\"moderationOverride\":true,\"expiresAt\":null}");
        Assert.Equal(HttpStatusCode.OK, created.Status);
        var id = JsonDocument.Parse(created.Text).RootElement.GetProperty("data").GetProperty("workId").GetGuid();

        await using (var db = w.Host.Db())
        {
            var work = await db.Works.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal(artist.Id, work.ArtistId);
            Assert.Equal(WorkStatus.Draft, work.Status);
            Assert.False(work.ModerationOverride);
            Assert.NotNull(work.ExpiresAt);
        }

        var story = await Vela.Api.Tests.Share.ShareTestKit.Published(w.Host, artist, "Mine");
        var reply = await SendAsync(w.Bob.Client, HttpMethod.Post, $"/v1/works/{story}/chapters/0/replies",
            "{\"body\":\"hello\",\"hidden\":true,\"hiddenBy\":\"admin\",\"authorId\":\"" + artist.Id + "\",\"createdAt\":\"2000-01-01T00:00:00Z\"}");
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        var replyId = JsonDocument.Parse(reply.Text).RootElement.GetProperty("data").GetProperty("id").GetGuid();
        await using var after = w.Host.Db();
        var stored = await after.Replies.AsNoTracking().SingleAsync(r => r.Id == replyId);
        Assert.Equal(w.Bob.Id, stored.AuthorId);
        Assert.False(stored.Hidden);
        Assert.True(stored.CreatedAt.Year >= 2026);
        w.Dispose();
    }

    [Fact]
    public async Task Update_work_body_cannot_publish_a_draft_or_change_its_owner()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var artist = await w.NewUserAsync();
        var id = await Vela.Api.Tests.Works.WorkTestKit.ReadyDraft(w.Host, artist);

        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "T", ["chapters"] = new[] { Vela.Api.Tests.Works.WorkTestKit.Chapter() },
            ["status"] = "published", ["artistId"] = w.Bob.Id, ["publishedAt"] = "2000-01-01T00:00:00Z", ["reportHidden"] = true,
        });
        var r = await SendAsync(artist.Client, HttpMethod.Put, $"/v1/works/{id}", body);

        Assert.Equal(HttpStatusCode.OK, r.Status);
        await using var db = w.Host.Db();
        var work = await db.Works.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(WorkStatus.Draft, work.Status);
        Assert.Equal(artist.Id, work.ArtistId);
        Assert.Null(work.PublishedAt);
        Assert.False(work.ReportHidden);
        w.Dispose();
    }
}
