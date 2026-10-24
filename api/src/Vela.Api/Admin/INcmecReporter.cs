using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

/// <summary>
/// The seam to the NCMEC CyberTipline. It is called from exactly one place: the explicit admin action on the evidence hold
/// page. Nothing automatic ever submits a report to an authority.
/// </summary>
public interface INcmecReporter
{
    /// <summary>
    /// Submits the report for a hold and returns NCMEC's report id. <c>hold.Id</c> is the idempotency key: a real implementation must
    /// send it (or a value derived from it) with the submission, because a call can succeed at NCMEC and still fail to commit here, and
    /// the admin then retries. The fake derives its id from the same key, so the same hold always yields the same report id.
    /// </summary>
    Task<string> SubmitAsync(EvidenceHold hold, CancellationToken ct);
}

/// <summary>Reporting is not set up in this environment.</summary>
public sealed class NcmecNotConfiguredException() : Exception("NCMEC reporting is not configured.");
