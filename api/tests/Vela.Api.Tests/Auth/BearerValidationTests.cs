using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Auth;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Auth;

/// <summary>Validates tokens with the app's real JwtBearer options (issuer, audience, alg pin, lifetime).</summary>
[Collection(PostgresCollection.Name)]
public class BearerValidationTests(PostgresFixture pg) : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly MutableTimeProvider _clock = new(Start);
    private WebApplicationFactory<Program>? _factory;

    public void Dispose() => _factory?.Dispose();

    private (TokenValidationParameters Real, JwtOptions Jwt, IServiceProvider Services) Setup()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:Default", pg.ConnectionString);
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(_clock));
        });
        var sp = _factory.Services;
        var real = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        return (real, sp.GetRequiredService<IOptions<JwtOptions>>().Value, sp);
    }

    private static string Forge(JwtOptions jwt, DateTimeOffset now, string? key = null, string? alg = SecurityAlgorithms.HmacSha256,
        string? issuer = null, string? audience = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
            Issuer = issuer ?? jwt.Issuer,
            Audience = audience ?? jwt.Audience,
            NotBefore = now.UtcDateTime,
            Expires = now.UtcDateTime.AddMinutes(15),
        };
        if (alg is not null)
            descriptor.SigningCredentials = new SigningCredentials(
                key is null ? jwt.SecurityKey : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), alg);
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Hand-signs with the right key; the library refuses HS512 with a key under 64 bytes.</summary>
    private static string ForgeManually(JwtOptions jwt, string alg)
    {
        static string B64(byte[] b) => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(b);
        var header = B64(Encoding.UTF8.GetBytes($$"""{"alg":"{{alg}}","typ":"JWT"}"""));
        var exp = Start.AddMinutes(15).ToUnixTimeSeconds();
        var payload = B64(Encoding.UTF8.GetBytes(
            $$"""{"sub":"{{Guid.NewGuid()}}","iss":"{{jwt.Issuer}}","aud":"{{jwt.Audience}}","exp":{{exp}}}"""));
        var key = Encoding.UTF8.GetBytes(jwt.SigningKey);
        var data = Encoding.UTF8.GetBytes($"{header}.{payload}");
        byte[] sig = alg == SecurityAlgorithms.HmacSha384
            ? System.Security.Cryptography.HMACSHA384.HashData(key, data)
            : System.Security.Cryptography.HMACSHA512.HashData(key, data);
        return $"{header}.{payload}.{B64(sig)}";
    }

    private static Task<TokenValidationResult> Validate(TokenValidationParameters p, string token) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, p);

    [Fact]
    public async Task Real_options_accept_a_valid_token_from_TokenService_and_reject_it_after_15_minutes()
    {
        var (real, _, sp) = Setup();
        var userId = Guid.NewGuid();
        await using var db = pg.NewContext();
        db.Users.Add(new Vela.Api.Data.Entities.User { Id = userId, TierId = Vela.Api.Data.Entities.Tier.FreemiumId, CreatedAt = Start });
        await db.SaveChangesAsync();
        var token = (await new TokenService(db, sp.GetRequiredService<IOptions<JwtOptions>>(), _clock).IssueAsync(userId)).AccessToken;

        var ok = await Validate(real, token);
        Assert.True(ok.IsValid);
        Assert.Equal(userId.ToString(), ok.Claims["sub"]);

        _clock.Advance(TimeSpan.FromMinutes(14).Add(TimeSpan.FromSeconds(59)));
        Assert.True((await Validate(real, token)).IsValid);

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False((await Validate(real, token)).IsValid);
    }

    [Fact]
    public async Task Real_options_reject_a_token_signed_with_a_different_key()
    {
        var (real, jwt, _) = Setup();
        var token = Forge(jwt, Start, key: "a-completely-different-signing-key-0123456789");
        Assert.False((await Validate(real, token)).IsValid);
    }

    [Fact]
    public async Task Real_options_reject_alg_none()
    {
        var (real, jwt, _) = Setup();
        var token = Forge(jwt, Start, alg: null);
        Assert.False((await Validate(real, token)).IsValid);
    }

    [Fact]
    public async Task Real_options_reject_wrong_audience_and_wrong_issuer()
    {
        var (real, jwt, _) = Setup();
        Assert.False((await Validate(real, Forge(jwt, Start, audience: "someone-else"))).IsValid);
        Assert.False((await Validate(real, Forge(jwt, Start, issuer: "someone-else"))).IsValid);
        Assert.True((await Validate(real, Forge(jwt, Start))).IsValid);
    }

    [Theory]
    [InlineData(SecurityAlgorithms.HmacSha384)]
    [InlineData(SecurityAlgorithms.HmacSha512)]
    public async Task Real_options_reject_other_hmac_algorithms_with_the_right_key(string alg)
    {
        var (real, jwt, _) = Setup();
        var token = ForgeManually(jwt, alg);
        Assert.False((await Validate(real, token)).IsValid);
    }
}
