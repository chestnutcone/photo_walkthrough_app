namespace Vela.Api.Auth;

public record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);

/// <summary>Thrown for every refresh failure; callers must not reveal which reason applied.</summary>
public sealed class InvalidRefreshTokenException() : Exception("The refresh token is invalid.");

public interface ITokenService
{
    Task<TokenPair> IssueAsync(Guid userId);
    Task<TokenPair> RefreshAsync(string refreshToken);
    /// <summary>Revokes the whole session (family) of the presented token. Unknown tokens are ignored.</summary>
    Task RevokeAsync(string refreshToken);
    Task RevokeAllAsync(Guid userId);
}

public interface ICurrentUser
{
    Guid? UserId { get; }
}
