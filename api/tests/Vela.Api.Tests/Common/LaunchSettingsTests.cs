using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Vela.Api.Common;

namespace Vela.Api.Tests.Common;

/// <summary>FINALAPI M9: Production refuses to start without the DMCA agent and Cloudflare purge settings.</summary>
public class LaunchSettingsTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Vela.Api";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static readonly Dictionary<string, string?> Complete = new()
    {
        ["Legal:DmcaAgentName"] = "Agent", ["Legal:DmcaAgentEmail"] = "dmca@example.test", ["Legal:DmcaAgentAddress"] = "1 Main St",
        ["Cloudflare:ZoneId"] = "zone", ["Cloudflare:ApiToken"] = "token",
    };

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Theory]
    [InlineData("Legal:DmcaAgentName")]
    [InlineData("Legal:DmcaAgentEmail")]
    [InlineData("Legal:DmcaAgentAddress")]
    [InlineData("Cloudflare:ZoneId")]
    [InlineData("Cloudflare:ApiToken")]
    public void Production_names_each_missing_launch_setting(string key)
    {
        var values = new Dictionary<string, string?>(Complete) { [key] = " " };

        var e = Assert.Throws<InvalidOperationException>(() => LaunchSettings.Verify(Config(values), new Env("Production")));

        Assert.Contains(key, e.Message);
        Assert.Contains("Launch__RequireLaunchSettings=false", e.Message);
    }

    [Fact]
    public void Complete_settings_pass_and_development_test_and_the_opt_out_never_check()
    {
        LaunchSettings.Verify(Config(Complete), new Env("Production"));
        LaunchSettings.Verify(Config([]), new Env("Development"));
        LaunchSettings.Verify(Config([]), new Env("Test"));
        LaunchSettings.Verify(Config(new() { [LaunchSettings.Switch] = "false" }), new Env("Production"));
        Assert.Throws<InvalidOperationException>(() => LaunchSettings.Verify(Config([]), new Env("Staging")));
    }

    [Fact]
    public void The_real_program_refuses_to_start_in_production_without_them()
    {
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Abuse:RequireAttestation", "false");
            b.UseSetting("ConnectionStrings:Default", "Host=localhost;Database=x;Username=x;Password=x");
            b.UseSetting("Admin:Enabled", "false");
            b.UseSetting("Admin:BanPepper", new string('p', 40));
            b.UseSetting("Auth:Jwt:SigningKey", "integration-test-signing-key-0123456789-abc");
            b.UseSetting("Auth:EmailCode:Pepper", "integration-test-pepper-0123456789-abcdef");
            b.UseSetting("Email:Acs:ConnectionString", "endpoint=https://x.communication.azure.com/;accesskey=abc");
            b.UseSetting("Email:SenderAddress", "noreply@example.test");
            b.UseSetting("Storage:ConnectionString", "UseDevelopmentStorage=true");
            b.UseSetting("Storage:PublicBaseUrl", "https://cdn.example.test/");
            b.UseSetting("Web:ViewerUrl", "https://viewer.example.test");
            b.UseSetting("Branding:Domain", "api.example.test");
            b.UseSetting("ContentSafety:Endpoint", "https://safety.example.test/");
            b.UseSetting("ContentSafety:Key", "k");
        });

        var e = Assert.Throws<InvalidOperationException>(() => f.CreateClient());

        Assert.Contains("Legal:DmcaAgentName", e.Message);
    }
}
