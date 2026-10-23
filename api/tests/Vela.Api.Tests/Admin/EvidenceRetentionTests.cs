using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vela.Api.Admin;
using Vela.Api.Tests.Account;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class EvidenceRetentionTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    private readonly CapturingLoggerProvider _logs = new();
    public async Task InitializeAsync()
    {
        _s = await AdminSite.Create(pg, isolated: true, extra: s => s.AddLogging(l => l.AddProvider(_logs)));
        _admin = await _s.NewAdminUser();
    }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private async Task<Guid> Hold()
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        return (await _s.MarkIllegal(_admin, report)).HoldId!.Value;
    }

    private Task<int> Run(DateTimeOffset now) => _s.Service<EvidenceRetentionJob>().RunOnceAsync(now, CancellationToken.None);

    [Fact]
    public async Task A_hold_is_kept_until_its_date_and_then_deleted_with_its_blobs_and_an_audit_row()
    {
        var id = await Hold();
        var until = (await _s.Hold(id)).HoldUntil;

        Assert.Equal(0, await Run(until.AddSeconds(-1)));
        Assert.Single(await _s.Holds());
        Assert.NotEqual("", _s.EvidenceBlobs(id));

        Assert.Equal(1, await Run(until));

        Assert.Empty(await _s.Holds());
        Assert.Equal("", _s.EvidenceBlobs(id));
        var row = Assert.Single(await _s.Audit(), a => a.Action == "evidence_expired");
        Assert.Equal(("evidence", id), (row.TargetType, row.TargetId));
    }

    [Fact]
    public async Task Only_expired_holds_are_deleted()
    {
        var old = await Hold();
        _s.Host.Clock.Advance(TimeSpan.FromDays(10));
        var fresh = await Hold();

        await Run((await _s.Hold(old)).HoldUntil);

        var left = Assert.Single(await _s.Holds());
        Assert.Equal(fresh, left.Id);
        Assert.NotEqual("", _s.EvidenceBlobs(fresh));
    }

    [Fact]
    public async Task A_failed_blob_delete_keeps_the_row_and_the_next_run_retries_with_type_names_only_in_the_log()
    {
        var id = await Hold();
        var until = (await _s.Hold(id)).HoldUntil;
        _s.Host.Blobs.FailDeletes = p => p.StartsWith("evidence/", StringComparison.Ordinal);

        Assert.Equal(0, await Run(until));

        Assert.Single(await _s.Holds());
        Assert.NotEqual("", _s.EvidenceBlobs(id));
        Assert.DoesNotContain(await _s.Audit(), a => a.Action == "evidence_expired");
        Assert.Contains(_logs.Lines, l => l.Contains("IOException"));
        Assert.DoesNotContain(_logs.Lines, l => l.Contains("delete failed") || l.Contains(id.ToString()));

        _s.Host.Blobs.FailDeletes = null;
        Assert.Equal(1, await Run(until));
        Assert.Empty(await _s.Holds());
        Assert.Equal("", _s.EvidenceBlobs(id));
    }

    [Fact]
    public async Task The_job_is_registered_as_a_singleton_for_the_daily_worker()
    {
        Assert.Same(_s.Service<EvidenceRetentionJob>(), _s.Service<EvidenceRetentionJob>());
        Assert.Equal(TimeSpan.FromDays(1), EvidenceRetentionJob.Interval);
        await Task.CompletedTask;
    }
}
