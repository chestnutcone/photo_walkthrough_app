using System.Net;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Admin.ModerationKit;
using static Vela.Api.Tests.Legal.DmcaFlowKit;

namespace Vela.Api.Tests.Legal;

[Collection(PostgresCollection.Name)]
public class CounterNoticeTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private HttpClient _http = null!;
    private Guid _admin;
    public async Task InitializeAsync()
    {
        _s = await AdminSite.Create(pg, isolated: true);
        _http = _s.Host.Anonymous();
        _admin = await _s.NewAdminUser();
    }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private async Task<(Vela.Api.Tests.Profiles.TestUser Owner, Guid Work, Guid Notice)> Removed()
    {
        var x = await _s.NoticedStory(_http);
        Assert.Equal(ModerationOutcome.Ok, await _s.Dm(d => d.TakeDownAsync(_admin, x.Notice)));
        return x;
    }

    [Fact]
    public async Task The_owner_of_a_removed_story_with_a_takedown_notice_can_send_a_counter_notice()
    {
        var (owner, work, notice) = await Removed();

        var r = await owner.Post($"/v1/works/{work}/counter-notice", Counter(statement: "  mine\nall mine <b>x</b>  "));

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(notice, r.Data.GetProperty("noticeId").GetGuid());
        var n = await _s.Notice(notice);
        Assert.Equal(DmcaStatus.CounterNotice, n.Status);
        Assert.Equal(("Olive Owner", "9 Side Road\nShelbyville", "555-0199", "  mine\nall mine <b>x</b>  "), (n.CounterName, n.CounterAddress, n.CounterPhone, n.CounterStatement));
        Assert.Equal(_s.Host.Clock.GetUtcNow(), n.CounterAt);
    }

    [Fact]
    public async Task Every_other_case_gets_the_identical_404()
    {
        var (owner, work, _) = await Removed();
        var stranger = await _s.NewUser();
        var (_, published, _) = await _s.NoticedStory(_http); // pending notice, work still published
        var (adminOwner, adminRemoved) = await _s.Story();
        await _s.Mod(m => m.RemoveWorkAsync(_admin, adminRemoved, "policy")); // removed, but not by a DMCA takedown

        var cases = new List<Vela.Api.Tests.Profiles.Reply>
        {
            await stranger.Post($"/v1/works/{work}/counter-notice", Counter()),
            await owner.Post($"/v1/works/{Guid.NewGuid()}/counter-notice", Counter()),
            await owner.Post("/v1/works/not-a-guid/counter-notice", Counter()),
            await adminOwner.Post($"/v1/works/{adminRemoved}/counter-notice", Counter()),
            await (await _s.NewUser()).Post($"/v1/works/{published}/counter-notice", Counter()),
        };
        var ownerOfPublished = await _s.LoadWorkOwner(published);
        Assert.NotEqual(owner.Id, ownerOfPublished);

        foreach (var c in cases)
        {
            Assert.Equal(HttpStatusCode.NotFound, c.Status);
            Assert.Equal("work_not_found", c.ErrorCode);
            Assert.Equal(cases[0].Body.ToString(), c.Body.ToString());
        }
        Assert.Equal(DmcaStatus.Takedown, (await _s.Notices()).First(n => n.WorkId == work).Status);
    }

    [Fact]
    public async Task The_owner_of_a_story_that_is_still_published_gets_the_same_404()
    {
        var (owner, work, _) = await _s.NoticedStory(_http);
        var r = await owner.Post($"/v1/works/{work}/counter-notice", Counter());
        Assert.Equal((HttpStatusCode.NotFound, "work_not_found"), (r.Status, r.ErrorCode));
    }

    [Fact]
    public async Task It_needs_a_signed_in_user_and_only_one_counter_notice_per_story_is_taken()
    {
        var (owner, work, _) = await Removed();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.PostAsync($"/v1/works/{work}/counter-notice", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await owner.Post($"/v1/works/{work}/counter-notice", Counter())).Status);
        var again = await owner.Post($"/v1/works/{work}/counter-notice", Counter(name: "Second"));

        Assert.Equal((HttpStatusCode.Conflict, "counter_notice_exists"), (again.Status, again.ErrorCode));
        Assert.Equal("Olive Owner", (await _s.Notices()).First(n => n.WorkId == work).CounterName);
    }

    public static IEnumerable<object[]> Boundaries()
    {
        yield return ["name", new string('n', 120), new string('n', 121)];
        yield return ["name", "n", ""];
        yield return ["address", new string('a', 300), new string('a', 301)];
        yield return ["address", "a", " "];
        yield return ["phone", new string('1', 40), new string('1', 41)];
        yield return ["statement", new string('s', 2000), new string('s', 2001)];
        yield return ["statement", "s", ""];
    }

    private static Dictionary<string, object?> Fields() => new()
    {
        ["name"] = "Olive", ["address"] = "Addr", ["phone"] = null, ["statement"] = "St", ["consentToJurisdiction"] = true, ["goodFaithBelief"] = true,
    };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task Field_lengths_are_enforced_at_the_boundary(string field, string good, string bad)
    {
        var (owner, work, notice) = await Removed();
        var badBody = Fields(); badBody[field] = bad;
        var r = await owner.Post($"/v1/works/{work}/counter-notice", badBody);

        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, r.ErrorCode));
        Assert.StartsWith(field + " ", r.Body.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(DmcaStatus.Takedown, (await _s.Notice(notice)).Status);

        var goodBody = Fields(); goodBody[field] = good;
        Assert.Equal(HttpStatusCode.OK, (await owner.Post($"/v1/works/{work}/counter-notice", goodBody)).Status);
    }

    [Theory]
    [InlineData("consentToJurisdiction", false)]
    [InlineData("goodFaithBelief", false)]
    [InlineData("goodFaithBelief", null)]
    [InlineData("name", "bad\u0001name")]
    [InlineData("statement", "bad\rstatement")]
    [InlineData("address", "bad\u0000")]
    [InlineData("name", null)]
    public async Task Unaccepted_statements_and_control_characters_are_refused_naming_the_field(string field, object? value)
    {
        var (owner, work, notice) = await Removed();
        var body = Fields(); body[field] = value;

        var r = await owner.Post($"/v1/works/{work}/counter-notice", body);

        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, r.ErrorCode));
        Assert.StartsWith(field + " ", r.Body.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(DmcaStatus.Takedown, (await _s.Notice(notice)).Status);
    }
}
