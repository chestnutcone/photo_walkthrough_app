using Reply = Vela.Api.Data.Entities.Reply;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Moderation;
using Vela.Api.Tests.Profiles;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Admin;

/// <summary>Seeding helpers: real users, real publishing through the pipeline, real reports through the API.</summary>
public static class ModerationKit
{
    public static readonly string Unsafe = FakeContentSafety.UnsafeMarker;

    public static async Task<TestUser> NewUser(this AdminSite s, string? email = null) =>
        await s.Host.SignInOnboarded(ProfileTestHost.UniqueHandle());

    public static async Task<Guid> NewAdminUser(this AdminSite s)
    {
        var u = await s.NewUser();
        await MakeAdmin(s.Host, u.Id);
        return u.Id;
    }

    public static Task RunPipeline(this AdminSite s) =>
        s.Host.Services.GetRequiredService<PublishPipeline>().RunOnceAsync(ProfileTestHost.Now + TimeSpan.FromSeconds(10), CancellationToken.None);

    /// <summary>A story that has been requested for publishing and swept by the pipeline (published, or blocked when the title says so).</summary>
    public static async Task<(TestUser Owner, Guid Work)> Story(this AdminSite s, string title = "Quiet harbour", TestUser? owner = null)
    {
        var u = owner ?? await s.NewUser();
        var id = await ReadyDraft(s.Host, u, title);
        var r = await u.Post($"/v1/works/{id}/publish", new { });
        Assert.True(r.Body.GetProperty("success").GetBoolean(), r.Body.ToString());
        await s.RunPipeline();
        return (u, id);
    }

    public static Task<(TestUser Owner, Guid Work)> BlockedStory(this AdminSite s) => s.Story("Bad " + Unsafe);

    public static async Task<Guid> Report(this AdminSite s, TestUser reporter, string type, Guid target, string reason = "spam", string? note = null)
    {
        var r = await reporter.Post("/v1/reports", new { targetType = type, targetId = target, reason, note });
        Assert.True(r.Body.GetProperty("success").GetBoolean(), r.Body.ToString());
        await using var db = s.Host.Db();
        return await db.Reports.Where(x => x.ReporterId == reporter.Id && x.TargetId == target).Select(x => x.Id).SingleAsync();
    }

    public static async Task<Guid> ReplyOn(this AdminSite s, TestUser author, Guid work, string text = "Lovely light")
    {
        var r = await author.Post($"/v1/works/{work}/chapters/0/replies", new { body = text });
        Assert.True(r.Body.GetProperty("success").GetBoolean(), r.Body.ToString());
        return r.Data.GetProperty("id").GetGuid();
    }

    public static async Task<Guid> AutoFlagId(this AdminSite s, string type, Guid target)
    {
        await using var db = s.Host.Db();
        return await db.Reports.Where(x => x.Source == "auto_flag" && x.TargetType == type && x.TargetId == target).Select(x => x.Id).SingleAsync();
    }

    public static async Task<T> Mod<T>(this AdminSite s, Func<IModerationService, Task<T>> call)
    {
        using var scope = s.Host.Scope();
        return await call(scope.ServiceProvider.GetRequiredService<IModerationService>());
    }

    public static async Task<Report> LoadReport(this AdminSite s, Guid id)
    {
        await using var db = s.Host.Db();
        return await db.Reports.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    public static async Task<List<AdminAction>> Audit(this AdminSite s)
    {
        await using var db = s.Host.Db();
        return await db.AdminActions.AsNoTracking().OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync();
    }

    public static async Task<Reply> LoadReply(this AdminSite s, Guid id)
    {
        await using var db = s.Host.Db();
        return await db.Replies.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    public static async Task<User> LoadUser(this AdminSite s, Guid id)
    {
        await using var db = s.Host.Db();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    public static string[] Variants(Guid work) => [$"works/{work}/thumb.webp", $"works/{work}/feed.webp", $"works/{work}/full.webp"];
}
