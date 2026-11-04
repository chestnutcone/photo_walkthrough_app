using Vela.Api.Social;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using ReplyEntity = Vela.Api.Data.Entities.Reply;

namespace Vela.Api.Tests.Social;

/// <summary>
/// S2 ruling on report brigading (R-2, e2e report Major), owner decision 2026-11-03: an illegal-content report hides at once
/// when the reporter's account is at least 24 h old (younger: queued and counted, no hide); any other reason hides only
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

    [Theory]
    [InlineData(24 * 60)]      // exactly 24 h: at least 24 h old, hides
    [InlineData(24 * 60 + 1)]  // over 24 h
    [InlineData(8 * 24 * 60)]  // established account
    public async Task Illegal_content_report_from_an_account_at_least_24_hours_old_hides_at_once(int ageMinutes)
    {
        var artist = await NewUser(); var rep = await AgedUser(TimeSpan.FromMinutes(ageMinutes));
        var wid = await SeedWork(artist.Id);

        Assert.Equal(HttpStatusCode.OK, (await rep.Post("/v1/reports", Rep("work", wid, "illegal_content"))).Status);

        Assert.True(await WorkHidden(wid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24 * 60 - 1)] // just under 24 h
    public async Task Illegal_content_report_from_a_younger_account_queues_but_does_not_hide(int ageMinutes)
    {
        // Owner decision 2026-11-03 (review I-5): a fresh account cannot take content down on its own.
        var artist = await NewUser(); var rep = await AgedUser(TimeSpan.FromMinutes(ageMinutes));
        var wid = await SeedWork(artist.Id);

        Assert.Equal(HttpStatusCode.OK, (await rep.Post("/v1/reports", Rep("work", wid, "illegal_content"))).Status);

        Assert.False(await WorkHidden(wid));
        await using var db = _h.Db();
        var row = await db.Reports.SingleAsync(r => r.TargetId == wid);
        Assert.Equal(("pending", "illegal_content"), (row.Status, row.Reason));
    }

    [Fact]
    public async Task Fresh_illegal_content_reports_count_towards_the_distinct_reporter_threshold()
    {
        var artist = await NewUser();
        var wid = await SeedWork(artist.Id);
        await (await NewUser()).Post("/v1/reports", Rep("work", wid, "illegal_content"));
        await (await NewUser()).Post("/v1/reports", Rep("work", wid, "spam"));
        Assert.False(await WorkHidden(wid));

        await (await NewUser()).Post("/v1/reports", Rep("work", wid, "illegal_content"));

        Assert.True(await WorkHidden(wid));
    }

    [Fact]
    public async Task Fresh_illegal_content_report_on_a_reply_does_not_hide_it_but_an_older_one_does()
    {
        var artist = await NewUser(); var author = await NewUser();
        var rid = await SeedReply(await SeedWork(artist.Id), author.Id);

        await (await AgedUser(TimeSpan.FromHours(23))).Post("/v1/reports", Rep("reply", rid, "illegal_content"));
        Assert.False(await ReplyHidden(rid));

        await (await AgedUser(TimeSpan.FromHours(25))).Post("/v1/reports", Rep("reply", rid, "illegal_content"));
        Assert.True(await ReplyHidden(rid));
    }

    [Fact]
    public async Task Minimum_account_age_for_an_instant_illegal_content_hide_is_configurable()
    {
        using var h = new ProfileTestHost(pg, new Dictionary<string, string> { ["Reports:IllegalContentInstantHideMinAgeHours"] = "48" });
        var artist = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var rep = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        await using (var db = h.Db())
            await db.Users.Where(x => x.Id == rep.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, ProfileTestHost.Now - TimeSpan.FromHours(30)));
        var wid = await SeedWork(artist.Id);

        await rep.Post("/v1/reports", Rep("work", wid, "illegal_content"));

        Assert.False(await WorkHidden(wid));
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
    public async Task Reports_from_accounts_suspended_or_deleted_since_do_not_count()
    {
        // S2 fix (review M-14).
        var artist = await NewUser();
        var wid = await SeedWork(artist.Id);
        var reporters = new[] { await NewUser(), await NewUser(), await NewUser() };
        await reporters[0].Post("/v1/reports", Rep("work", wid, "spam"));
        await reporters[1].Post("/v1/reports", Rep("work", wid, "spam"));
        await using (var db = _h.Db())
        {
            await db.Users.Where(u => u.Id == reporters[0].Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));
        }

        await reporters[2].Post("/v1/reports", Rep("work", wid, "spam"));
        Assert.False(await WorkHidden(wid));

        await using (var db = _h.Db())
        {
            await db.Users.Where(u => u.Id == reporters[0].Id).ExecuteUpdateAsync(x => x
                .SetProperty(u => u.SuspendedAt, (DateTimeOffset?)null).SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        }
        var fourth = await NewUser();
        await fourth.Post("/v1/reports", Rep("work", wid, "spam"));
        Assert.True(await WorkHidden(wid)); // reporters 1, 2 and 4 are active
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

/// <summary>DECISIONS fix M-4: the instant-hide account age is checked when the API starts.</summary>
public class ReportOptionsValidationTests
{
    private static ReportOptions Load(string? hours)
    {
        var values = new Dictionary<string, string?>();
        if (hours is not null) values["Reports:IllegalContentInstantHideMinAgeHours"] = hours;
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return ReportOptions.Load(config);
    }

    [Theory]
    [InlineData(null, 24)]
    [InlineData("0", 0)]
    [InlineData("48", 48)]
    [InlineData("8760", 8760)]
    public void Accepts_zero_to_one_year(string? hours, int expected) =>
        Assert.Equal(expected, Load(hours).IllegalContentInstantHideMinAgeHours);

    [Theory]
    [InlineData("-1")]
    [InlineData("8761")]
    [InlineData("2147483647")]
    public void Refuses_negative_or_absurd_values_with_a_clear_message(string hours)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Load(hours));
        Assert.Contains("Reports:IllegalContentInstantHideMinAgeHours", ex.Message);
        Assert.Contains("8760", ex.Message);
    }

    [Fact]
    public void The_api_refuses_to_start_with_a_negative_value()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Reports:IllegalContentInstantHideMinAgeHours"] = "-5" }).Build();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddSafety(config));
    }
}
