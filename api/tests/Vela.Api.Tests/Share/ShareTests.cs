using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Share.ShareTestKit;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Share;

[Collection(PostgresCollection.Name)]
public class ShareTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg, Config);
    public void Dispose() => _host.Dispose();

    private Task<TestUser> Artist(string name = "Ada Lovelace") => _host.SignInOnboarded(ProfileTestHost.UniqueHandle(), name);

    [Fact]
    public async Task Share_page_has_og_title_description_and_image()
    {
        var owner = await Artist("Ada Lovelace");
        var id = await Published(_host, owner, "Quiet harbour", "Morning light on the water.");

        var p = await _host.Anonymous().Fetch($"/s/{id}");

        Assert.Equal(HttpStatusCode.OK, p.Status);
        Assert.Equal("text/html; charset=utf-8", p.Header("Content-Type"));
        Assert.StartsWith("<!doctype html>", p.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<title>Quiet harbour", p.Body);
        Assert.Contains("<meta name=\"description\" content=\"Morning light on the water.\">", p.Body);
        Assert.Contains("<meta property=\"og:type\" content=\"article\">", p.Body);
        Assert.Contains("<meta property=\"og:title\" content=\"Quiet harbour\">", p.Body);
        Assert.Contains("<meta property=\"og:description\" content=\"Morning light on the water.\">", p.Body);
        Assert.Contains($"<meta property=\"og:image\" content=\"https://cdn.test/works/{id}/feed.webp\">", p.Body);
        Assert.Contains("<meta property=\"og:image:width\" content=\"400\">", p.Body);
        Assert.Contains("<meta property=\"og:image:height\" content=\"300\">", p.Body);
        Assert.Contains($"<meta property=\"og:url\" content=\"https://brandy.example.test/s/{id}\">", p.Body);
        Assert.Contains("<meta property=\"og:site_name\" content=\"Brandy\">", p.Body);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\">", p.Body);
        Assert.Contains($"<link rel=\"canonical\" href=\"https://brandy.example.test/s/{id}\">", p.Body);
        Assert.Contains("<h1>Quiet harbour</h1>", p.Body);
        Assert.Contains("Ada Lovelace", p.Body);
        Assert.Contains($" src=\"https://cdn.test/works/{id}/feed.webp\"", p.Body);
        Assert.DoesNotContain("<script", p.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http-equiv", p.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase), p.Body);
    }

    [Fact]
    public async Task Share_page_has_a_single_view_story_link_to_the_viewer()
    {
        var id = await Published(_host, await Artist());

        var p = await _host.Anonymous().Fetch($"/s/{id}");

        var links = Regex.Matches(p.Body, "<a [^>]*>");
        Assert.Single(links);
        Assert.Contains($"href=\"https://viewer.example.test/s/{id}\"", links[0].Value);
        Assert.Contains(">View story</a>", p.Body);
    }

    [Fact]
    public async Task Share_page_description_falls_back_when_intro_is_empty()
    {
        var id = await Published(_host, await Artist("Grace Hopper"), "T", "");

        var p = await _host.Anonymous().Fetch($"/s/{id}");

        Assert.Contains("<meta name=\"description\" content=\"A photo story by Grace Hopper\">", p.Body);
    }

    [Fact]
    public async Task Share_page_description_is_cut_at_200_characters_without_splitting_a_surrogate_pair()
    {
        var intro = new string('a', 199) + "\U0001F600" + "tail";
        var id = await Published(_host, await Artist(), "T", intro);

        var p = await _host.Anonymous().Fetch($"/s/{id}");

        var m = Regex.Match(p.Body, "<meta name=\"description\" content=\"([^\"]*)\">");
        var content = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
        Assert.True(content.Length <= 200);
        Assert.False(char.IsHighSurrogate(content[^1]));
        Assert.Equal(new string('a', 199), content);
    }

    [Fact]
    public async Task Share_page_sends_security_and_cache_headers_with_a_fresh_nonce_matching_the_style_tag()
    {
        var id = await Published(_host, await Artist());

        var a = await _host.Anonymous().Fetch($"/s/{id}");
        var b = await _host.Anonymous().Fetch($"/s/{id}");

        string Nonce(Page p) => Regex.Match(p.Header("Content-Security-Policy"), "style-src 'nonce-([^']+)'").Groups[1].Value;
        Assert.NotEmpty(Nonce(a));
        Assert.NotEqual(Nonce(a), Nonce(b));
        Assert.Contains($"<style nonce=\"{Nonce(a)}\">", a.Body);
        Assert.Equal(1, Regex.Matches(a.Body, "<style").Count);
        Assert.Equal(
            $"default-src 'none'; img-src https:; style-src 'nonce-{Nonce(a)}'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
            a.Header("Content-Security-Policy"));
        Assert.Equal("nosniff", a.Header("X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", a.Header("Referrer-Policy"));
        Assert.Equal("DENY", a.Header("X-Frame-Options"));
        Assert.Equal("camera=(), microphone=(), geolocation=()", a.Header("Permissions-Policy"));
        Assert.Contains("public", a.Header("Cache-Control"));
        Assert.Contains("max-age=300", a.Header("Cache-Control"));
    }

    [Fact]
    public async Task Share_page_answers_head_without_a_body()
    {
        var id = await Published(_host, await Artist());

        var p = await _host.Anonymous().Fetch($"/s/{id}", HttpMethod.Head);

        Assert.Equal(HttpStatusCode.OK, p.Status);
        Assert.Equal("", p.Body);
        Assert.Contains("max-age=300", p.Header("Cache-Control"));
    }

    [Fact]
    public async Task Share_page_for_draft_pending_blocked_removed_report_hidden_unknown_or_malformed_is_an_identical_404()
    {
        var owner = await Artist();
        var ids = new List<string> { Guid.NewGuid().ToString(), "not-a-guid", "'\"><script>" };
        foreach (var status in new[] { WorkStatus.Draft, WorkStatus.Pending, WorkStatus.Blocked, WorkStatus.Removed })
        {
            var id = await Published(_host, owner);
            await Modify(_host, id, w => w.Status = status);
            ids.Add(id.ToString());
        }
        var hidden = await Published(_host, owner);
        await Modify(_host, hidden, w => w.ReportHidden = true);
        ids.Add(hidden.ToString());

        var pages = new List<Page>();
        foreach (var id in ids) pages.Add(await _host.Anonymous().Fetch($"/s/{Uri.EscapeDataString(id)}"));

        Assert.All(pages, p =>
        {
            Assert.Equal(HttpStatusCode.NotFound, p.Status);
            Assert.Equal("no-store", p.Header("Cache-Control"));
            Assert.Contains("Story not found", p.Body);
            Assert.Equal("nosniff", p.Header("X-Content-Type-Options"));
            Assert.Equal("text/html; charset=utf-8", p.Header("Content-Type"));
        });
        string Strip(Page p) => Regex.Replace(p.Body, "nonce-?=?\"?[A-Za-z0-9+/=]+\"?", "");
        Assert.Single(pages.Select(Strip).Distinct());
    }

    [Fact]
    public async Task Share_page_for_a_suspended_or_deleted_owner_is_404()
    {
        var suspended = await Artist();
        var deleted = await Artist();
        var s = await Published(_host, suspended);
        var d = await Published(_host, deleted);
        await using (var db = _host.Db())
        {
            var now = ProfileTestHost.Now;
            await db.Users.Where(u => u.Id == suspended.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SuspendedAt, now));
            await db.Users.Where(u => u.Id == deleted.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.DeletedAt, now));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _host.Anonymous().Fetch($"/s/{s}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.Anonymous().Fetch($"/s/{d}")).Status);
    }

    [Fact]
    public async Task Work_dto_share_url_points_at_the_share_route()
    {
        var owner = await Artist();
        var id = await Published(_host, owner);

        var r = await owner.Get($"/v1/works/{id}");

        Assert.Equal($"https://brandy.example.test/s/{id}", r.Data.GetProperty("shareUrl").GetString());
    }
}
