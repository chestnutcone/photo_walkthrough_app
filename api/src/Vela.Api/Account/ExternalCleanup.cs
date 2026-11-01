using Vela.Api.Data.Entities;

namespace Vela.Api.Account;

/// <summary>Purges edge-cached copies of deleted public images. The real Cloudflare purger is infrastructure work.</summary>
public interface ICdnPurger
{
    Task PurgeAsync(IReadOnlyList<string> publicUrlsOrPaths);
}

/// <summary>Placeholder until the CDN purge integration exists. Logs a count only, never the URLs.</summary>
public sealed class NoOpCdnPurger(ILogger<NoOpCdnPurger> log) : ICdnPurger
{
    public Task PurgeAsync(IReadOnlyList<string> publicUrlsOrPaths)
    {
        log.LogInformation("CDN purge skipped (no purger configured) for {Count} paths.", publicUrlsOrPaths.Count);
        return Task.CompletedTask;
    }
}

/// <summary>Revokes the user's Sign in with Apple grant when the account is deleted (App Store rule 5.1.1(v)).</summary>
public interface IAppleTokenRevoker
{
    Task RevokeAsync(Identity identity);
}

/// <summary>
/// Known gap: real revocation needs the Apple authorization code or refresh token, which is not stored at sign-in yet.
/// This records only a reason code.
/// </summary>
public sealed class LoggingAppleTokenRevoker(ILogger<LoggingAppleTokenRevoker> log) : IAppleTokenRevoker
{
    public Task RevokeAsync(Identity identity)
    {
        log.LogWarning("Apple token revocation skipped ({Reason}).", "no_stored_apple_token");
        return Task.CompletedTask;
    }
}
