namespace Vela.Api.Auth;

public record RefreshRequest(string? RefreshToken);
public record LogoutRequest(string? RefreshToken);
public record TokenPairResponse(string AccessToken, string RefreshToken, int ExpiresIn, bool IsNewUser);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Suspension: access tokens stay valid for up to 15 minutes (stateless JWTs), but refresh
        // is refused immediately and sessions are revoked via RevokeAllAsync at suspension time.
        app.MapPost("/v1/auth/refresh", async (RefreshRequest? req, ITokenService tokens, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(req?.RefreshToken))
                return Results.Fail(StatusCodes.Status400BadRequest, "validation_failed", "refreshToken is required.");
            try
            {
                var pair = await tokens.RefreshAsync(req.RefreshToken);
                var expiresIn = (int)Math.Round((pair.AccessExpiresAt - clock.GetUtcNow()).TotalSeconds);
                return Results.Ok(new TokenPairResponse(pair.AccessToken, pair.RefreshToken, expiresIn, false));
            }
            catch (InvalidRefreshTokenException)
            {
                return Results.Fail(StatusCodes.Status401Unauthorized, "invalid_refresh_token", "The refresh token is invalid.");
            }
        }).WithName("refresh");

        app.MapPost("/v1/auth/logout", async (LogoutRequest? req, ITokenService tokens) =>
        {
            if (string.IsNullOrWhiteSpace(req?.RefreshToken))
                return Results.Fail(StatusCodes.Status400BadRequest, "validation_failed", "refreshToken is required.");
            await tokens.RevokeAsync(req.RefreshToken);
            return Results.Ok<object?>(null);
        }).WithName("logout");

        return app;
    }
}
