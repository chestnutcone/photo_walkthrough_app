using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vela.Api.Tests.Share;

public class ShareConfigTests
{
    private static WebApplicationFactory<Program> Production(params (string Key, string Value)[] extra) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Abuse:RequireAttestation", "false"); // attestation start-up rules are covered in AttestationEndpointTests
            b.UseSetting("ConnectionStrings:Default", "Host=localhost;Database=x;Username=x;Password=x");
            b.UseSetting("Auth:Jwt:SigningKey", "integration-test-signing-key-0123456789-abc");
            b.UseSetting("Auth:EmailCode:Pepper", "integration-test-pepper-0123456789-abcdef");
            b.UseSetting("Email:Acs:ConnectionString", "endpoint=https://x.communication.azure.com/;accesskey=abc");
            b.UseSetting("Email:SenderAddress", "noreply@example.test");
            b.UseSetting("Storage:ConnectionString", "UseDevelopmentStorage=true");
            b.UseSetting("Storage:PublicBaseUrl", "https://cdn.example.test/");
            b.UseSetting("ContentSafety:Endpoint", "https://cs.example.test/");
            b.UseSetting("ContentSafety:Key", "k");
            b.UseSetting("Branding:Domain", "brandy.example.test");
            b.UseSetting("Admin:Emails", "admin@example.test");
            b.UseSetting("Admin:BanPepper", "integration-test-ban-pepper-0123456789-abcdef");
            foreach (var (k, v) in extra) b.UseSetting(k, v);
        });

    [Theory]
    [InlineData(null)]
    [InlineData("http://viewer.example.test")]
    [InlineData("viewer.example.test")]
    [InlineData("javascript:alert(1)")]
    public void Startup_outside_development_needs_an_absolute_https_viewer_url(string? value)
    {
        using var f = value is null ? Production() : Production(("Web:ViewerUrl", value));
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("Web:ViewerUrl", ex.Message);
    }

    [Theory]
    [InlineData("https://brandy.example.test/path")]
    [InlineData("http://brandy.example.test")]
    [InlineData("brandy example")]
    [InlineData("")]
    public void Startup_outside_development_needs_a_bare_branding_domain(string value)
    {
        using var f = Production(("Web:ViewerUrl", "https://viewer.example.test"), ("Branding:Domain", value));
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("Branding:Domain", ex.Message);
    }

    [Theory]
    [InlineData("https://brandy.example.test")]
    [InlineData("https://BRANDY.example.test/base")]
    [InlineData("https://brandy.example.test:8443")]
    public void Startup_outside_development_rejects_a_viewer_on_the_share_page_host(string viewer)
    {
        using var f = Production(("Web:ViewerUrl", viewer));
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("Web:ViewerUrl", ex.Message);
        Assert.Contains("Branding:Domain", ex.Message);
    }

    [Fact]
    public void Startup_accepts_a_viewer_on_another_host()
    {
        using var f = Production(("Web:ViewerUrl", "https://view.brandy.example.test"));
        using var client = f.CreateClient();
    }
}
