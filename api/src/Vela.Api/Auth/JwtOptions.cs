using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Vela.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Auth:Jwt";
    public const string SigningKeySetting = "Auth:Jwt:SigningKey";
    public const int MinKeyBytes = 32;

    public string SigningKey { get; set; } = "";
    public string Issuer { get; set; } = "vela-api";
    public string Audience { get; set; } = "vela-app";

    public SymmetricSecurityKey SecurityKey => new(Encoding.UTF8.GetBytes(SigningKey));

    /// <summary>Fails fast at startup when the signing key is missing or too short.</summary>
    public static JwtOptions Load(IConfiguration config)
    {
        var options = config.GetSection(Section).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(options.SigningKey))
            throw new InvalidOperationException(
                $"{SigningKeySetting} is not configured. Set it via environment (Auth__Jwt__SigningKey) or a secret store.");
        if (Encoding.UTF8.GetByteCount(options.SigningKey) < MinKeyBytes)
            throw new InvalidOperationException($"{SigningKeySetting} must be at least {MinKeyBytes} bytes.");
        return options;
    }
}
