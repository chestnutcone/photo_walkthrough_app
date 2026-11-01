using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Share.ShareTestKit;

namespace Vela.Api.Tests.Share;

/// <summary>Review Focus 3: hostile text is escaped in text and attribute contexts, including meta tags.</summary>
[Collection(PostgresCollection.Name)]
public class ShareEscapingTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg, Config);
    public void Dispose() => _host.Dispose();

    public static TheoryData<string> Hostile() => new()
    {
        "<script>alert(1)</script>",
        "\"><img src=x onerror=alert(1)>",
        "' onmouseover='x",
        "line\u2028sep\u2029para\u0085next\r\nnl",
        "<svg/onload=alert(1)>&amp;&lt;",
        new string('<', 5000) + new string('"', 5000),
        "\u202Eevil rtl <b>bold</b>",
    };

    [Theory]
    [MemberData(nameof(Hostile))]
    public async Task Share_page_html_escapes_script_in_title_intro_and_artist_name(string hostile)
    {
        var owner = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle(), "Placeholder");
        await SetDisplayName(_host, owner.Id, hostile);
        var id = await Published(_host, owner, hostile, hostile);

        var p = await _host.Anonymous().Fetch($"/s/{id}");

        Assert.Equal(System.Net.HttpStatusCode.OK, p.Status);
        // No hostile markup survives: every angle bracket in the body belongs to the page's own fixed markup.
        Assert.DoesNotContain("<script", p.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", p.Body.Replace("<img class=\"cover\" src=\"https://cdn.test/", ""), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", p.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>", p.Body);
        Assert.DoesNotContain("\"><", p.Body);
        Assert.DoesNotContain("' onmouseover='", p.Body);
        foreach (var c in new[] { '\u2028', '\u2029', '\u0085', '\r' }) Assert.DoesNotContain(c, p.Body);
        // Inside every attribute value there is no raw quote or angle bracket.
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(p.Body, "<meta [^>]*>"))
            Assert.Matches("^<meta( [a-z:]+=\"[^\"<>]*\")+>$", m.Value);
    }
}
