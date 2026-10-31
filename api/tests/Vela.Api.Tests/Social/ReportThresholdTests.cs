using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using ReplyEntity = Vela.Api.Data.Entities.Reply;

namespace Vela.Api.Tests.Social;

/// <summary>
/// S2 ruling on report brigading (R-2, e2e report Major): an illegal-content report hides at once; any other reason hides only
/// once 3 distinct accounts have reported the item, or one reporter whose account is older than 7 days has.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ReportThresholdTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _h = new(pg);
    public void Dispose() => _h.Dispose();

    private Task<TestUser> NewUser() => _h.SignInOnboarded(ProfileTestHost.UniqueHandle());

    private async Task<TestUser> AgedUser(TimeSpan age)
    {
        var u = await NewUser();
        await using var db = _h.Db();
        await db.Users.Where(x => x.Id == u.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, ProfileTestHost.Now - age));
        return u;
    }

    private async Task<Guid> SeedWork(Guid artist)
    {
        await using var db = _h.Db();
        var w = new Work { Id = Guid.NewGuid(), ArtistId = artist, Title = "t", Status = WorkStatus.Published, ImageBlob = "b", Width = 3, Height = 4, CreatedAt = ProfileTestHost.Now };
        db.Works.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    }

    private async Task<Guid> SeedReply(Guid workId, Guid author)
    {
        await using var db = _h.Db();
        var r = new ReplyEntity { Id = Guid.NewGuid(), WorkId = workId, ChapterIndex = 0, AuthorId = author, Text = "hi", CreatedAt = ProfileTestHost.Now };
        db.Replies.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    private static object Rep(string type, Guid id, string reason) => new { targetType = type, targetId = id, reason };

    private async Task<bool> WorkHidden(Guid id)
    {
        await using var db = _h.Db();
        return (await db.Works.SingleAsync(w => w.Id == id)).ReportHidden;
    }

    private async Task<bool> ReplyHidden(Guid id)
    {
        await using var db = _h.Db();
        return (await db.Replies.SingleAsync(r => r.Id == id)).Hidden;
    }

    [Fact]
    public async Task Illegal_content_report_from_a_new_account_hides_the_work_at_once()
    {
        var artist = await NewUser(); var rep = await NewUser();
        var wid = await SeedWork(artist.Id);

        Assert.Equal(HttpStatusCode.OK, (await rep.Post("/v1/reports", Rep("work", wid, "illegal_content"))).Status);

        Assert.True(await WorkHidden(wid));
    }

    [Theory]
    [InlineData("spam")]
    [InlineData("harassment")]
    [InlineData("copyright")]
    [InlineData("other")]
    public async Task One_report_from_a_new_account_queues_but_does_not_hide(string reason)
    {
        var artist = await NewUser(); var rep = await NewUser();
        var wid = await SeedWork(artist.Id);

        Assert.Equal(HttpStatusCode.OK, (await rep.Post("/v1/reports", Rep("work", wid, reason))).Status);

        Assert.False(await WorkHidden(wid));
        await using var db = _h.Db();
        Assert.Equal("pending", (await db.Reports.SingleAsync(r => r.TargetId == wid)).Status);
    }

    [Fact]
    public async Task Third_distinct_new_reporter_hides_the_work()
    {
        var artist = await NewUser();
        var wid = await SeedWork(artist.Id);
        var reporters = new[] { await NewUser(), await NewUser(), await NewUser() };

        await reporters[0].Post("/v1/reports", Rep("work", wid, "spam"));
        await reporters[1].Post("/v1/reports", Rep("work", wid, "harassment"));
        Assert.False(await WorkHidden(wid));

        await reporters[2].Post("/v1/reports", Rep("work", wid, "other"));
        Assert.True(await WorkHidden(wid));
    }

    [Fact]
    public async Task Repeated_reports_by_the_same_account_count_once()
    {
        var artist = await NewUser(); var rep = await NewUser();
        var wid = await SeedWork(artist.Id);

        for (var i = 0; i < 3; i++) await rep.Post("/v1/reports", Rep("work", wid, "spam"));

        Assert.False(await WorkHidden(wid));
    }

    [Fact]
    public async Task Report_from_an_account_older_than_seven_days_hides_at_once()
    {
        var artist = await NewUser(); var rep = await AgedUser(TimeSpan.FromDays(8));
        var wid = await SeedWork(artist.Id);

        await rep.Post("/v1/reports", Rep("work", wid, "spam"));

        Assert.True(await WorkHidden(wid));
    }

    [Fact]
    public async Task Account_of_exactly_seven_days_is_not_yet_established()
    {
        var artist = await NewUser(); var rep = await AgedUser(TimeSpan.FromDays(7));
        var wid = await SeedWork(artist.Id);

        await rep.Post("/v1/reports", Rep("work", wid, "spam"));

        Assert.False(await WorkHidden(wid));
    }

    [Fact]
    public async Task Reply_follows_the_same_threshold()
    {
        var artist = await NewUser(); var author = await NewUser();
        var rid = await SeedReply(await SeedWork(artist.Id), author.Id);

        await (await NewUser()).Post("/v1/reports", Rep("reply", rid, "harassment"));
        Assert.False(await ReplyHidden(rid));
        await (await AgedUser(TimeSpan.FromDays(30))).Post("/v1/reports", Rep("reply", rid, "harassment"));
        Assert.True(await ReplyHidden(rid));
    }

    [Fact]
    public async Task Resolved_reports_do_not_count_towards_the_threshold()
    {
        var artist = await NewUser();
        var wid = await SeedWork(artist.Id);
        var a = await NewUser(); var b = await NewUser();
        await a.Post("/v1/reports", Rep("work", wid, "spam"));
        await b.Post("/v1/reports", Rep("work", wid, "spam"));
        await using (var db = _h.Db())
            await db.Reports.Where(r => r.TargetId == wid).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "dismissed"));

        await (await NewUser()).Post("/v1/reports", Rep("work", wid, "spam"));

        Assert.False(await WorkHidden(wid));
    }

    [Fact]
    public async Task Parallel_new_reporters_cannot_slip_under_the_threshold()
    {
        var artist = await NewUser();
        var wid = await SeedWork(artist.Id);
        var reporters = new List<TestUser>();
        for (var i = 0; i < 3; i++) reporters.Add(await NewUser());

        await Task.WhenAll(reporters.Select(r => r.Post("/v1/reports", Rep("work", wid, "spam"))));

        Assert.True(await WorkHidden(wid));
    }
}
