using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

/// <summary>
/// The seam to the NCMEC CyberTipline. It is called from exactly one place: the explicit admin action on the evidence hold
/// page. Nothing automatic ever submits a report to an authority.
/// </summary>
public interface INcmecReporter
{
    /// <summary>Submits the report for a hold and returns NCMEC's report id.</summary>
    Task<string> SubmitAsync(EvidenceHold hold, CancellationToken ct);
}

/// <summary>Reporting is not set up in this environment.</summary>
public sealed class NcmecNotConfiguredException() : Exception("NCMEC reporting is not configured.");
