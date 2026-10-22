namespace Vela.Api.Auth.EmailCodes;

public sealed class InvalidEmailException() : Exception("The email address is invalid.");

/// <summary>Every verify failure maps to this one exception so callers cannot tell the reason.</summary>
public sealed class InvalidCodeException() : Exception("The code is invalid or has expired.");

public sealed record EmailSignInResult(TokenPair Pair, Guid UserId, bool IsNew);

public interface IEmailCodeService
{
    Task RequestAsync(string email, string ip);
    Task<EmailSignInResult> VerifyAsync(string email, string code, string ip);
}
