using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Legal;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Admin.ModerationKit;
using static Vela.Api.Tests.Legal.DmcaKit;

namespace Vela.Api.Tests.Legal;

[Collection(PostgresCollection.Name)]
public class DmcaNoticeTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private HttpClient _http = null!;
    private readonly MutableIp _ip = new("203.0.113.7");
    public async Task InitializeAsync()
    {
        _s = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<IClientIpResolver>(_ip));
        _http = _s.Host.Anonymous();
    }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private static Dictionary<string, object?> With(Dictionary<string, object?> b, string key, object? value) { b[key] = value; return b; }

    [Fact]
    public async Task A_notice_for_a_story_link_is_stored_verbatim_with_the_owner_and_reaches_the_queue()
    {
        var (owner, work) = await _s.Story("Harbour");
        var body = Valid($"see https://app.example/s/{work}?utm=x");
        body["originalWorkDescription"] = "  line one\nline two <b>bold</b> éè \U0001F600  ";

        var r = await _http.PostNotice(body);

        Assert.Equal(HttpStatusCode.Accepted, r.Status);
        Assert.True(r.Json.GetProperty("success").GetBoolean());
        var n = await _s.Notice(r.NoticeId);
        Assert.Equal((work, owner.Id, DmcaStatus.Pending), (n.WorkId, n.OwnerUserId, n.Status));
        Assert.Equal("  line one\nline two <b>bold</b> éè \U0001F600  ", n.OriginalWorkDescription);
        Assert.Equal("1 Main Street\nSpringfield", n.ClaimantAddress);
        Assert.Equal(("Carla Claimant", "carla@claims.test", "+1 555 0100"), (n.ClaimantName, n.ClaimantEmail, n.ClaimantPhone));
        Assert.Equal($"see https://app.example/s/{work}?utm=x", n.WorkReference);
        Assert.True(n.GoodFaithStatement && n.PerjuryStatement);
        Assert.Equal(_s.Host.Clock.GetUtcNow(), n.CreatedAt);
        Assert.Equal("203.0.113.7", n.ClientIp);
        var report = Assert.Single(await _s.DmcaReports());
        Assert.Equal(("copyright", "work", work, "pending", null), (report.Reason, report.TargetType, report.TargetId, report.Status, report.ReporterId));
    }

    [Fact]
    public async Task A_bare_guid_matches_too_and_a_second_open_notice_adds_no_second_queue_item()
    {
        var (_, work) = await _s.Story();

        await _http.PostNotice(Valid(work.ToString()));
        await _http.PostNotice(Valid($"  {work.ToString().ToUpperInvariant()}  "));

        Assert.Equal(2, (await _s.Notices()).Count(n => n.WorkId == work));
        Assert.Single(await _s.DmcaReports());
    }

    [Fact]
    public async Task Once_the_queue_item_is_closed_a_new_notice_opens_a_new_one()
    {
        var (_, work) = await _s.Story();
        await _http.PostNotice(Valid(work.ToString()));
        var admin = await _s.NewAdminUser();
        Assert.Equal(Vela.Api.Admin.ModerationOutcome.Ok, await _s.Mod(m => m.DismissReportAsync(admin, _s.DmcaReports().Result.Single().Id)));

        await _http.PostNotice(Valid(work.ToString()));

        Assert.Equal(2, (await _s.DmcaReports()).Count);
    }

    [Theory]
    [InlineData("not a link at all")]
    [InlineData("https://app.example/s/not-a-guid")]
    [InlineData("00000000-0000-0000-0000-0000000000aa")]
    [InlineData("https://app.example/x/00000000-0000-0000-0000-0000000000aa")]
    public async Task An_unmatched_reference_is_accepted_stored_without_a_work_and_not_queued(string reference)
    {
        var r = await _http.PostNotice(Valid(reference));

        Assert.Equal(HttpStatusCode.Accepted, r.Status);
        var n = await _s.Notice(r.NoticeId);
        Assert.Null(n.WorkId);
        Assert.Null(n.OwnerUserId);
        Assert.Equal(reference, n.WorkReference);
        Assert.Empty(await _s.DmcaReports());
    }

    [Fact]
    public async Task A_draft_is_treated_as_unmatched()
    {
        var u = await _s.NewUser();
        var draft = await Works.WorkTestKit.ReadyDraft(_s.Host, u);

        var r = await _http.PostNotice(Valid(draft.ToString()));

        Assert.Equal(HttpStatusCode.Accepted, r.Status);
        Assert.Null((await _s.Notice(r.NoticeId)).WorkId);
    }

    [Fact]
    public async Task Matched_and_unmatched_responses_are_byte_identical_apart_from_the_notice_id()
    {
        var (_, work) = await _s.Story();
        var matched = await _http.PostNotice(Valid(work.ToString()));
        var unmatched = await _http.PostNotice(Valid(Guid.NewGuid().ToString()));

        string Normalise(Raw r) => Regex.Replace(r.Text, "[0-9a-f]{8}-[0-9a-f-]{27}", "ID");
        Assert.Equal(Normalise(matched), Normalise(unmatched));
        Assert.Equal(matched.Status, unmatched.Status);
        Assert.Equal(matched.Response.Content.Headers.ContentLength, unmatched.Response.Content.Headers.ContentLength);
        Assert.Equal(
            matched.Response.Headers.Select(h => h.Key).Order(), unmatched.Response.Headers.Select(h => h.Key).Order());
    }

    public static IEnumerable<object[]> Boundaries()
    {
        yield return ["claimantName", new string('n', 120), new string('n', 121)];
        yield return ["claimantName", "N", ""];
        yield return ["claimantName", "N", "   "];
        yield return ["claimantAddress", new string('a', 300), new string('a', 301)];
        yield return ["claimantAddress", "A", ""];
        yield return ["claimantPhone", new string('1', 40), new string('1', 41)];
        yield return ["workReference", new string('r', 500), new string('r', 501)];
        yield return ["workReference", "r", ""];
        yield return ["originalWorkDescription", new string('d', 2000), new string('d', 2001)];
        yield return ["originalWorkDescription", "d", ""];
        yield return ["signatureName", new string('s', 120), new string('s', 121)];
        yield return ["signatureName", "s", ""];
        yield return ["claimantEmail", new string('e', 64) + "@" + new string('d', 60) + "." + new string('d', 60) + "." + new string('d', 60) + ".com", new string('e', 64) + "@" + new string('d', 63) + "." + new string('d', 63) + "." + new string('d', 63) + ".com"];
        yield return ["claimantEmail", "a@b.co", "not an email"];
        yield return ["claimantEmail", "a@b.co", "a@b"];
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task Field_lengths_are_enforced_at_the_boundary_and_the_error_names_only_the_field(string field, string good, string bad)
    {
        var ok = await _http.PostNotice(With(Valid(), field, good));
        Assert.Equal(HttpStatusCode.Accepted, ok.Status);
        var before = (await _s.Notices()).Count;

        var r = await _http.PostNotice(With(Valid(), field, bad));

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("validation_failed", r.ErrorCode);
        Assert.StartsWith(field + " ", r.ErrorMessage);
        if (bad.Length > 2) Assert.DoesNotContain(bad, r.Text);
        Assert.Equal(before, (await _s.Notices()).Count);
    }

    [Theory]
    [InlineData("goodFaithStatement", false)]
    [InlineData("perjuryStatement", false)]
    [InlineData("goodFaithStatement", "true")]
    [InlineData("perjuryStatement", null)]
    [InlineData("claimantName", 5)]
    [InlineData("claimantEmail", null)]
    [InlineData("claimantAddress", null)]
    [InlineData("workReference", null)]
    [InlineData("originalWorkDescription", 7)]
    [InlineData("signatureName", null)]
    [InlineData("claimantPhone", 7)]
    public async Task Missing_wrong_typed_or_unaccepted_fields_are_refused_naming_the_field(string field, object? value)
    {
        var r = await _http.PostNotice(With(Valid(), field, value));

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("validation_failed", r.ErrorCode);
        Assert.StartsWith(field + " ", r.ErrorMessage);
        Assert.Empty(await _s.Notices());
    }

    [Fact]
    public async Task Required_fields_may_be_absent_but_the_phone_is_optional()
    {
        foreach (var field in new[] { "claimantName", "claimantEmail", "claimantAddress", "workReference", "originalWorkDescription", "signatureName", "goodFaithStatement", "perjuryStatement" })
        {
            var body = Valid();
            body.Remove(field);
            var r = await _http.PostNotice(body);
            Assert.Equal(HttpStatusCode.BadRequest, r.Status);
            Assert.StartsWith(field + " ", r.ErrorMessage);
        }
        var noPhone = Valid();
        noPhone.Remove("claimantPhone");
        Assert.Equal(HttpStatusCode.Accepted, (await _http.PostNotice(noPhone)).Status);
        Assert.Equal(HttpStatusCode.Accepted, (await _http.PostNotice(With(Valid(), "claimantPhone", null))).Status);
    }

    [Theory]
    [InlineData("claimantName", "Eve\r\nBcc: x@y.test")]
    [InlineData("claimantName", "Eve\nSmith")]
    [InlineData("claimantAddress", "a\rb")]
    [InlineData("claimantAddress", "a\u0000b")]
    [InlineData("originalWorkDescription", "x\u0007y")]
    [InlineData("originalWorkDescription", "x\ty")]
    [InlineData("signatureName", "sig\u001b")]
    [InlineData("workReference", "a\nb")]
    [InlineData("claimantPhone", "1\n2")]
    public async Task Control_characters_other_than_newline_in_text_fields_are_refused(string field, string value)
    {
        var r = await _http.PostNotice(With(Valid(), field, value));
        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.StartsWith(field + " ", r.ErrorMessage);
        Assert.Empty(await _s.Notices());
    }

    [Fact]
    public async Task Bodies_that_are_not_json_objects_are_refused_and_oversized_bodies_get_413()
    {
        Assert.Equal("validation_failed", (await _http.PostRaw("{not json")).ErrorCode);
        Assert.Equal("validation_failed", (await _http.PostRaw("[]")).ErrorCode);
        Assert.Equal("validation_failed", (await _http.PostRaw("")).ErrorCode);

        var big = Valid();
        big["padding"] = new string('x', 21 * 1024);
        var r = await _http.PostNotice(big);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.Status);
        Assert.Equal("payload_too_large", r.ErrorCode);
        Assert.Empty(await _s.Notices());

        var small = Valid();
        small["originalWorkDescription"] = new string('d', 2000);
        small["claimantAddress"] = new string('a', 300);
        Assert.Equal(HttpStatusCode.Accepted, (await _http.PostNotice(small)).Status);
    }

    [Fact]
    public async Task The_sixth_notice_from_one_address_in_24_hours_is_rate_limited_with_retry_after_and_the_window_rolls()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await _http.PostNotice(Valid())).Status);
            _s.Host.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var limited = await _http.PostNotice(Valid());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.Equal("rate_limited", limited.ErrorCode);
        var wait = int.Parse(limited.Response.Headers.GetValues("Retry-After").Single());
        Assert.InRange(wait, (24 * 60 - 5) * 60 - 1, 24 * 60 * 60);
        Assert.Equal(5, (await _s.Notices()).Count);

        _ip.Ip = "203.0.113.8";
        Assert.Equal(HttpStatusCode.Accepted, (await _http.PostNotice(Valid())).Status);
        _ip.Ip = "203.0.113.7";

        _s.Host.Clock.Advance(TimeSpan.FromSeconds(wait - 1));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _http.PostNotice(Valid())).Status);
        _s.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Accepted, (await _http.PostNotice(Valid())).Status);
    }

    [Fact]
    public async Task Refused_requests_do_not_use_up_the_allowance()
    {
        for (var i = 0; i < 8; i++) await _http.PostNotice(With(Valid(), "goodFaithStatement", false));
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Accepted, (await _http.PostNotice(Valid())).Status);
    }

    [Fact]
    public async Task The_claimant_gets_a_plain_acknowledgement_with_a_fixed_subject_and_only_the_reference()
    {
        var body = Valid(Guid.NewGuid().ToString());
        body["claimantName"] = "Mallory <script>alert(1)</script>";
        body["originalWorkDescription"] = "SECRET-DESCRIPTION";
        body["claimantAddress"] = "SECRET-ADDRESS";

        var r = await _http.PostNotice(body);

        var mail = Assert.Single(_s.Service<FakeEmailSender>().Sent, m => m.To == "carla@claims.test");
        Assert.Equal("DMCA notice received", mail.Subject);
        Assert.Contains(r.NoticeId.ToString(), mail.TextBody);
        foreach (var leak in new[] { "Mallory", "script", "SECRET-DESCRIPTION", "SECRET-ADDRESS", "Carla", "<" })
            Assert.DoesNotContain(leak, mail.TextBody);
    }

    [Fact]
    public async Task A_failing_email_does_not_fail_the_request()
    {
        using var site = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<IEmailSender>(new ThrowingEmailSender()));
        var r = await site.Host.Anonymous().PostNotice(Valid());

        Assert.Equal(HttpStatusCode.Accepted, r.Status);
        Assert.Single(await site.Notices());
    }

    [Fact]
    public async Task The_endpoint_needs_no_sign_in()
    {
        var r = await _s.Host.Anonymous().PostNotice(Valid());
        Assert.Equal(HttpStatusCode.Accepted, r.Status);
    }
}
