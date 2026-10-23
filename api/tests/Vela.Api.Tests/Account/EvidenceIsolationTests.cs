using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Account;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Legal;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Account.AccountKit;
using static Vela.Api.Tests.Account.ExportKit;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Account;

[Collection(PostgresCollection.Name)]
public class EvidenceIsolationTests(PostgresFixture pg)
{
    private const string Marker = "EVIDENCE-MARKER-7f3a";
    private const string Claimant = "Clarence Claimant-Secret";

    private static async Task SeedHeldAsync(ProfileTestHost h, World w)
    {
        await using var db = h.Db();
        var hold = new EvidenceHold
        {
            Id = Guid.NewGuid(), UserId = w.U.Id, WorkId = w.UWork, TargetType = "work", BlobPrefix = "evidence/h/", Reason = Marker,
            HoldUntil = ProfileTestHost.Now.AddDays(365), Snapshot = $"{{\"note\":\"{Marker}\"}}", CreatedByAdminId = w.O.Id, CreatedAt = ProfileTestHost.Now,
        };
        db.EvidenceHolds.Add(hold);
        db.DmcaNotices.Add(new DmcaNotice
        {
            Id = Guid.NewGuid(), WorkId = w.UWork, OwnerUserId = w.U.Id, ClaimantName = Claimant, ClaimantEmail = "secret.claimant@claims.test",
            ClaimantAddress = "9 Hidden Lane", ClaimantPhone = "555-0000", WorkReference = "ref", OriginalWorkDescription = "desc", SignatureName = "sig",
            GoodFaithStatement = true, PerjuryStatement = true, Status = DmcaStatus.Takedown, TakedownAt = ProfileTestHost.Now, CreatedAt = ProfileTestHost.Now,
            ClientIp = "198.51.100.77",
        });
        await db.SaveChangesAsync();
        h.Blobs.Put($"evidence/{hold.Id}/original", [1, 2, 3]);
    }

    [Fact]
    public async Task The_owners_export_holds_no_evidence_and_only_the_fact_of_notices_about_their_works()
    {
        using var h = NewHost(pg, await pg.CreateIsolatedDatabaseAsync());
        var w = await Seed(h);
        await SeedHeldAsync(h, w);

        var (_, zip) = await RequestAndBuild(h, w.U);

        Assert.DoesNotContain(zip.Names, n => n.Contains("evidence", StringComparison.OrdinalIgnoreCase));
        var all = zip.AllText;
        foreach (var leak in new[] { Marker, "evidence", Claimant, "secret.claimant@claims.test", "9 Hidden Lane", "555-0000", "198.51.100.77", "ncmec" })
            Assert.DoesNotContain(leak, all, StringComparison.OrdinalIgnoreCase);
        var notice = zip.Json("dmca_notices_about_my_works.json").EnumerateArray().Single();
        Assert.Equal(["counterNoticeAt", "forwardedAt", "noticeId", "receivedAt", "status", "takedownAt", "workId"],
            notice.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("takedown", notice.GetProperty("status").GetString());
        Assert.Equal(w.UWork, notice.GetProperty("workId").GetGuid());
    }

    [Fact]
    public async Task A_user_without_notices_gets_an_empty_list_and_other_users_notices_stay_out()
    {
        using var h = NewHost(pg, await pg.CreateIsolatedDatabaseAsync());
        var w = await Seed(h);
        await SeedHeldAsync(h, w);

        var (_, zip) = await RequestAndBuild(h, w.T);

        Assert.Empty(zip.Json("dmca_notices_about_my_works.json").EnumerateArray());
        Assert.DoesNotContain(Marker, zip.AllText);
    }

    [Fact]
    public async Task No_public_endpoint_returns_held_evidence()
    {
        using var site = await AdminSite.Create(pg, isolated: true);
        var admin = await site.NewAdminUser();
        var owner = await site.OwnerWithEmail();
        var (_, work) = await site.Story("Held story", owner);
        var reporter = await site.NewUser();
        var report = await site.Report(reporter, "work", work, "illegal_content");
        var hold = (await site.MarkIllegal(admin, report, Marker)).HoldId!.Value;
        var viewer = await site.NewUser();

        var urls = new[]
        {
            "/v1/me", "/v1/feed?tab=discover", "/v1/feed?tab=following", $"/v1/works/{work}", $"/v1/artists/{owner.Id}", $"/v1/artists/{owner.Id}/works",
            "/v1/notifications", "/v1/search?q=held", "/v1/search?q=" + Marker, $"/v1/works/{work}/chapters/0/replies",
        };
        foreach (var u in new[] { viewer, reporter, owner })
            foreach (var url in urls)
            {
                var text = (await u.Client.GetAsync(url)).Content.ReadAsStringAsync().Result;
                Assert.DoesNotContain(Marker, text);
                Assert.DoesNotContain(hold.ToString(), text);
                Assert.DoesNotContain("evidence/", text);
            }
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await site.Host.Anonymous().GetAsync($"/v1/evidence/{hold}")).StatusCode);
    }

    [Fact]
    public async Task Only_admin_and_data_code_ever_touches_the_evidence_table_and_notices_stay_out_of_public_code()
    {
        var root = SourceRoot();
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories))
            .Where(f => !f.Contains("/obj/") && !f.Contains("/Migrations/")).ToList();
        string Rel(string f) => Path.GetRelativePath(root, f).Replace('\\', '/');

        var evidenceUsers = files.Where(f => File.ReadAllText(f).Contains("EvidenceHolds")).Select(Rel).ToList();
        Assert.NotEmpty(evidenceUsers);
        Assert.All(evidenceUsers, f => Assert.True(f.StartsWith("Admin/") || f.StartsWith("Data/"), $"{f} reads evidence outside Admin/ and Data/"));

        var noticeUsers = files.Where(f => File.ReadAllText(f).Contains("DmcaNotices")).Select(Rel).ToList();
        Assert.All(noticeUsers, f => Assert.True(
            f.StartsWith("Admin/") || f.StartsWith("Data/") || f.StartsWith("Legal/") || f is "Account/ExportDocuments.cs" or "Account/ExportArchiveBuilder.cs", $"{f} reads DMCA notices"));
        // The export reads notices only through the owner-scoped fact list.
        Assert.DoesNotContain("ClaimantEmail", File.ReadAllText(Path.Combine(root, "Account", "ExportDocuments.cs")));
        await Task.CompletedTask;
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Vela.Api"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Vela.Api");
    }

    [Fact]
    public async Task Deleting_the_owners_account_keeps_the_hold_its_blobs_and_the_notice_until_retention_removes_them()
    {
        using var site = await AdminSite.Create(pg, isolated: true);
        var admin = await site.NewAdminUser();
        var owner = await site.OwnerWithEmail();
        var (_, work) = await site.Story("To be held", owner);
        var http = site.Host.Anonymous();
        var notice = (await http.PostNotice(DmcaKit.Valid(work.ToString()))).NoticeId;
        var report = (await site.DmcaReports()).Single().Id;
        var holdId = (await site.MarkIllegal(admin, report)).HoldId!.Value;
        var hold = await site.Hold(holdId);

        using (var scope = site.Host.Scope())
            await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().RequestAsync(owner.Id);
        await RunJob(site.Host, owner.Id);

        await using (var db = site.Host.Db())
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == owner.Id));
            Assert.False(await db.Works.AnyAsync(w => w.Id == work));
        }
        var kept = await site.Hold(holdId);
        Assert.Equal((owner.Id, work), (kept.UserId, kept.WorkId));
        Assert.Contains("To be held", kept.Snapshot);
        Assert.True(site.Host.Blobs.Exists($"evidence/{holdId}/original"));
        Assert.False(site.Host.Blobs.Exists($"originals/works/{work}"));
        Assert.Equal(work, (await site.Notice(notice)).WorkId);

        var removed = await site.Service<EvidenceRetentionJob>().RunOnceAsync(hold.HoldUntil, CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Empty(await site.Holds());
        Assert.Equal("", site.EvidenceBlobs(holdId));
    }
}
