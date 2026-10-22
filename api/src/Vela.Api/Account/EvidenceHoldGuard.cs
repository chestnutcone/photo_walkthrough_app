using Vela.Api.Data.Entities;

namespace Vela.Api.Account;

/// <summary>
/// Extension point for the illegal-content evidence hold (A14). Account deletion must not remove reports (and the content
/// they preserve) that are held as evidence. Today nothing is held, so every report is deletable; A14 narrows the query here.
/// </summary>
public static class EvidenceHoldGuard
{
    public static IQueryable<Report> Deletable(IQueryable<Report> reports) => reports;
}
