using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;

namespace Vela.Api.Auth;

/// <summary>
/// Sign-in bookkeeping keeps raw email addresses and client IPs (<c>email_codes</c>, <c>verify_attempts</c>) only as long as the
/// limits that read them need: the longest window is one hour, so a day is generous. Runs hourly; safe to run twice.
/// </summary>
public sealed class AuthDataRetentionJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<AuthDataRetentionJob> log) : BackgroundService
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunOnceAsync(clock.GetUtcNow(), stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Auth data retention run failed ({Reason}).", e.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Deletes rows older than <see cref="Retention"/>; returns (email codes, verify attempts) removed.</summary>
    public async Task<(int Codes, int Attempts)> RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
        var cutoff = now - Retention;
        var codes = await db.EmailCodes.Where(c => c.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        var attempts = await db.VerifyAttempts.Where(a => a.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        if (codes + attempts > 0) log.LogInformation("Auth data retention removed {Codes} email codes and {Attempts} verify attempts.", codes, attempts);
        return (codes, attempts);
    }
}
