using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Tests.Profiles;

namespace Vela.Api.Tests.Security;

/// <summary>Bearer tokens an attacker could present: none of them may authenticate.</summary>
public static class ForgedTokens
{
    private const string RealKey = "integration-test-signing-key-0123456789-abc"; // the Test host's key (see ProfileTestHost)

    public static IEnumerable<(string Name, string Token)> All(Guid userId)
    {
        var now = ProfileTestHost.Now.UtcDateTime;
        yield return ("garbage", "not-a-token");
        yield return ("empty-segments", "..");
        yield return ("alg-none", AlgNone(userId, now));
        yield return ("wrong-key", Signed(userId, "some-other-signing-key-0123456789-abcdef", "vela-api", "vela-app", now, now.AddMinutes(15)));
        yield return ("wrong-audience", Signed(userId, RealKey, "vela-api", "someone-else", now, now.AddMinutes(15)));
        yield return ("wrong-issuer", Signed(userId, RealKey, "someone-else", "vela-app", now, now.AddMinutes(15)));
        yield return ("expired", Signed(userId, RealKey, "vela-api", "vela-app", now.AddHours(-2), now.AddMinutes(-1)));
        yield return ("not-yet-valid", Signed(userId, RealKey, "vela-api", "vela-app", now.AddHours(1), now.AddHours(2)));
        yield return ("hs512", Signed(userId, RealKey, "vela-api", "vela-app", now, now.AddMinutes(15), SecurityAlgorithms.HmacSha512));
        yield return ("no-expiry", Signed(userId, RealKey, "vela-api", "vela-app", now, null));
    }

    private static string Signed(Guid userId, string key, string issuer, string audience, DateTime notBefore, DateTime? expires,
        string algorithm = SecurityAlgorithms.HmacSha256) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString())]),
            Issuer = issuer, Audience = audience, NotBefore = notBefore, Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key.PadRight(64, 'x'))), algorithm),
        });

    private static string AlgNone(Guid userId, DateTime now)
    {
        static string B64(string s) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(s));
        var exp = new DateTimeOffset(now.AddMinutes(15)).ToUnixTimeSeconds();
        return $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64($"{{\"sub\":\"{userId}\",\"iss\":\"vela-api\",\"aud\":\"vela-app\",\"exp\":{exp}}}")}.";
    }
}
