using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Profiles;

[Collection(PostgresCollection.Name)]
public class ArtistEndpointTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg);
    public void Dispose() => _host.Dispose();

    private async Task<(JsonElement Data, HttpStatusCode Status, JsonElement Body)> Anon(string url)
    {
        var r = await _host.Anonymous().GetAsync(url);
        var body = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
        return (body.GetProperty("data"), r.StatusCode, body);
    }

    private static async Task<(JsonElement Data, HttpStatusCode Status, JsonElement Body)> AnonOn(ProfileTestHost host, string url)
    {
        var r = await host.Anonymous().GetAsync(url);
        var body = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
        return (body.GetProperty("data"), r.StatusCode, body);
    }

    private async Task<TestUser> OnboardedWithEmail(string handle, string email)
    {
        var u = await _host.SignIn(email);
        Assert.Equal(HttpStatusCode.OK, (await u.Onboard(handle)).Status);
        return u;
    }

    [Fact]
    public async Task Public_artist_response_never_contains_login_email()
    {
        var handle = ProfileTestHost.UniqueHandle();
        var u = await OnboardedWithEmail(handle, "private-login@secret.test");
        await u.Patch("/v1/me/profile", new { publicEmail = "contact@studio.test", bio = "hello" });

        var (data, status, body) = await Anon($"/v1/artists/{handle}");

        Assert.Equal(HttpStatusCode.OK, status);
        var raw = body.GetRawText();
        Assert.DoesNotContain("private-login@secret.test", raw);
        Assert.DoesNotContain("identit", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fake", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("contact@studio.test", data.GetProperty("publicEmail").GetString());
        Assert.Equal(handle, data.GetProperty("handle").GetString());
        Assert.Equal("hello", data.GetProperty("bio").GetString());
    }

    [Fact]
    public async Task Artist_lookup_is_case_insensitive()
    {
        var handle = ProfileTestHost.UniqueHandle();
        await _host.SignInOnboarded(handle);
        var (_, status, _) = await Anon($"/v1/artists/{handle.ToUpperInvariant()}");
        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task Unknown_not_onboarded_deleted_and_suspended_artists_are_404()
    {
        var gone = ProfileTestHost.UniqueHandle();
        var del = await _host.SignInOnboarded(gone);
        var susp = ProfileTestHost.UniqueHandle();
        var s = await _host.SignInOnboarded(susp);
        await using (var db = _host.Db())
        {
            await db.Users.Where(x => x.Id == del.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
            await db.Users.Where(x => x.Id == s.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));
        }
        var half = await _host.SignIn();
        await using (var db = _host.Db())
        {
            db.Profiles.Add(new Profile { UserId = half.Id, Handle = ProfileTestHost.UniqueHandle("h"), DisplayName = "H" });
            await db.SaveChangesAsync();
        }
        var halfHandle = (await _host.Db().Profiles.SingleAsync(p => p.UserId == half.Id)).Handle;

        foreach (var h in new[] { "nobody_here", gone, susp, halfHandle })
        {
            var (_, status, body) = await Anon($"/v1/artists/{h}");
            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.Equal("artist_not_found", body.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Artist_counts_and_is_following_reflect_the_viewer()
    {
        var handle = ProfileTestHost.UniqueHandle();
        var artist = await _host.SignInOnboarded(handle);
        var fan = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var other = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var published = Guid.NewGuid();
        await using (var db = _host.Db())
        {
            db.Follows.Add(new Follow { FollowerId = fan.Id, FolloweeId = artist.Id, CreatedAt = ProfileTestHost.Now });
            db.Follows.Add(new Follow { FollowerId = artist.Id, FolloweeId = other.Id, CreatedAt = ProfileTestHost.Now });
            db.Works.Add(new Work { Id = published, ArtistId = artist.Id, Status = WorkStatus.Published, CreatedAt = ProfileTestHost.Now, PublishedAt = ProfileTestHost.Now, Width = 1, Height = 1, ImageBlob = "b" });
            db.Works.Add(new Work { Id = Guid.NewGuid(), ArtistId = artist.Id, Status = WorkStatus.Draft, CreatedAt = ProfileTestHost.Now, Width = 1, Height = 1, ImageBlob = "b" });
            db.Chapters.AddRange(
                new Chapter { WorkId = published, Index = 0, X = 0.1, Y = 0.1, Title = "a", Body = "b" },
                new Chapter { WorkId = published, Index = 1, X = 0.2, Y = 0.2, Title = "c", Body = "d" });
            await db.SaveChangesAsync();
        }

        var asFan = await fan.Get($"/v1/artists/{handle}");
        var asOther = await other.Get($"/v1/artists/{handle}");
        var asSelf = await artist.Get($"/v1/artists/{handle}");
        var (anon, _, _) = await Anon($"/v1/artists/{handle}");

        Assert.Equal(1, asFan.Data.GetProperty("followerCount").GetInt32());
        Assert.Equal(1, asFan.Data.GetProperty("followingCount").GetInt32());
        Assert.Equal(1, asFan.Data.GetProperty("workCount").GetInt32());
        Assert.Equal(2, asFan.Data.GetProperty("chapterCount").GetInt32());
        Assert.True(asFan.Data.GetProperty("isFollowing").GetBoolean());
        Assert.False(asOther.Data.GetProperty("isFollowing").GetBoolean());
        Assert.Equal(JsonValueKind.Null, asSelf.Data.GetProperty("isFollowing").ValueKind);
        Assert.Equal(JsonValueKind.Null, anon.GetProperty("isFollowing").ValueKind);
    }

    [Fact]
    public async Task A_bad_token_on_a_public_endpoint_is_treated_as_anonymous()
    {
        var handle = ProfileTestHost.UniqueHandle();
        await _host.SignInOnboarded(handle);
        var c = _host.Anonymous();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "garbage");

        var r = await c.GetAsync($"/v1/artists/{handle}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Suggested_orders_by_followers_then_newest_and_excludes_viewer_and_followed()
    {
        // A global top-12 on the shared database is flaky, so this test owns an isolated database.
        using var host = new ProfileTestHost(pg, null, await pg.CreateIsolatedDatabaseAsync());
        var popular = await host.SignInOnboarded(ProfileTestHost.UniqueHandle("pop"));
        var fan1 = await host.SignInOnboarded(ProfileTestHost.UniqueHandle("f"));
        var fan2 = await host.SignInOnboarded(ProfileTestHost.UniqueHandle("f"));
        await using (var db = host.Db())
        {
            db.Follows.Add(new Follow { FollowerId = fan1.Id, FolloweeId = popular.Id, CreatedAt = ProfileTestHost.Now });
            db.Follows.Add(new Follow { FollowerId = fan2.Id, FolloweeId = popular.Id, CreatedAt = ProfileTestHost.Now });
            await db.SaveChangesAsync();
        }
        var viewer = await host.SignInOnboarded(ProfileTestHost.UniqueHandle("v"));
        var followed = await host.SignInOnboarded(ProfileTestHost.UniqueHandle("fl"));
        await using (var db = host.Db())
        {
            db.Follows.Add(new Follow { FollowerId = viewer.Id, FolloweeId = followed.Id, CreatedAt = ProfileTestHost.Now });
            // The viewer and the followed artist outrank everyone, so the exclusions are really exercised.
            foreach (var fan in new[] { fan1, fan2, popular })
            {
                db.Follows.Add(new Follow { FollowerId = fan.Id, FolloweeId = viewer.Id, CreatedAt = ProfileTestHost.Now });
                db.Follows.Add(new Follow { FollowerId = fan.Id, FolloweeId = followed.Id, CreatedAt = ProfileTestHost.Now });
            }
            await db.SaveChangesAsync();
        }

        var anon = await AnonOn(host, "/v1/artists/suggested");
        var mine = await viewer.Get("/v1/artists/suggested");

        var anonIds = anon.Data.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        var mineIds = mine.Data.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        Assert.True(anonIds.Count <= 12);
        Assert.Contains(viewer.Id, anonIds);
        Assert.Contains(followed.Id, anonIds);
        Assert.Contains(popular.Id, mineIds);
        Assert.DoesNotContain(viewer.Id, mineIds);
        Assert.DoesNotContain(followed.Id, mineIds);
        Assert.Equal(0, anonIds.IndexOf(followed.Id) < anonIds.IndexOf(popular.Id) ? 0 : 1); // 4 followers before 2
        Assert.Equal(0, mineIds.IndexOf(popular.Id)); // most followers first
        Assert.DoesNotContain("@", mine.Body.GetRawText());
    }

    [Fact]
    public async Task Suggested_excludes_suspended_deleted_and_not_onboarded()
    {
        var susp = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle("s"));
        var del = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle("d"));
        var fresh = await _host.SignIn();
        await using (var db = _host.Db())
        {
            await db.Users.Where(x => x.Id == susp.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));
            await db.Users.Where(x => x.Id == del.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        }

        var (data, _, _) = await Anon("/v1/artists/suggested");

        var ids = data.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(susp.Id, ids);
        Assert.DoesNotContain(del.Id, ids);
        Assert.DoesNotContain(fresh.Id, ids);
    }
}
