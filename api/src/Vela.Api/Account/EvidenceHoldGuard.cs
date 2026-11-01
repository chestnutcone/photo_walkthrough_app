using Vela.Api.Data.Entities;

namespace Vela.Api.Account;

/// <summary>
/// Account deletion and the evidence hold (A14). Held evidence lives in its own table (<c>evidence_holds</c>, no foreign keys) and its
/// private blob container, both of which deletion never touches; the snapshot already carries the report ids and reasons, so the
/// report rows themselves remain deletable.
/// </summary>
public static class EvidenceHoldGuard
{
    public static IQueryable<Report> Deletable(IQueryable<Report> reports) => reports;
}
