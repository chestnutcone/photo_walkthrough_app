using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Tests.Admin;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Legal;

public static class DmcaFlowKit
{
    public static async Task<T> Dm<T>(this AdminSite s, Func<Vela.Api.Admin.IDmcaAdminService, Task<T>> call)
    {
        using var scope = s.Host.Scope();
        return await call(scope.ServiceProvider.GetRequiredService<Vela.Api.Admin.IDmcaAdminService>());
    }

    /// <summary>A story by an owner with a login email, plus a matched pending notice about it.</summary>
    public static async Task<(Vela.Api.Tests.Profiles.TestUser Owner, Guid Work, Guid Notice)> NoticedStory(
        this AdminSite s, HttpClient http, string title = "Quiet harbour", Dictionary<string, object?>? overrides = null)
    {
        var owner = await s.OwnerWithEmail($"owner-{Guid.NewGuid():N}@owners.test");
        var (_, work) = await s.Story(title, owner);
        var body = DmcaKit.Valid(work.ToString());
        foreach (var (k, v) in overrides ?? []) body[k] = v;
        var r = await http.PostNotice(body);
        Assert.Equal(HttpStatusCode.Accepted, r.Status);
        return (owner, work, r.NoticeId);
    }

    public static async Task<string> OwnerEmail(this AdminSite s, Guid userId)
    {
        await using var db = s.Host.Db();
        return await db.Identities.Where(i => i.UserId == userId && i.Provider == "email").Select(i => i.Email!).SingleAsync();
    }

    public static object Counter(string name = "Olive Owner", string address = "9 Side Road\nShelbyville", string? phone = "555-0199",
        string statement = "I own this photograph.", bool consent = true, bool goodFaith = true) =>
        new { name, address, phone, statement, consentToJurisdiction = consent, goodFaithBelief = goodFaith };
}

public static class WorkOwnerKit
{
    public static async Task<Guid> LoadWorkOwner(this AdminSite s, Guid work)
    {
        await using var db = s.Host.Db();
        return await db.Works.Where(w => w.Id == work).Select(w => w.ArtistId).SingleAsync();
    }
}
