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
    Task<(Guid UserId, bool IsNew)> SignInAsync(VerifiedIdentity identity);
}
