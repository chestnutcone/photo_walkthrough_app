using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

public sealed class CountingReporter : INcmecReporter
{
    public int Calls;
    public bool Fail;
    public Task<string> SubmitAsync(EvidenceHold hold, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return Fail ? throw new IOException("boom") : Task.FromResult("REAL-" + hold.Id.ToString("N")[..8]);
    }
}

[Collection(PostgresCollection.Name)]
public class NcmecTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Browser _b = null!;
    private Guid _admin;
    private readonly CountingReporter _counter = new();
    public async Task InitializeAsync()
    {
        _s = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<INcmecReporter>(_counter));
        _admin = await _s.NewAdminUser();
        _b = await _s.AdminBrowser();
    }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private async Task<Guid> Hold()
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        return (await _s.MarkIllegal(_admin, report)).HoldId!.Value;
    }

    private Task<Shared> Submit(Guid hold, bool confirm = true, string reason = "reviewed, reporting", bool token = true)
    {
        var form = new Dictionary<string, string> { ["reason"] = reason };
        if (confirm) form["confirm"] = "on";
        return Post(hold, form, token);
    }

    private async Task<Shared> Post(Guid hold, Dictionary<string, string> form, bool token) =>
        new(await _b.Post($"/admin/evidence/{hold}?handler=SubmitNcmec", form, withToken: token, tokenFrom: $"/admin/evidence/{hold}"));

    public sealed record Shared(Vela.Api.Tests.Share.Page Page);

    [Fact]
    public async Task An_explicit_admin_post_records_the_report_id_extends_the_hold_and_audits()
    {
        var id = await Hold();
        _s.Host.Clock.Advance(TimeSpan.FromDays(100));
        _b = await _s.AdminBrowser(); // the 8 hour session ended with the clock jump

        var p = (await Submit(id)).Page;

        Assert.Equal(HttpStatusCode.Redirect, p.Status);
        var hold = await _s.Hold(id);
        Assert.Equal("REAL-" + id.ToString("N")[..8], hold.NcmecReportId);
        Assert.Equal(ProfileTestHost.Now.AddDays(100), hold.NcmecSubmittedAt);
        Assert.Equal(ProfileTestHost.Now.AddDays(465), hold.HoldUntil);
        Assert.Equal(1, _counter.Calls);
        var row = Assert.Single(await _s.Audit(), a => a.Action == "ncmec_submit");
        Assert.Equal(("evidence", id, "reviewed, reporting"), (row.TargetType, row.TargetId, row.Reason));
    }

    [Fact]
    public async Task A_second_submission_for_the_same_hold_is_refused_with_409_and_never_reaches_the_reporter()
    {
        var id = await Hold();
        await Submit(id);

        var again = (await Submit(id)).Page;

        Assert.Equal(HttpStatusCode.Conflict, again.Status);
        Assert.Contains("already been submitted", again.Body);
        Assert.Equal(1, _counter.Calls);
        Assert.Single(await _s.Audit(), a => a.Action == "ncmec_submit");
    }

    [Fact]
    public async Task Without_the_confirmation_a_reason_or_a_token_nothing_is_submitted()
    {
        var id = await Hold();

        Assert.Contains("flash=confirmation_required", (await Submit(id, confirm: false)).Page.Response.Headers.Location!.OriginalString);
        Assert.Contains("flash=reason_required", (await Submit(id, reason: "  ")).Page.Response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(id, token: false)).Page.Status);

        Assert.Equal(0, _counter.Calls);
        Assert.Null((await _s.Hold(id)).NcmecReportId);
    }

    [Fact]
    public async Task A_failing_reporter_changes_nothing_and_the_admin_can_retry()
    {
        var id = await Hold();
        _counter.Fail = true;
        Assert.Contains("flash=ncmec_failed", (await Submit(id)).Page.Response.Headers.Location!.OriginalString);
        Assert.Null((await _s.Hold(id)).NcmecReportId);

        _counter.Fail = false;
        await Submit(id);
        Assert.NotNull((await _s.Hold(id)).NcmecReportId);
    }

    [Fact]
    public async Task Anonymous_visitors_and_a_missing_hold_cannot_submit()
    {
        var id = await Hold();
        var anon = _s.NewBrowser();
        var p = await anon.Post($"/admin/evidence/{id}?handler=SubmitNcmec", new Dictionary<string, string> { ["reason"] = "x", ["confirm"] = "on" }, withToken: false);
        Assert.Equal(HttpStatusCode.Redirect, p.Status);
        Assert.Equal("/admin/login", p.Response.Headers.Location?.OriginalString);
        Assert.Equal(0, _counter.Calls);
        Assert.Equal(HttpStatusCode.NotFound, (await _b.Get($"/admin/evidence/{Guid.NewGuid()}")).Status);
    }

    [Fact]
    public async Task The_unconfigured_reporter_shows_the_legal_advisor_message_and_changes_nothing()
    {
        using var site = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<INcmecReporter>(new UnconfiguredNcmecReporter()));
        var admin = await site.NewAdminUser();
        var b = await site.AdminBrowser();
        var (_, work) = await site.Story();
        var id = (await site.MarkIllegal(admin, await site.Report(await site.NewUser(), "work", work, "illegal_content"))).HoldId!.Value;

        var post = await b.Post($"/admin/evidence/{id}?handler=SubmitNcmec", new Dictionary<string, string> { ["reason"] = "go", ["confirm"] = "on" }, tokenFrom: $"/admin/evidence/{id}");
        var page = await b.Get(post.Response.Headers.Location!.OriginalString);

        Assert.Contains("NCMEC reporting is not configured: contact your legal advisor", page.Body);
        Assert.Null((await site.Hold(id)).NcmecReportId);
        await Assert.ThrowsAsync<NcmecNotConfiguredException>(() => new UnconfiguredNcmecReporter().SubmitAsync(new EvidenceHold(), default));
    }

    [Theory]
    [InlineData("Development", typeof(FakeNcmecReporter))]
    [InlineData("Test", typeof(FakeNcmecReporter))]
    [InlineData("Production", typeof(UnconfiguredNcmecReporter))]
    [InlineData("Staging", typeof(UnconfiguredNcmecReporter))]
    public void The_reporter_depends_on_the_environment(string env, Type expected)
    {
        var reporter = AdminEndpoints.NcmecReporterFor(new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = env });
        Assert.IsType(expected, reporter);
    }

    [Fact]
    public async Task The_fake_reporter_returns_a_deterministic_id_per_hold()
    {
        var hold = new EvidenceHold { Id = Guid.NewGuid() };
        var a = await new FakeNcmecReporter().SubmitAsync(hold, default);
        var b = await new FakeNcmecReporter().SubmitAsync(hold, default);
        var c = await new FakeNcmecReporter().SubmitAsync(new EvidenceHold { Id = Guid.NewGuid() }, default);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith("FAKE-", a);
    }

    [Fact]
    public void Nothing_but_the_admin_hold_page_ever_calls_the_ncmec_reporter()
    {
        var src = SourceRoot();
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(src, "*.cshtml", SearchOption.AllDirectories))
            .Where(f => !f.Contains("/obj/") && !f.Contains("/Migrations/")).ToList();
        Assert.NotEmpty(files);

        // The only call of the reporter itself is in the submission service...
        var reporterCalls = files.Where(f => Regex.IsMatch(File.ReadAllText(f), @"[Rr]eporter\.SubmitAsync\(")).Select(Path.GetFileName).ToList();
        Assert.Equal(["EvidenceService.Ncmec.cs"], reporterCalls);
        // ...and the only caller of that service method is the admin action on the hold detail page.
        var callers = files.Where(f => Regex.IsMatch(File.ReadAllText(f), @"\.SubmitToNcmecAsync\(")).Select(Path.GetFileName).ToList();
        Assert.Equal(["EvidenceDetail.cshtml.cs"], callers);
        // No other mention of the interface than registration and the service.
        var users = files.Where(f => File.ReadAllText(f).Contains("INcmecReporter")).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Assert.Equal(["AdminEndpoints.cs", "EvidenceService.cs", "INcmecReporter.cs"], users.Where(u => u is not ("FakeNcmecReporter.cs" or "UnconfiguredNcmecReporter.cs")).ToList());
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Vela.Api"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Vela.Api");
    }
}
