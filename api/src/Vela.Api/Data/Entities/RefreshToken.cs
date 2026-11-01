namespace Vela.Api.Data.Entities;

public class RefreshToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    /// <summary>All tokens descended from one sign-in; reuse of a rotated token revokes the family.</summary>
    public Guid FamilyId { get; init; }
    public string TokenHash { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RotatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
