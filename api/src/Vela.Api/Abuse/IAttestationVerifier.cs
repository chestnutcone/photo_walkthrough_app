namespace Vela.Api.Abuse;

/// <summary>Proves a request comes from a genuine, unmodified copy of the app on a real device.</summary>
public interface IAttestationVerifier
{
    /// <param name="platform">"ios" or "android".</param>
    /// <param name="token">The X-Attestation header value.</param>
    /// <param name="action">The operation the token was minted for (e.g. "email_request"); tokens do not transfer between actions.</param>
    Task<bool> VerifyAsync(string platform, string token, string action);
}

/// <summary>Routes by platform. A platform with no verifier configured never verifies.</summary>
public sealed class PlatformAttestationVerifier(IAttestationVerifier? android, IAttestationVerifier? ios) : IAttestationVerifier
{
    public Task<bool> VerifyAsync(string platform, string token, string action) =>
        (platform.ToLowerInvariant() switch { "android" => android, "ios" => ios, _ => null }) is { } v
            ? v.VerifyAsync(platform, token, action)
            : Task.FromResult(false);
}
