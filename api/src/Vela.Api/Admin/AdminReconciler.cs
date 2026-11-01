using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;

namespace Vela.Api.Admin;

/// <summary>Clears <c>Users.IsAdmin</c> for everyone whose email identity is no longer in <c>Admin:Emails</c>.</summary>
public sealed class AdminReconciler(IServiceScopeFactory scopes, IAdminAllowList allow)
{
    /// <summary>Returns how many users lost the flag.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
        var admins = await db.Users.AsNoTracking().Where(u => u.IsAdmin).Select(u => u.Id).ToListAsync(ct);
        if (admins.Count == 0) return 0;
        var emails = await db.Identities.AsNoTracking().Where(i => admins.Contains(i.UserId) && i.Provider == "email")
            .Select(i => new { i.UserId, i.Subject }).ToListAsync(ct);
        var keep = emails.Where(e => allow.Contains(e.Subject)).Select(e => e.UserId).ToHashSet();
        var stale = admins.Where(a => !keep.Contains(a)).ToList();
        if (stale.Count == 0) return 0;
        return await db.Users.Where(u => stale.Contains(u.Id) && u.IsAdmin).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, false), ct);
    }
}

/// <summary>Runs the reconciliation at startup and every ten minutes. Logs a count only.</summary>
public sealed class AdminReconcileWorker(AdminReconciler reconciler, ILogger<AdminReconcileWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var cleared = await reconciler.RunOnceAsync(stoppingToken);
                if (cleared > 0) log.LogWarning("Cleared the admin flag from {Count} accounts no longer on the allow-list.", cleared);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Admin reconciliation failed ({Reason}).", e.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
