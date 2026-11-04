namespace Vela.Api.Data.Entities;

/// <summary>
/// Owner decision 2026-11-03: a sign-in identity whose account was deleted while its content was under review may not sign up
/// again until <see cref="ExpiresAt"/> (the 30-day deletion cap). Holds only HMAC-SHA256(Admin:BanPepper, provider + ":" + subject)
/// and dates, no user id or other personal data. Removed by the hard delete (review closed or cap reached) or by the hourly sweep.
/// </summary>
public class SignupBlock
{
    public string SubjectHash { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
