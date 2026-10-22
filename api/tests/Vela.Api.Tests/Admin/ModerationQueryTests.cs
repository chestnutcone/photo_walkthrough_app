using Microsoft.EntityFrameworkCore;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class ModerationQueryTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync() { _s = await AdminSite.Create(pg, isolated: true); _admin = await _s.NewAdminUser(); }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private async Task<Guid> Seed(string source, string reason, string status, DateTimeOffset at, string type = "work", Guid? target = null, Guid? reporter = null, string? note = null)
    {
        await using var db = _s.Host.Db();
        var r = new Report { Id = Guid.NewGuid(), Source = source, Reason = reason, Status = status, TargetType = type, TargetId = target ?? Guid.NewGuid(), ReporterId = reporter, Note = note, CreatedAt = at };
        db.Reports.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    private static DateTimeOffset T(int minutes) => ProfileTestHost.Now.AddMinutes(-minutes);

    [Fact]
    public async Task Queue_is_oldest_first_and_defaults_to_nothing_filtered_when_status_is_null()
    {
        var newest = await Seed("user_report", "spam", "pending", T(1));
        var oldest = await Seed("user_report", "spam", "pending", T(30));
        var mid = await Seed("auto_flag", "auto:unsafe_text:6", "pending", T(10));
        var done = await Seed("user_report", "spam", "dismissed", T(60));

        var all = await _s.Mod(m => m.QueueAsync(null, null, null, null, 50));
        Assert.Equal([done, oldest, mid, newest], all.Items.Select(i => i.Id));
        var pending = await _s.Mod(m => m.QueueAsync(ReportStatus.Pending, null, null, null, 50));
        Assert.Equal([oldest, mid, newest], pending.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Queue_filters_by_status_reason_and_source_and_combines_them()
    {
        var a = await Seed("user_report", "spam", "pending", T(5));
        var b = await Seed("user_report", "harassment", "pending", T(4));
        var c = await Seed("auto_flag", "auto:unsafe_image:6", "pending", T(3));
        var d = await Seed("user_report", "spam", "actioned", T(2));
        var e = await Seed("dmca", "copyright", "pending", T(1));

        Assert.Equal([a, b, c, e], (await _s.Mod(m => m.QueueAsync(ReportStatus.Pending, null, null, null, 50))).Items.Select(i => i.Id));
        Assert.Equal([d], (await _s.Mod(m => m.QueueAsync(ReportStatus.Actioned, null, null, null, 50))).Items.Select(i => i.Id));
        Assert.Equal([a], (await _s.Mod(m => m.QueueAsync(ReportStatus.Pending, "spam", null, null, 50))).Items.Select(i => i.Id));
        Assert.Equal([c], (await _s.Mod(m => m.QueueAsync(null, null, "auto_flag", null, 50))).Items.Select(i => i.Id));
        Assert.Equal([e], (await _s.Mod(m => m.QueueAsync(null, null, "dmca", null, 50))).Items.Select(i => i.Id));
        Assert.Equal([a, d], (await _s.Mod(m => m.QueueAsync(null, "spam", "user_report", null, 50))).Items.Select(i => i.Id));
        Assert.Empty((await _s.Mod(m => m.QueueAsync(ReportStatus.Dismissed, null, null, null, 50))).Items);
    }

    [Fact]
    public async Task Queue_pagination_has_no_duplicates_or_gaps_even_with_equal_timestamps()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++) ids.Add(await Seed("user_report", "spam", "pending", T(i < 6 ? 20 : 20 - i)));
        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await _s.Mod(m => m.QueueAsync(ReportStatus.Pending, null, null, cursor, 5));
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(12, seen.Count);
        Assert.Equal(12, seen.Distinct().Count());
        Assert.Equal(ids.OrderBy(x => x).ToList(), seen.OrderBy(x => x).ToList());
        var expected = (await _s.Mod(m => m.QueueAsync(ReportStatus.Pending, null, null, null, 50))).Items.Select(i => i.Id).ToList();
        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task Queue_rejects_a_malformed_cursor()
    {
        await Assert.ThrowsAsync<Vela.Api.Common.InvalidCursorException>(() => _s.Mod(m => m.QueueAsync(null, null, null, "not-a-cursor", 5)));
    }

    [Fact]
    public async Task Counts_per_status_and_the_oldest_pending_item()
    {
        await Seed("user_report", "spam", "pending", T(90));
        await Seed("user_report", "spam", "pending", T(5));
        await Seed("user_report", "spam", "reviewed", T(200));
        await Seed("user_report", "spam", "actioned", T(1));
        await Seed("user_report", "spam", "actioned", T(2));
        await Seed("user_report", "spam", "dismissed", T(3));

        var c = await _s.Mod(m => m.CountsAsync());
        Assert.Equal((2, 1, 2, 1), (c.Pending, c.Reviewed, c.Actioned, c.Dismissed));
        Assert.Equal(T(90), c.OldestPendingAt);
    }

    [Fact]
    public async Task Counts_have_no_oldest_when_nothing_is_pending()
    {
        await Seed("user_report", "spam", "dismissed", T(3));
        Assert.Null((await _s.Mod(m => m.CountsAsync())).OldestPendingAt);
    }

    [Fact]
    public async Task Reviewed_items_leave_the_pending_queue()
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work);
        Assert.Contains(report, (await _s.Mod(m => m.QueueAsync(ReportStatus.Pending, null, null, null, 50))).Items.Select(i => i.Id));
        await _s.Mod(m => m.DismissReportAsync(_admin, report));
        Assert.DoesNotContain(report, (await _s.Mod(m => m.QueueAsync(ReportStatus.Pending, null, null, null, 50))).Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Auto_flag_items_appear_in_the_same_queue()
    {
        var (_, work) = await _s.BlockedStory();
        var page = await _s.Mod(m => m.QueueAsync(ReportStatus.Pending, null, "auto_flag", null, 50));
        var item = Assert.Single(page.Items);
        Assert.Equal((work, "work", "auto_flag"), (item.TargetId, item.TargetType, item.Source));
    }

    [Fact]
    public async Task User_lookup_finds_by_handle_display_name_and_exact_id_and_escapes_wildcards()
    {
        var a = await _s.Host.SignInOnboarded("zeta_one", "Pia Quartz");
        var b = await _s.Host.SignInOnboarded("zeta_two", "Quincy Reed");
        var c = await _s.Host.SignInOnboarded("other_handle", "100% Plain");

        Assert.Equal([a.Id, b.Id], (await _s.Mod(m => m.FindUsersAsync("zeta", null))).Items.Select(u => u.UserId).OrderBy(x => x == a.Id ? 0 : 1));
        Assert.Equal([b.Id], (await _s.Mod(m => m.FindUsersAsync("quincy", null))).Items.Select(u => u.UserId));
        Assert.Equal([a.Id], (await _s.Mod(m => m.FindUsersAsync(a.Id.ToString(), null))).Items.Select(u => u.UserId));
        Assert.Equal([c.Id], (await _s.Mod(m => m.FindUsersAsync("100%", null))).Items.Select(u => u.UserId));
        Assert.Empty((await _s.Mod(m => m.FindUsersAsync("%", null))).Items.Where(u => u.UserId != c.Id));
        Assert.Empty((await _s.Mod(m => m.FindUsersAsync("zeta_", null))).Items.Where(u => u.UserId == c.Id));
        Assert.Empty((await _s.Mod(m => m.FindUsersAsync("", null))).Items);
    }

    [Fact]
    public async Task User_lookup_pages_without_duplicates_and_marks_suspended_accounts()
    {
        var made = new List<Guid>();
        for (var i = 0; i < 7; i++) made.Add((await _s.Host.SignInOnboarded($"page_{i}", $"Pager {i}")).Id);
        await _s.Mod(m => m.SuspendUserAsync(_admin, made[0], "x"));
        var seen = new List<AdminUserItem>();
        string? cursor = null;
        do
        {
            var page = await _s.Mod(m => m.FindUsersAsync("page_", cursor, 3));
            seen.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(made.OrderBy(x => x), seen.Select(u => u.UserId).OrderBy(x => x));
        Assert.Single(seen, u => u.IsSuspended);
    }

    [Fact]
    public async Task Audit_log_is_newest_first_and_paginates()
    {
        for (var i = 0; i < 5; i++)
        {
            var (_, work) = await _s.Story($"Story {i}");
            await _s.Mod(m => m.RemoveWorkAsync(_admin, work, $"reason {i}"));
            _s.Host.Clock.Advance(TimeSpan.FromMinutes(1));
        }
        var first = await _s.Mod(m => m.AuditAsync(null, 2));
        Assert.Equal(["reason 4", "reason 3"], first.Items.Select(a => a.Reason));
        var rest = await _s.Mod(m => m.AuditAsync(first.NextCursor, 10));
        Assert.Equal(["reason 2", "reason 1", "reason 0"], rest.Items.Select(a => a.Reason));
        Assert.Null(rest.NextCursor);
    }

    [Fact]
    public async Task Report_detail_shows_the_content_the_note_and_the_owners_history()
    {
        var (owner, work) = await _s.Story("Harbour <b>");
        var reporter = await _s.NewUser();
        var report = await _s.Report(reporter, "work", work, "harassment", "this is <script>bad</script>");
        var second = await _s.Report(await _s.NewUser(), "user", owner.Id);
        var (_, other) = await _s.Story("Second", owner);
        await _s.Mod(m => m.RemoveWorkAsync(_admin, other, "gone"));

        var d = await _s.Mod(m => m.GetReportAsync(report));

        Assert.NotNull(d);
        Assert.Equal("work", d!.Item.TargetType);
        Assert.Equal("Harbour <b>", d.WorkTitle);
        Assert.Equal("this is <script>bad</script>", d.Note);
        Assert.Equal(owner.Id, d.OwnerId);
        Assert.Equal(1, d.OwnerRemovals);
        Assert.True(d.OwnerReportCount >= 2);
        Assert.NotEmpty(d.Chapters);
        Assert.Null(await _s.Mod(m => m.GetReportAsync(Guid.NewGuid())));
        Assert.NotEqual(Guid.Empty, second);
    }
}
