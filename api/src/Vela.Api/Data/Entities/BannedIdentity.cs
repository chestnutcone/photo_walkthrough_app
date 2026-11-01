namespace Vela.Api.Data.Entities;

/// <summary>
/// A suspended person's identity, stored only as HMAC-SHA256(Admin:BanPepper, provider + ":" + subject) in hex, so a banned
/// identity cannot sign up again. No foreign key to users: deleting the account must not lift the ban.
/// </summary>
public class BannedIdentity
{
    public string Provider { get; init; } = "";
    public string SubjectHash { get; init; } = "";
    /// <summary>The user the identity belonged to; lets reinstatement remove exactly that user's rows.</summary>
    public Guid? UserRef { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
