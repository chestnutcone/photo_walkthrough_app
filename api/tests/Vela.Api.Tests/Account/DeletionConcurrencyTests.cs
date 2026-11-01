using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Vela.Api.Account;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Account.AccountKit;

namespace Vela.Api.Tests.Account;

public sealed class CountingCdn : ICdnPurger
{
    public ConcurrentQueue<IReadOnlyList<string>> Calls { get; } = new();

    public async Task PurgeAsync(IReadOnlyList<string> urls)
    {
        Calls.Enqueue(urls);
        await Task.Delay(150); // keep every sweep inside the IO phase long enough to overlap
    }
}

public sealed class CountingApple : IAppleTokenRevoker
{
    public ConcurrentQueue<Guid> Revoked { get; } = new();

    public Task RevokeAsync(Identity identity)
    {
        Revoked.Enqueue(identity.Id);
        return Task.CompletedTask;
    }
}

public sealed class FaultingObserver : IDeletionStepObserver
{
    public int FailOnCall { get; set; }
    private int _calls;

    public Task AfterStepAsync() =>
        FailOnCall > 0 && Interlocked.Increment(ref _calls) == FailOnCall ? throw new InvalidOperationException("fault between row deletions") : Task.CompletedTask;
}

[Collection(PostgresCollection.Name)]
public class DeletionConcurrencyTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private async Task<(ProfileTestHost Host, string Cs)> Host(Action<IServiceCollection> extra)
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var h = NewHost(pg, cs, null, null, extra);
        _hosts.Add(h);
        return (h, cs);
    }

    [Fact]
    public async Task Concurrent_sweeps_delete_each_account_exactly_once()
    {
        var cdn = new CountingCdn(); var apple = new CountingApple();
        var (h, cs) = await Host(s => s.AddSingleton<ICdnPurger>(cdn).AddSingleton<IAppleTokenRevoker>(apple));
        var worker = h.Services.GetRequiredService<DeletionWorker>();

        for (var round = 0; round < 5; round++)
        {
            cdn.Calls.Clear(); apple.Revoked.Clear();
            var w = await Seed(h);
            await RequestDelete(w.U);
            await RequestDelete(w.T);
            var now = h.Clock.GetUtcNow();

            var done = await Task.WhenAll(worker.RunOnceAsync(now, CancellationToken.None), worker.RunOnceAsync(now, CancellationToken.None));

            Assert.Equal(2, done.Sum());
            Assert.Equal(0, await Count(cs, "users", "id", w.U.Id) + await Count(cs, "users", "id", w.T.Id));
            Assert.Equal(1, await Count(cs, "users", "id", w.O.Id));
            Assert.Single(apple.Revoked);
            Assert.Equal(2, cdn.Calls.Count); // one purge per deleted account
        }
    }

    [Fact]
    public async Task Concurrent_sweeps_over_one_account_run_the_job_once()
    {
        var cdn = new CountingCdn(); var apple = new CountingApple();
        var (h, cs) = await Host(s => s.AddSingleton<ICdnPurger>(cdn).AddSingleton<IAppleTokenRevoker>(apple));
        var worker = h.Services.GetRequiredService<DeletionWorker>();
        for (var round = 0; round < 5; round++)
        {
            cdn.Calls.Clear(); apple.Revoked.Clear();
            var w = await Seed(h);
            await RequestDelete(w.U);
            var now = h.Clock.GetUtcNow();

            var done = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => worker.RunOnceAsync(now, CancellationToken.None)));

            Assert.Equal(1, done.Sum());
            Assert.Equal(0, await Count(cs, "users", "id", w.U.Id));
            Assert.Single(apple.Revoked);
            Assert.Single(cdn.Calls);
        }
    }

    [Fact]
    public async Task A_failure_between_row_deletions_rolls_everything_back_and_the_next_sweep_completes()
    {
        var observer = new FaultingObserver { FailOnCall = 6 };
        var capture = new CapturingLoggerProvider();
        var (h, cs) = await Host(s => s.AddSingleton<IDeletionStepObserver>(observer).AddLogging(b => b.AddProvider(capture)));
        var w = await Seed(h);
        await RequestDelete(w.U);
        var before = await TableCounts(cs);
        var worker = h.Services.GetRequiredService<DeletionWorker>();

        var done = await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None);

        Assert.Equal(0, done);
        Assert.Equal(before, await TableCounts(cs)); // nothing partially deleted, in any table
        var row = (await Query(cs, $"SELECT deletion_failures, deletion_failed_at IS NOT NULL, deletion_lease_until IS NULL FROM users WHERE id = '{w.U.Id}'")).Single();
        Assert.Equal(["1", "True", "True"], row);
        var logs = string.Join("\n", capture.Lines);
        Assert.Contains("InvalidOperationException", logs);
        Assert.DoesNotContain("fault between row deletions", logs);

        observer.FailOnCall = 0;
        await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None); // inside the back-off window
        Assert.Equal(1, await Count(cs, "users", "id", w.U.Id));
        h.Clock.Advance(DeletionWorker.Backoff + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None));

        Assert.Equal(0, await Count(cs, "users", "id", w.U.Id));
        Assert.Equal(0, await Count(cs, "works", "artist_id", w.U.Id));
        Assert.Equal(1, await Count(cs, "users", "id", w.O.Id));
        Assert.Equal(1, await Count(cs, "works", "artist_id", w.O.Id));
        Assert.Equal(1, await Count(cs, "works", "artist_id", w.T.Id));
    }

    [Fact]
    public async Task A_crashed_claim_is_retried_once_its_lease_expires()
    {
        var (h, cs) = await Host(_ => { });
        var w = await Seed(h);
        await RequestDelete(w.U);
        await Execute(cs, $"UPDATE users SET deletion_lease_until = '{h.Clock.GetUtcNow().AddMinutes(10):O}' WHERE id = '{w.U.Id}'");
        var worker = h.Services.GetRequiredService<DeletionWorker>();

        Assert.Equal(0, await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None)); // leased by "another instance"
        h.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(1, await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None));
    }

    [Fact]
    public async Task No_row_lock_is_held_during_blob_cdn_and_apple_work_and_late_rows_are_cleaned_by_the_row_phase()
    {
        var cdn = new RecordingCdnPurger();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cdn.OnPurge = async () => { entered.TrySetResult(); await release.Task; };
        var (h, cs) = await Host(s => s.AddSingleton<ICdnPurger>(cdn));
        var w = await Seed(h);
        await RequestDelete(w.U);
        var sweep = h.Services.GetRequiredService<DeletionWorker>().RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // The user row is not locked: an insert that takes a KEY SHARE lock on it (a new follow) must not wait.
        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 5;
            cmd.CommandText = $"INSERT INTO follows (follower_id, followee_id, created_at) VALUES ('{w.T.Id}', '{w.U.Id}', now())";
            await cmd.ExecuteNonQueryAsync();
        }
        Assert.Equal(1, await Count(cs, "follows", "followee_id", w.U.Id) - 1); // the seeded follow (O) plus the new one
        release.SetResult();

        Assert.Equal(1, await sweep);
        Assert.Equal(0, await Count(cs, "follows", "followee_id", w.U.Id));
        Assert.Equal(0, await Count(cs, "users", "id", w.U.Id));
    }

    private static async Task Execute(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> TableCounts(string cs)
    {
        var tables = (await Query(cs, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY 1")).Select(r => r[0]);
        var parts = new List<string>();
        foreach (var t in tables) parts.Add($"{t}={(await Query(cs, $"SELECT count(*) FROM \"{t}\""))[0][0]}");
        var user = await Query(cs, "SELECT string_agg(id::text || coalesce(deleted_at::text, ''), ',' ORDER BY id) FROM users");
        return string.Join(";", parts) + "|" + user[0][0];
    }
}
