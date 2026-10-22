using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Profiles;

[Collection(PostgresCollection.Name)]
public class ProfileOnboardingTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg);
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Me_requires_authentication_with_an_envelope()
    {
        var r = await _host.Anonymous().GetAsync("/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("\"success\":false", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Me_before_onboarding_has_no_profile_and_is_incomplete_and_never_has_the_login_email()
    {
        var u = await _host.SignIn("login@secret.test");

        var me = await u.Get("/v1/me");

        Assert.Equal(HttpStatusCode.OK, me.Status);
        Assert.Equal(u.Id, me.Data.GetProperty("id").GetGuid());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, me.Data.GetProperty("profile").ValueKind);
        Assert.False(me.Data.GetProperty("onboardingComplete").GetBoolean());
        Assert.Equal("Freemium", me.Data.GetProperty("tier").GetProperty("name").GetString());
        Assert.Equal(20, me.Data.GetProperty("tier").GetProperty("maxWorks").GetInt32());
        Assert.Equal(0, me.Data.GetProperty("usedWorks").GetInt32());
        Assert.DoesNotContain("login@secret.test", me.Body.GetRawText());
    }

    [Fact]
    public async Task Onboarding_creates_profile_and_marks_complete()
    {
        var u = await _host.SignIn("login@secret.test");
        var handle = ProfileTestHost.UniqueHandle();

        var r = await u.Onboard(handle, "  Ada Lovelace  ");

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(handle, r.Data.GetProperty("profile").GetProperty("handle").GetString());
        Assert.True(r.Data.GetProperty("onboardingComplete").GetBoolean());
        await using var db = _host.Db();
        var p = await db.Profiles.SingleAsync(x => x.UserId == u.Id);
        Assert.Equal("Ada Lovelace", p.DisplayName);
        Assert.Null(p.HandleChangedAt); // the initial handle is not a change
        var user = await db.Users.SingleAsync(x => x.Id == u.Id);
        Assert.Equal(ProfileTestHost.Now, user.AgeConfirmedAt);
        Assert.Equal(ProfileTestHost.Now, user.TermsAcceptedAt);
        Assert.DoesNotContain("login@secret.test", r.Body.GetRawText());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Onboarding_without_age_or_terms_is_refused(bool age, bool terms)
    {
        var u = await _host.SignIn();

        var r = await u.Onboard(ProfileTestHost.UniqueHandle(), age: age, terms: terms);

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("age_and_terms_required", r.ErrorCode);
        await using var db = _host.Db();
        Assert.False(await db.Profiles.AnyAsync(x => x.UserId == u.Id));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("admin")]
    [InlineData("")]
    public async Task Onboarding_with_invalid_or_reserved_handle_is_refused(string handle)
    {
        var r = await (await _host.SignIn()).Onboard(handle);
        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("invalid_handle", r.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Onboarding_with_empty_display_name_is_refused_without_echoing_input(string name)
    {
        var r = await (await _host.SignIn()).Onboard(ProfileTestHost.UniqueHandle(), name);
        Assert.Equal("validation_failed", r.ErrorCode);
        Assert.Contains("displayName", r.Body.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Onboarding_with_61_char_display_name_is_refused()
    {
        var r = await (await _host.SignIn()).Onboard(ProfileTestHost.UniqueHandle(), new string('x', 61));
        Assert.Equal("validation_failed", r.ErrorCode);
    }

    [Fact]
    public async Task Handle_taken_returns_handle_taken_error_case_insensitively()
    {
        var first = ProfileTestHost.UniqueHandle();
        await _host.SignInOnboarded(first);

        var r = await (await _host.SignIn()).Onboard(first.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, r.Status);
        Assert.Equal("handle_taken", r.ErrorCode);
    }

    [Fact]
    public async Task Onboarding_twice_returns_already_onboarded()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());

        var r = await u.Onboard(ProfileTestHost.UniqueHandle());

        Assert.Equal(HttpStatusCode.Conflict, r.Status);
        Assert.Equal("already_onboarded", r.ErrorCode);
    }

    [Fact]
    public async Task Concurrent_onboarding_with_the_same_handle_yields_exactly_one_winner()
    {
        var handle = ProfileTestHost.UniqueHandle();
        var users = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _host.SignIn()));

        var results = await Task.WhenAll(users.Select(u => u.Onboard(handle)));

        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.Equal("handle_taken", r.ErrorCode));
    }

    [Fact]
    public async Task Onboarding_follows_chosen_artists()
    {
        var a = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var b = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var u = await _host.SignIn();

        var r = await u.Onboard(ProfileTestHost.UniqueHandle(), follow: [a.Id, b.Id]);

        Assert.Equal(HttpStatusCode.OK, r.Status);
        await using var db = _host.Db();
        var followed = await db.Follows.Where(f => f.FollowerId == u.Id).Select(f => f.FolloweeId).ToListAsync();
        Assert.Equal(new[] { a.Id, b.Id }.Order(), followed.Order());
        Assert.Equal(2, r.Data.GetProperty("profile").GetProperty("followingCount").GetInt32());
    }

    [Fact]
    public async Task Onboarding_ignores_duplicate_self_unknown_and_not_onboarded_follow_ids()
    {
        var a = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var notOnboarded = await _host.SignIn();
        var u = await _host.SignIn();

        var r = await u.Onboard(ProfileTestHost.UniqueHandle(), follow: [a.Id, a.Id, u.Id, Guid.NewGuid(), notOnboarded.Id]);

        Assert.Equal(HttpStatusCode.OK, r.Status);
        await using var db = _host.Db();
        Assert.Equal([a.Id], await db.Follows.Where(f => f.FollowerId == u.Id).Select(f => f.FolloweeId).ToListAsync());
    }

    [Fact]
    public async Task Onboarding_with_more_than_50_follow_ids_is_refused()
    {
        var u = await _host.SignIn();
        var r = await u.Onboard(ProfileTestHost.UniqueHandle(), follow: Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()));
        Assert.Equal("validation_failed", r.ErrorCode);
    }

    [Fact]
    public async Task Failed_onboarding_leaves_no_follows_behind()
    {
        var a = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var taken = ProfileTestHost.UniqueHandle();
        await _host.SignInOnboarded(taken);
        var u = await _host.SignIn();

        var r = await u.Onboard(taken, follow: [a.Id]);

        Assert.Equal("handle_taken", r.ErrorCode);
        await using var db = _host.Db();
        Assert.False(await db.Follows.AnyAsync(f => f.FollowerId == u.Id));
        Assert.Null((await db.Users.SingleAsync(x => x.Id == u.Id)).TermsAcceptedAt);
    }

    [Fact]
    public async Task Used_works_counts_draft_pending_and_published_only()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        await using (var db = _host.Db())
        {
            foreach (var s in new[] { WorkStatus.Draft, WorkStatus.Pending, WorkStatus.Published, WorkStatus.Blocked, WorkStatus.Removed })
                db.Works.Add(new Work { Id = Guid.NewGuid(), ArtistId = u.Id, Status = s, CreatedAt = ProfileTestHost.Now, Width = 1, Height = 1, ImageBlob = "b" });
            await db.SaveChangesAsync();
        }

        var me = await u.Get("/v1/me");

        Assert.Equal(3, me.Data.GetProperty("usedWorks").GetInt32());
    }
}
