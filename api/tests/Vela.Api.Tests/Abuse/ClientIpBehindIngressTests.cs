using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Vela.Api.Abuse;

namespace Vela.Api.Tests.Abuse;

/// <summary>Azure Container Apps terminates the connection: the TCP peer is the ingress and the real peer is the last X-Forwarded-For entry.</summary>
public class ClientIpBehindIngressTests
{
    private const string Ingress = "100.100.0.12";

    private static ClientIpResolver Resolver(int hops) => new(CloudflareRanges.Default, hops);

    private static DefaultHttpContext Ctx(string? xff, string? cf = null, string peer = Ingress)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        if (xff is not null) ctx.Request.Headers["X-Forwarded-For"] = xff;
        if (cf is not null) ctx.Request.Headers["CF-Connecting-IP"] = cf;
        return ctx;
    }

    [Fact]
    public void Cloudflare_edge_in_forwarded_for_makes_the_cf_header_trusted()
    {
        var ip = Resolver(1).Resolve(Ctx("203.0.113.9, 104.16.0.9", cf: "203.0.113.9"));
        Assert.Equal("203.0.113.9", ip.ToString());
    }

    [Fact]
    public void Spoofed_cf_header_is_ignored_when_the_connecting_address_is_not_cloudflare()
    {
        var ip = Resolver(1).Resolve(Ctx("198.51.100.7", cf: "203.0.113.9"));
        Assert.Equal("198.51.100.7", ip.ToString());
    }

    [Fact]
    public void Client_supplied_forwarded_for_entries_before_the_ingress_are_ignored()
    {
        var ip = Resolver(1).Resolve(Ctx("104.16.0.9, 198.51.100.7", cf: "203.0.113.9"));
        Assert.Equal("198.51.100.7", ip.ToString());
    }

    [Fact]
    public void Two_hops_skip_one_more_proxy()
    {
        var ip = Resolver(2).Resolve(Ctx("203.0.113.9, 104.16.0.9, 100.100.0.99", cf: "203.0.113.9"));
        Assert.Equal("203.0.113.9", ip.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-ip")]
    public void Missing_or_unusable_forwarded_for_falls_back_to_the_tcp_peer(string? xff)
    {
        Assert.Equal(Ingress, Resolver(1).Resolve(Ctx(xff, cf: "203.0.113.9")).ToString());
    }

    [Fact]
    public void Fewer_entries_than_hops_falls_back_to_the_tcp_peer()
    {
        Assert.Equal(Ingress, Resolver(2).Resolve(Ctx("104.16.0.9", cf: "203.0.113.9")).ToString());
    }

    [Fact]
    public void Zero_hops_ignores_forwarded_for_entirely()
    {
        var ip = Resolver(0).Resolve(Ctx("104.16.0.9", cf: "203.0.113.9"));
        Assert.Equal(Ingress, ip.ToString());
    }

    [Fact]
    public void Repeated_forwarded_for_headers_are_joined_in_order()
    {
        var ctx = Ctx(null, cf: "203.0.113.9");
        ctx.Request.Headers["X-Forwarded-For"] = new[] { "203.0.113.9", "104.16.0.9" };
        Assert.Equal("203.0.113.9", Resolver(1).Resolve(ctx).ToString());
    }

    [Fact]
    public void Negative_hops_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolver(-1));
    }

    private static IConfiguration Cfg(params (string, string)[] kv) =>
        new ConfigurationBuilder().AddInMemoryCollection(kv.ToDictionary(k => k.Item1, k => (string?)k.Item2)).Build();

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void Trusted_proxy_hops_default_to_zero_and_read_from_config()
    {
        Assert.Equal(0, AbuseOptions.Load(Cfg(), new Env("Test")).TrustedProxyHops);
        Assert.Equal(1, AbuseOptions.Load(Cfg(("Abuse:TrustedProxyHops", "1")), new Env("Test")).TrustedProxyHops);
    }

    [Fact]
    public void Negative_trusted_proxy_hops_fail_at_startup()
    {
        Assert.Throws<InvalidOperationException>(() => AbuseOptions.Load(Cfg(("Abuse:TrustedProxyHops", "-1")), new Env("Test")));
    }
}
