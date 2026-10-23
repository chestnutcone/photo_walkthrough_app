namespace Vela.Api.Auth.Identity;

public record VerifiedIdentity(string Provider, string Subject, string? Email, bool EmailVerified);

/// <summary>Thrown for every verification failure; callers must not reveal which reason applied.</summary>
public sealed class IdentityVerificationException(string? detail = null, Exception? inner = null)
    : Exception(detail ?? "The identity token is invalid.", inner);

public interface IIdentityVerifier
{
    string Provider { get; }
    Task<VerifiedIdentity> VerifyAsync(string idToken, string? nonce);
}

public sealed class AccountSuspendedException() : Exception("The account is suspended.");
public sealed class AccountDeletedException() : Exception("The account is deleted.");

public interface IAccountResolver
{
    /// <summary>Finds or creates the user. A new account from <paramref name="clientIp"/> counts against the per-address daily signup limit (RateLimitedException when over).</summary>
    Task<(Guid UserId, bool IsNew)> SignInAsync(VerifiedIdentity identity, System.Net.IPAddress? clientIp = null);
}
