using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

/// <summary>Helpers for the evidence-hold tests.</summary>
public static class EvidenceKit
{
    public const string OwnerEmail = "owner.secret@evidence-mail.test";

    public static Task<MarkIllegalResult> MarkIllegal(this AdminSite s, Guid admin, Guid report, string reason = "clear violation") =>
        s.Mod(m => m.MarkIllegalAsync(admin, report, reason));

    public static async Task<List<EvidenceHold>> Holds(this AdminSite s)
    {
        await using var db = s.Host.Db();
        return await db.EvidenceHolds.AsNoTracking().OrderBy(h => h.CreatedAt).ThenBy(h => h.Id).ToListAsync();
    }

    public static async Task<EvidenceHold> Hold(this AdminSite s, Guid id)
    {
        await using var db = s.Host.Db();
        return await db.EvidenceHolds.AsNoTracking().SingleAsync(h => h.Id == id);
    }

    public static JsonElement Snapshot(this EvidenceHold hold) => JsonDocument.Parse(hold.Snapshot).RootElement.Clone();

    /// <summary>A user with a login email (so the snapshot has an identity worth capturing).</summary>
    public static async Task<TestUser> OwnerWithEmail(this AdminSite s, string email = OwnerEmail)
    {
        var u = await s.NewUser();
        await using var db = s.Host.Db();
        db.Identities.Add(new Identity { Id = Guid.NewGuid(), UserId = u.Id, Provider = "email", Subject = email, Email = email });
        await db.SaveChangesAsync();
        return u;
    }

    /// <summary>An onboarded user who is a current admin (allow-listed email identity).</summary>
    public static async Task<TestUser> AdminOwner(this AdminSite s)
    {
        var u = await s.NewUser();
        var email = $"adm-{Guid.NewGuid():N}@vela-admin.test";
        await using (var db = s.Host.Db())
        {
            db.Identities.Add(new Identity { Id = Guid.NewGuid(), UserId = u.Id, Provider = "email", Subject = email, Email = email });
            await db.SaveChangesAsync();
        }
        s.Allow.Add(email);
        await Works.WorkTestKit.MakeAdmin(s.Host, u.Id);
        return u;
    }

    public static string EvidenceBlobs(this AdminSite s, Guid holdId) =>
        string.Join(",", s.Host.Blobs.Names.Where(n => n.StartsWith($"evidence/{holdId}/", StringComparison.Ordinal)));

    public static T Service<T>(this AdminSite s) where T : notnull => s.Host.Services.GetRequiredService<T>();
}

public static class EvidenceServiceKit
{
    public static async Task<T> Mod2<T>(this AdminSite s, Func<IEvidenceService, Task<T>> call)
    {
        using var scope = s.Host.Scope();
        return await call(scope.ServiceProvider.GetRequiredService<IEvidenceService>());
    }
}
