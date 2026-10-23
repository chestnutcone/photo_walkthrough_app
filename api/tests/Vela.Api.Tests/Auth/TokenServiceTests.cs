using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Auth;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Auth;

[Collection(PostgresCollection.Name)]
public class TokenServiceTests(PostgresFixture pg)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private const string Key = "unit-test-signing-key-0123456789-abcdef";

    private static JwtOptions Options() => new() { SigningKey = Key, Issuer = "vela-test", Audience = "vela-test-app" };

    private (TokenService Svc, MutableTimeProvider Clock, VelaDbContext Db) Create()
    {
        var clock = new MutableTimeProvider(Start);
        var db = pg.NewContext();
        return (new TokenService(db, Microsoft.Extensions.Options.Options.Create(Options()), clock), clock, db);
    }

    private async Task<Guid> NewUserAsync(Action<User>? tweak = null)
    {
        var user = new User { Id = Guid.NewGuid(), TierId = Tier.FreemiumId, CreatedAt = Start };
        tweak?.Invoke(user);
        await using var db = pg.NewContext();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Issued_access_token_validates_and_carries_user_id()
    {
        var (svc, _, _) = Create();
        var userId = await NewUserAsync();

        var pair = await svc.IssueAsync(userId);

        var o = Options();
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(pair.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = o.Issuer, ValidAudience = o.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(Key)),
            ClockSkew = TimeSpan.Zero, ValidateLifetime = false,
        });
        Assert.True(result.IsValid);
        Assert.Equal(userId.ToString(), result.Claims["sub"]);
        Assert.Equal(Start.AddMinutes(15), pair.AccessExpiresAt);

        Assert.Equal(Start.AddMinutes(15).ToUnixTimeSeconds(), Convert.ToInt64(result.Claims["exp"]));
    }

    [Fact]
    public async Task Issue_stores_only_a_hash_of_the_refresh_token()
    {
        var (svc, _, db) = Create();
        var userId = await NewUserAsync();

        var pair = await svc.IssueAsync(userId);

        var row = await db.RefreshTokens.SingleAsync(t => t.UserId == userId);
        Assert.NotEqual(pair.RefreshToken, row.TokenHash);
        Assert.Equal(64, row.TokenHash.Length);
        Assert.Equal(Start.AddDays(30), row.ExpiresAt);
        Assert.Equal(32, Base64Url(pair.RefreshToken).Length);
    }

    private static byte[] Base64Url(string s) => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(s);

    [Fact]
    public async Task Refresh_returns_new_pair_and_invalidates_old_refresh_token()
    {
        var (svc, _, db) = Create();
        var userId = await NewUserAsync();
        var first = await svc.IssueAsync(userId);

        var second = await svc.RefreshAsync(first.RefreshToken);

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(first.RefreshToken));
        var families = await db.RefreshTokens.Where(t => t.UserId == userId).Select(t => t.FamilyId).Distinct().CountAsync();
        Assert.Equal(1, families);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_fails_and_revokes_the_whole_family()
    {
        var (svc, _, _) = Create();
        var userId = await NewUserAsync();
        var first = await svc.IssueAsync(userId);
        var second = await svc.RefreshAsync(first.RefreshToken);
        var third = await svc.RefreshAsync(second.RefreshToken);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(first.RefreshToken));

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(third.RefreshToken));
    }

    [Fact]
    public async Task Reuse_does_not_revoke_other_families_of_the_same_user()
    {
        var (svc, _, _) = Create();
        var userId = await NewUserAsync();
        var deviceA = await svc.IssueAsync(userId);
        var deviceB = await svc.IssueAsync(userId);
        await svc.RefreshAsync(deviceA.RefreshToken);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(deviceA.RefreshToken));

        await svc.RefreshAsync(deviceB.RefreshToken);
    }

    [Fact]
    public async Task Expired_refresh_token_fails()
    {
        var (svc, clock, _) = Create();
        var pair = await svc.IssueAsync(await NewUserAsync());

        clock.Advance(TimeSpan.FromDays(30).Add(TimeSpan.FromSeconds(1)));

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(pair.RefreshToken));
    }

    [Fact]
    public async Task Unknown_or_garbage_refresh_token_fails()
    {
        var (svc, _, _) = Create();
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync("not-a-token"));
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(""));
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var (svc, _, _) = Create();
        var first = await svc.IssueAsync(await NewUserAsync());
        var second = await svc.RefreshAsync(first.RefreshToken);

        await svc.RevokeAsync(second.RefreshToken);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(second.RefreshToken));
        await svc.RevokeAsync("unknown"); // idempotent, no throw
    }

    [Fact]
    public async Task RevokeAll_revokes_every_session_of_the_user()
    {
        var (svc, _, _) = Create();
        var userId = await NewUserAsync();
        var a = await svc.IssueAsync(userId);
        var b = await svc.IssueAsync(userId);

        await svc.RevokeAllAsync(userId);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(a.RefreshToken));
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(b.RefreshToken));
    }

    [Fact]
    public async Task Deleted_user_cannot_refresh()
    {
        var (svc, clock, _) = Create();
        var userId = await NewUserAsync();
        var pair = await svc.IssueAsync(userId);
        await using (var db = pg.NewContext())
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, clock.GetUtcNow()));

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(pair.RefreshToken));
    }

    [Fact]
    public async Task Suspended_user_cannot_refresh()
    {
        var (svc, clock, _) = Create();
        var userId = await NewUserAsync();
        var pair = await svc.IssueAsync(userId);
        await using (var db = pg.NewContext())
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, clock.GetUtcNow()));

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync(pair.RefreshToken));
    }

    [Fact]
    public async Task Concurrent_refreshes_of_one_token_yield_exactly_one_success()
    {
        var clock = new MutableTimeProvider(Start);
        var userId = await NewUserAsync();
        var pair = await new TokenService(pg.NewContext(), Microsoft.Extensions.Options.Options.Create(Options()), clock).IssueAsync(userId);

        var attempts = Enumerable.Range(0, 4).Select(async _ =>
        {
            try
            {
                await new TokenService(pg.NewContext(), Microsoft.Extensions.Options.Options.Create(Options()), clock).RefreshAsync(pair.RefreshToken);
                return true;
            }
            catch (InvalidRefreshTokenException) { return false; }
        });

        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(r => r));
    }

    [Fact]
    public void CurrentUser_reads_sub_claim_and_is_null_when_anonymous()
    {
        var id = Guid.NewGuid();
        var ctx = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        Assert.Null(new CurrentUser(accessor).UserId);

        ctx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", id.ToString())], "test"));
        Assert.Equal(id, new CurrentUser(accessor).UserId);
    }

    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Development");
        b.UseSetting("Abuse:RequireAttestation", "false"); // attestation start-up rules are covered in AttestationEndpointTests
        b.UseSetting("ConnectionStrings:Default", pg.ConnectionString);
    });

    [Fact]
    public async Task Refresh_endpoint_rotates_and_returns_envelope_then_rejects_reuse_with_401()
    {
        using var factory = Factory();
        Guid userId = await NewUserAsync();
        string refresh;
        using (var scope = factory.Services.CreateScope())
            refresh = (await scope.ServiceProvider.GetRequiredService<ITokenService>().IssueAsync(userId)).RefreshToken;
        var client = factory.CreateClient();

        var ok = await client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var doc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.NotEqual(refresh, data.GetProperty("refreshToken").GetString());
        Assert.Equal(900, data.GetProperty("expiresIn").GetInt32());
        Assert.False(data.GetProperty("isNewUser").GetBoolean());

        var reuse = await client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        using var err = JsonDocument.Parse(await reuse.Content.ReadAsStringAsync());
        Assert.Equal("invalid_refresh_token", err.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_endpoint_with_missing_token_returns_400_and_logout_revokes()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var bad = await client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = "" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var userId = await NewUserAsync();
        string refresh;
        using (var scope = factory.Services.CreateScope())
            refresh = (await scope.ServiceProvider.GetRequiredService<ITokenService>().IssueAsync(userId)).RefreshToken;

        var logout = await client.PostAsJsonAsync("/v1/auth/logout", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        var after = await client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too-short-key")]
    public void Startup_outside_development_without_valid_signing_key_fails(string? key)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Abuse:RequireAttestation", "false"); // attestation start-up rules are covered in AttestationEndpointTests
            b.UseSetting("ConnectionStrings:Default", pg.ConnectionString);
            b.UseSetting("Auth:Jwt:SigningKey", key);
        });
        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Auth:Jwt:SigningKey", ex.Message);
    }
}
