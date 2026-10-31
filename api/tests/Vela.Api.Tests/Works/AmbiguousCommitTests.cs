using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Works;

/// <summary>Throws from the Nth commit after it is armed, once the database has committed: the "ambiguous commit" failure.</summary>
public sealed class ThrowAfterCommit : DbTransactionInterceptor
{
    private int _countdown;
    public void ArmForCommit(int nth) => Volatile.Write(ref _countdown, nth);

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default) =>
        Interlocked.Decrement(ref _countdown) == 0 ? throw new TimeoutException("connection lost after COMMIT") : Task.CompletedTask;
}

/// <summary>Review round 1, F6: a commit that fails ambiguously must not delete the sealed blob of a publish that did commit.</summary>
[Collection(PostgresCollection.Name)]
public class AmbiguousCommitTests : IDisposable
{
    private readonly ThrowAfterCommit _fault = new();
    private readonly ProfileTestHost _host;

    public AmbiguousCommitTests(PostgresFixture pg) =>
        _host = new ProfileTestHost(pg, configureServices: s => s.AddSingleton<IInterceptor>(_fault));

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_publish_whose_completion_commit_throws_after_committing_keeps_its_sealed_original()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var id = await ReadyDraft(_host, u);
        _fault.ArmForCommit(2); // 1 = the claim, 2 = the completion

        var r = await u.Post($"/v1/works/{id}/publish", new { });

        Assert.Equal(HttpStatusCode.InternalServerError, r.Status);
        var w = await Load(_host, id);
        Assert.Equal(WorkStatus.Pending, w.Status); // it did commit
        Assert.NotNull(w.SealedOriginalBlob);
        Assert.True(_host.Blobs.Exists(w.SealedOriginalBlob!), "the committed work's sealed original was deleted");
    }

    [Fact]
    public async Task A_publish_that_really_rolled_back_still_removes_its_sealed_copy()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var id = await ReadyDraft(_host, u);
        _host.Blobs.FailWrites = name => name.StartsWith($"works/{id}/full", StringComparison.Ordinal); // fails after sealing

        var r = await u.Post($"/v1/works/{id}/publish", new { });

        Assert.Equal(HttpStatusCode.InternalServerError, r.Status);
        Assert.Equal(WorkStatus.Draft, (await Load(_host, id)).Status);
        Assert.DoesNotContain(_host.Blobs.Names, n => n.StartsWith($"originals/sealed/{id}/", StringComparison.Ordinal));
    }
}
