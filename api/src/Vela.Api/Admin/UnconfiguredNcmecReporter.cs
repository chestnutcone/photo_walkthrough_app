using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

/// <summary>
/// Every environment except Development and Test. The real CyberTipline integration needs the operator's registered ESP
/// account and is a separate piece of work; until then the admin page tells the admin to contact the legal advisor.
/// </summary>
public sealed class UnconfiguredNcmecReporter : INcmecReporter
{
    public Task<string> SubmitAsync(EvidenceHold hold, CancellationToken ct) => throw new NcmecNotConfiguredException();
}
