using System.Security.Cryptography;
using System.Text;
using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

/// <summary>Development and Test only: returns a deterministic fake id and contacts nobody.</summary>
public sealed class FakeNcmecReporter : INcmecReporter
{
    public Task<string> SubmitAsync(EvidenceHold hold, CancellationToken ct) =>
        Task.FromResult("FAKE-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hold.Id.ToString("N"))))[..12]);
}
