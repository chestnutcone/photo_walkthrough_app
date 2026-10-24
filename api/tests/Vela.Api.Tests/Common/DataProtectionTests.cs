using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vela.Api.Common;

namespace Vela.Api.Tests.Common;

/// <summary>Admin cookies and antiforgery tokens must survive restarts and be readable by every replica.</summary>
public class DataProtectionTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static ServiceProvider Build(string env, params (string, string)[] kv)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(kv.ToDictionary(k => k.Item1, k => (string?)k.Item2)).Build();
        var s = new ServiceCollection().AddLogging();
        s.AddVelaDataProtection(cfg, new Env(env));
        return s.BuildServiceProvider();
    }

    private const string Conn = "DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=a2V5;EndpointSuffix=core.windows.net";

    [Fact]
    public void Production_stores_the_key_ring_in_blob_storage_under_one_shared_application_name()
    {
        using var sp = Build("Production", ("Storage:ConnectionString", Conn));
        var repo = sp.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;
        Assert.NotNull(repo);
        Assert.Contains("AzureBlobXmlRepository", repo!.GetType().Name);
        Assert.Equal(DataProtectionSetup.ApplicationName, sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Development_and_test_keep_the_default_local_key_ring(string env)
    {
        using var sp = Build(env, ("Storage:ConnectionString", Conn));
        Assert.Null(sp.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
        Assert.Equal(DataProtectionSetup.ApplicationName, sp.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    [Fact]
    public void The_setting_can_force_blob_storage_on_or_off()
    {
        using var on = Build("Development", ("Storage:ConnectionString", Conn), ("DataProtection:PersistToBlob", "true"));
        Assert.NotNull(on.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
        using var off = Build("Production", ("Storage:ConnectionString", Conn), ("DataProtection:PersistToBlob", "false"));
        Assert.Null(off.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
    }

    [Fact]
    public void Production_without_a_storage_connection_string_fails_naming_the_setting()
    {
        var e = Assert.Throws<InvalidOperationException>(() => Build("Production"));
        Assert.Contains("Storage:ConnectionString", e.Message);
    }
}
