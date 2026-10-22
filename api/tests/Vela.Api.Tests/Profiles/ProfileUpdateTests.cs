using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Profiles;

[Collection(PostgresCollection.Name)]
public class ProfileUpdateTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg);
    public void Dispose() => _host.Dispose();

    private async Task<(TestUser User, string Handle)> Onboarded()
    {
        var h = ProfileTestHost.UniqueHandle();
        return (await _host.SignInOnboarded(h), h);
    }

    [Fact]
    public async Task Update_requires_authentication()
    {
        var c = _host.Anonymous();
        var r = await c.PatchAsync("/v1/me/profile", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Update_before_onboarding_returns_onboarding_required()
    {
        var u = await _host.SignIn();
        var r = await u.Patch("/v1/me/profile", new { bio = "hi" });
        Assert.Equal(HttpStatusCode.Conflict, r.Status);
        Assert.Equal("onboarding_required", r.ErrorCode);
    }

    [Fact]
    public async Task Partial_update_changes_only_provided_fields_and_ignores_unknown_fields()
    {
        var (u, _) = await Onboarded();
        await u.Patch("/v1/me/profile", new { bio = "First bio", city = "Lisbon" });

        var r = await u.Patch("/v1/me/profile", new { bio = "<b>Second</b> 🎨 مرحبا", isAdmin = true, nonsense = 1 });

        Assert.Equal(HttpStatusCode.OK, r.Status);
        var p = r.Data.GetProperty("profile");
        Assert.Equal("<b>Second</b> 🎨 مرحبا", p.GetProperty("bio").GetString()); // stored verbatim
        Assert.Equal("Lisbon", p.GetProperty("city").GetString());
        await using var db = _host.Db();
        Assert.False((await db.Users.SingleAsync(x => x.Id == u.Id)).IsAdmin);
    }

    [Fact]
    public async Task Update_profile_only_changes_own_profile()
    {
        var (a, _) = await Onboarded();
        var (b, _) = await Onboarded();

        var r = await a.Patch("/v1/me/profile", new { bio = "mine", userId = b.Id, id = b.Id });

        Assert.Equal(HttpStatusCode.OK, r.Status);
        await using var db = _host.Db();
        Assert.Equal("mine", (await db.Profiles.SingleAsync(x => x.UserId == a.Id)).Bio);
        Assert.Equal("", (await db.Profiles.SingleAsync(x => x.UserId == b.Id)).Bio);
    }

    [Fact]
    public async Task Bio_over_280_chars_is_refused_and_exactly_280_is_accepted()
    {
        var (u, _) = await Onboarded();

        var tooLong = await u.Patch("/v1/me/profile", new { bio = new string('a', 281) });
        var ok = await u.Patch("/v1/me/profile", new { bio = new string('a', 280) });

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.Status);
        Assert.Equal("validation_failed", tooLong.ErrorCode);
        Assert.Contains("bio", tooLong.Body.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("aaaa", tooLong.Body.GetRawText());
        Assert.Equal(HttpStatusCode.OK, ok.Status);
    }

    [Theory]
    [InlineData("displayName", "")]
    [InlineData("city", 81)]
    [InlineData("discipline", 61)]
    [InlineData("website", "ftp://example.test")]
    [InlineData("website", "not a url")]
    [InlineData("website", "/relative")]
    [InlineData("social", 61)]
    [InlineData("studio", 121)]
    [InlineData("replyTime", 121)]
    [InlineData("publicEmail", "nope")]
    [InlineData("publicEmail", "a b@c.test")]
    [InlineData("availability", 61)]
    [InlineData("bio", "bad\u0007control")]
    public async Task Invalid_field_is_refused_naming_the_field(string field, object value)
    {
        var (u, _) = await Onboarded();
        object v = value is int n ? new string('x', n) : value;

        var r = await u.Patch("/v1/me/profile", new Dictionary<string, object> { [field] = v });

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("validation_failed", r.ErrorCode);
        Assert.Contains(field, r.Body.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Website_over_200_chars_is_refused_and_https_is_accepted()
    {
        var (u, _) = await Onboarded();
        var tooLong = await u.Patch("/v1/me/profile", new { website = "https://example.test/" + new string('a', 190) });
        var ok = await u.Patch("/v1/me/profile", new { website = "https://example.test/me" });
        Assert.Equal("validation_failed", tooLong.ErrorCode);
        Assert.Equal(HttpStatusCode.OK, ok.Status);
    }

    [Fact]
    public async Task Public_email_can_be_set_and_cleared_with_null()
    {
        var (u, _) = await Onboarded();

        var set = await u.Patch("/v1/me/profile", new { publicEmail = "hello@studio.test" });
        Assert.Equal("hello@studio.test", set.Data.GetProperty("profile").GetProperty("publicEmail").GetString());

        var cleared = await u.Patch("/v1/me/profile", """{"publicEmail":null}""");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cleared.Data.GetProperty("profile").GetProperty("publicEmail").ValueKind);
    }

    [Fact]
    public async Task Services_and_shows_are_stored_and_limits_enforced()
    {
        var (u, _) = await Onboarded();
        var services = Enumerable.Range(0, 10).Select(i => new { title = $"S{i}", description = "line1\nline2", price = "$50" }).ToArray();
        var shows = new[] { new { year = "2024", text = "Solo show" } };

        var ok = await u.Patch("/v1/me/profile", new { services, shows });

        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.Equal(10, ok.Data.GetProperty("profile").GetProperty("services").GetArrayLength());
        Assert.Equal("Solo show", ok.Data.GetProperty("profile").GetProperty("shows")[0].GetProperty("text").GetString());

        var tooMany = await u.Patch("/v1/me/profile", new { services = services.Append(services[0]).ToArray() });
        Assert.Equal("validation_failed", tooMany.ErrorCode);
        var badYear = await u.Patch("/v1/me/profile", new { shows = new[] { new { year = "24", text = "x" } } });
        Assert.Equal("validation_failed", badYear.ErrorCode);
        var tooManyShows = await u.Patch("/v1/me/profile", new { shows = Enumerable.Range(0, 21).Select(_ => shows[0]).ToArray() });
        Assert.Equal("validation_failed", tooManyShows.ErrorCode);
        var longDesc = await u.Patch("/v1/me/profile", new { services = new[] { new { title = "t", description = new string('x', 301), price = "1" } } });
        Assert.Equal("validation_failed", longDesc.ErrorCode);
        var ctrl = await u.Patch("/v1/me/profile", new { services = new[] { new { title = "t", description = "a\u0000b", price = "1" } } });
        Assert.Equal("validation_failed", ctrl.ErrorCode);
    }

    [Fact]
    public async Task Handle_change_is_allowed_once_then_refused_within_30_days()
    {
        var (u, _) = await Onboarded();
        var next = ProfileTestHost.UniqueHandle("n");

        var first = await u.Patch("/v1/me/profile", new { handle = next });
        var second = await u.Patch("/v1/me/profile", new { handle = ProfileTestHost.UniqueHandle("m") });

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(next, first.Data.GetProperty("profile").GetProperty("handle").GetString());
        Assert.Equal(HttpStatusCode.Conflict, second.Status);
        Assert.Equal("handle_change_too_soon", second.ErrorCode);
    }

    [Fact]
    public async Task Handle_change_is_refused_within_30_days()
    {
        var (u, _) = await Onboarded();
        await SetChangedAt(u.Id, ProfileTestHost.Now.AddDays(-29));

        var r = await u.Patch("/v1/me/profile", new { handle = ProfileTestHost.UniqueHandle("n") });

        Assert.Equal("handle_change_too_soon", r.ErrorCode);
    }

    [Fact]
    public async Task Handle_change_is_allowed_after_30_days_and_resets_the_timer()
    {
        var (u, _) = await Onboarded();
        await SetChangedAt(u.Id, ProfileTestHost.Now.AddDays(-30));

        var r = await u.Patch("/v1/me/profile", new { handle = ProfileTestHost.UniqueHandle("n") });

        Assert.Equal(HttpStatusCode.OK, r.Status);
        await using var db = _host.Db();
        Assert.Equal(ProfileTestHost.Now, (await db.Profiles.SingleAsync(x => x.UserId == u.Id)).HandleChangedAt);
    }

    [Fact]
    public async Task Changing_to_the_same_handle_is_a_no_op_even_inside_the_window()
    {
        var (u, handle) = await Onboarded();
        await SetChangedAt(u.Id, ProfileTestHost.Now.AddDays(-1));

        var r = await u.Patch("/v1/me/profile", new { handle = handle.ToUpperInvariant(), bio = "x" });

        Assert.Equal(HttpStatusCode.OK, r.Status);
        await using var db = _host.Db();
        Assert.Equal(ProfileTestHost.Now.AddDays(-1), (await db.Profiles.SingleAsync(x => x.UserId == u.Id)).HandleChangedAt);
    }

    [Fact]
    public async Task Changing_handle_to_one_taken_or_invalid_is_refused()
    {
        var (u, _) = await Onboarded();
        var (_, other) = await Onboarded();

        var taken = await u.Patch("/v1/me/profile", new { handle = other });
        var invalid = await u.Patch("/v1/me/profile", new { handle = "no spaces!" });
        var reserved = await u.Patch("/v1/me/profile", new { handle = "support" });

        Assert.Equal("handle_taken", taken.ErrorCode);
        Assert.Equal("invalid_handle", invalid.ErrorCode);
        Assert.Equal("invalid_handle", reserved.ErrorCode);
    }

    [Fact]
    public async Task A_failed_update_applies_nothing()
    {
        var (u, _) = await Onboarded();

        var r = await u.Patch("/v1/me/profile", new { bio = "should not stick", city = new string('x', 81) });

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        await using var db = _host.Db();
        Assert.Equal("", (await db.Profiles.SingleAsync(x => x.UserId == u.Id)).Bio);
    }

    [Fact]
    public async Task Malformed_json_body_returns_validation_failed()
    {
        var (u, _) = await Onboarded();
        var r = await u.Patch("/v1/me/profile", "{not json");
        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("validation_failed", r.ErrorCode);
    }

    [Fact]
    public async Task Wrongly_typed_field_returns_validation_failed()
    {
        var (u, _) = await Onboarded();
        var r = await u.Patch("/v1/me/profile", """{"bio":12}""");
        Assert.Equal("validation_failed", r.ErrorCode);
    }

    private async Task SetChangedAt(Guid userId, DateTimeOffset at)
    {
        await using var db = _host.Db();
        await db.Profiles.Where(p => p.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(p => p.HandleChangedAt, at));
    }
}
