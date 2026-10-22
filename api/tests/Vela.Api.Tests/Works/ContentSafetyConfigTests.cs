using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Moderation;

namespace Vela.Api.Tests;

public class ContentSafetyConfigTests
{
    private static WebApplicationFactory<Program> Host(string env, params (string Key, string Value)[] extra) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(env);
            b.UseSetting("ConnectionStrings:Default", "Host=localhost;Database=x;Username=x;Password=x");
            b.UseSetting("Auth:Jwt:SigningKey", "integration-test-signing-key-0123456789-abc");
            b.UseSetting("Auth:EmailCode:Pepper", "integration-test-pepper-0123456789-abcdef");
            b.UseSetting("Email:Acs:ConnectionString", "endpoint=https://x.communication.azure.com/;accesskey=abc");
            b.UseSetting("Email:SenderAddress", "noreply@example.test");
            b.UseSetting("Storage:ConnectionString", "UseDevelopmentStorage=true");
            b.UseSetting("Storage:PublicBaseUrl", "https://cdn.example.test/");
            b.UseSetting("Web:ViewerUrl", "https://viewer.example.test");
            foreach (var (k, v) in extra) b.UseSetting(k, v);
        });

    [Fact]
    public void Production_without_endpoint_fails_naming_the_setting()
    {
        using var f = Host("Production", ("ContentSafety:Key", "k"));
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("ContentSafety:Endpoint", ex.Message);
    }

    [Fact]
    public void Production_without_key_fails_naming_the_setting()
    {
        using var f = Host("Production", ("ContentSafety:Endpoint", "https://safety.example.test/"));
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("ContentSafety:Key", ex.Message);
    }

    [Fact]
    public void Production_uses_azure_and_test_uses_the_fake()
    {
        using var prod = Host("Production", ("ContentSafety:Endpoint", "https://safety.example.test/"), ("ContentSafety:Key", "k"));
        using var test = Host("Test");

        Assert.IsType<AzureContentSafety>(prod.Services.GetRequiredService<IContentSafety>());
        Assert.IsType<FakeContentSafety>(test.Services.GetRequiredService<IContentSafety>());
        Assert.Null(prod.Services.GetService<FakeContentSafety>());
    }
}
