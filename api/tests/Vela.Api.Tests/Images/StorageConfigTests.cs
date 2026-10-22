using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vela.Api.Tests.Images;

public class StorageConfigTests
{
    private static WebApplicationFactory<Program> Production(params (string Key, string Value)[] extra) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("ConnectionStrings:Default", "Host=localhost;Database=x;Username=x;Password=x");
            b.UseSetting("Auth:Jwt:SigningKey", "integration-test-signing-key-0123456789-abc");
            b.UseSetting("Auth:EmailCode:Pepper", "integration-test-pepper-0123456789-abcdef");
            b.UseSetting("Email:Acs:ConnectionString", "endpoint=https://x.communication.azure.com/;accesskey=abc");
            b.UseSetting("Email:SenderAddress", "noreply@example.test");
            foreach (var (k, v) in extra) b.UseSetting(k, v);
        });

    [Fact]
    public void Startup_outside_development_without_storage_connection_string_fails_naming_the_setting()
    {
        using var f = Production(("Storage:PublicBaseUrl", "https://cdn.example.test/"));
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("Storage:ConnectionString", ex.Message);
    }

    [Fact]
    public void Startup_outside_development_without_public_base_url_fails_naming_the_setting()
    {
        using var f = Production(("Storage:ConnectionString", "UseDevelopmentStorage=true"));
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("Storage:PublicBaseUrl", ex.Message);
    }
}
