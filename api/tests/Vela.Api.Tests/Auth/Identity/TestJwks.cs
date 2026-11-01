using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Auth.Identity;

namespace Vela.Api.Tests.Auth.Identity;

/// <summary>Local stand-in for a provider's JWKS: a test RSA key, a fake IJwksProvider, and a token mint.</summary>
public sealed class TestJwks : IJwksProvider
{
    public const string Kid = "test-key-1";
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _other = RSA.Create(2048);
    public int Fetches { get; private set; }

    public SecurityKey PublicKey => new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = Kid };

    public Task<SecurityKey?> GetKeyAsync(string jwksUri, string kid, CancellationToken ct = default)
    {
        Fetches++;
        return Task.FromResult<SecurityKey?>(kid == Kid ? PublicKey : null);
    }

    public string Mint(string issuer, string audience, DateTimeOffset now, Dictionary<string, object> claims,
        TimeSpan? lifetime = null, bool signWithOtherKey = false, string alg = SecurityAlgorithms.RsaSha256, string kid = Kid)
    {
        var key = new RsaSecurityKey(signWithOtherKey ? _other : _rsa) { KeyId = kid };
        var d = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.UtcDateTime.Add(lifetime ?? TimeSpan.FromHours(1)),
            Claims = claims,
            SigningCredentials = new SigningCredentials(key, alg),
        };
        return new JsonWebTokenHandler().CreateToken(d);
    }

    public static string Sha256Hex(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
