using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Data.Entities;
using Vela.Api.Legal;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Legal;

/// <summary>FINALAPI I5: closed DMCA notices (third-party personal data) are deleted after the retention period; open ones never.</summary>
[Collection(PostgresCollection.Name)]
public class DmcaRetentionTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    public async Task InitializeAsync() => _s = await AdminSite.Create(pg, isolated: true);
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private static readonly DateTimeOffset T0 = ProfileTestHost.Now;

    private async Task<Guid> Notice(string status, DateTimeOffset created, DateTimeOffset? forwarded = null)
    {
        var n = new DmcaNotice
        {
            Id = Guid.NewGuid(), WorkId = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), ClaimantName = "Claire Claimant",
            ClaimantEmail = "claire@example.test", ClaimantAddress = "1 Main St", WorkReference = "x", OriginalWorkDescription = "y",
            SignatureName = "Claire", GoodFaithStatement = true, PerjuryStatement = true, Status = status, CreatedAt = created,
            ForwardedAt = forwarded, ClientIp = "203.0.113.9",
        };
        await using var db = _s.Host.Db();
        db.DmcaNotices.Add(n);
        await db.SaveChangesAsync();
        return n.Id;
    }

    [Fact]
    public async Task Closed_notices_past_the_period_are_deleted_with_an_audit_row_and_everything_else_stays()
    {
        var job = _s.Host.Services.GetRequiredService<DmcaRetentionJob>();
        var old = T0 - job.Retention - TimeSpan.FromDays(1);
        var rejected = await Notice(DmcaStatus.Rejected, old);
        var takedown = await Notice(DmcaStatus.Takedown, old);
        var restoredRecently = await Notice(DmcaStatus.Restored, old, forwarded: T0 - TimeSpan.FromDays(30));
        var pending = await Notice(DmcaStatus.Pending, old);
        var forwarded = await Notice(DmcaStatus.Forwarded, old, forwarded: old);
        var young = await Notice(DmcaStatus.Rejected, T0 - TimeSpan.FromDays(10));

        Assert.Equal(2, await job.RunOnceAsync(T0, CancellationToken.None));

        await using var db = _s.Host.Db();
        var left = await db.DmcaNotices.Select(n => n.Id).ToListAsync();
        Assert.DoesNotContain(rejected, left);
        Assert.DoesNotContain(takedown, left);
        Assert.Contains(restoredRecently, left);
        Assert.Contains(pending, left);
        Assert.Contains(forwarded, left);
        Assert.Contains(young, left);
        Assert.Equal(2, await db.AdminActions.CountAsync(a => a.Action == "dmca_expired"));
        Assert.Equal(0, await job.RunOnceAsync(T0, CancellationToken.None)); // idempotent
    }

    [Fact]
    public void The_default_period_is_three_years()
    {
        Assert.Equal(TimeSpan.FromDays(3 * 365), _s.Host.Services.GetRequiredService<DmcaRetentionJob>().Retention);
    }
}
