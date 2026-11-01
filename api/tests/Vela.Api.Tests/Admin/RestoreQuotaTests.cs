using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

/// <summary>
/// FINALAPI M5, defined behaviour: an admin restore corrects a moderation decision, so it is never refused for quota and may
/// leave the owner above max_works; the owner then cannot start a new story until they are below the limit again.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RestoreQuotaTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync() { _s = await AdminSite.Create(pg); _admin = await _s.NewAdminUser(); }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Restore_over_the_quota_succeeds_and_new_drafts_are_refused_until_below_the_limit()
    {
        var (owner, work) = await _s.BlockedStory();
        Guid flag;
        await using (var db = _s.Host.Db())
        {
            flag = await db.Reports.Where(r => r.TargetType == "work" && r.TargetId == work).Select(r => r.Id).SingleAsync();
            // The blocked story frees its slot, so the owner fills the quota (20 for Freemium) with other stories.
            for (var i = 0; i < 20; i++)
                db.Works.Add(new Work { Id = Guid.NewGuid(), ArtistId = owner.Id, Title = "t", Status = WorkStatus.Published, ImageBlob = "b",
                    Width = 3, Height = 4, CreatedAt = ProfileTestHost.Now, PublishedAt = ProfileTestHost.Now });
            await db.SaveChangesAsync();
        }

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RestoreAsync(_admin, flag, "false positive")));

        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        var me = await owner.Get("/v1/me");
        Assert.Equal(21, me.Data.GetProperty("usedWorks").GetInt32());
        var draft = await owner.Post("/v1/works", new { contentType = "image/jpeg" });
        Assert.Equal(HttpStatusCode.Conflict, draft.Status);
        Assert.Equal("quota_exceeded", draft.ErrorCode);
    }
}
