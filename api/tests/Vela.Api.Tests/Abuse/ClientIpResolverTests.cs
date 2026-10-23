using System.Net;
using Microsoft.AspNetCore.Http;
using Vela.Api.Abuse;

namespace Vela.Api.Tests.Abuse;

public class ClientIpResolverTests
{
    private static ClientIpResolver Resolver(params string[] ranges) =>
        new(ranges.Length == 0 ? CloudflareRanges.Default : ranges);

    private static DefaultHttpContext Ctx(string? remote, params string[] cfHeader)
    {
        var ctx = new DefaultHttpContext();
        if (remote is not null) ctx.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (cfHeader.Length > 0) ctx.Request.Headers["CF-Connecting-IP"] = cfHeader;
        return ctx;
    }

    [Fact]
    public void Spoofed_cf_connecting_ip_is_ignored_when_not_from_cloudflare()
    {
        var ip = Resolver().Resolve(Ctx("198.51.100.7", "203.0.113.9"));
        Assert.Equal("198.51.100.7", ip.ToString());
    }

    [Fact]
    public void Valid_cf_connecting_ip_from_cloudflare_range_is_used()
    {
        var ip = Resolver().Resolve(Ctx("173.245.48.5", "203.0.113.9"));
        Assert.Equal("203.0.113.9", ip.ToString());
    }

    [Fact]
    public void Cloudflare_ipv6_peer_may_carry_an_ipv6_client()
    {
        var ip = Resolver().Resolve(Ctx("2606:4700::1", "2001:db8::5"));
        Assert.Equal("2001:db8::5", ip.ToString());
    }

    [Fact]
    public void Ipv4_mapped_cloudflare_peer_is_recognised()
    {
        var ip = Resolver().Resolve(Ctx("::ffff:104.16.0.9", "203.0.113.9"));
        Assert.Equal("203.0.113.9", ip.ToString());
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("")]
    [InlineData("203.0.113.9, 198.51.100.1")]
    public void Unusable_header_from_cloudflare_falls_back_to_the_peer(string header)
    {
        var ip = Resolver().Resolve(Ctx("173.245.48.5", header));
        Assert.Equal("173.245.48.5", ip.ToString());
    }

    [Fact]
    public void Repeated_header_values_fall_back_to_the_peer()
    {
        var ip = Resolver().Resolve(Ctx("173.245.48.5", "203.0.113.9", "203.0.113.10"));
        Assert.Equal("173.245.48.5", ip.ToString());
    }

    [Fact]
    public void Missing_peer_resolves_to_loopback_and_ignores_the_header()
    {
        var ip = Resolver().Resolve(Ctx(null, "203.0.113.9"));
        Assert.Equal(IPAddress.Loopback, ip);
    }

    [Fact]
    public void Peer_without_header_is_returned_unchanged()
    {
        Assert.Equal("173.245.48.5", Resolver().Resolve(Ctx("173.245.48.5")).ToString());
    }

    [Fact]
    public void Configured_ranges_replace_the_defaults()
    {
        var custom = Resolver("192.0.2.0/24");
        Assert.Equal("203.0.113.9", custom.Resolve(Ctx("192.0.2.77", "203.0.113.9")).ToString());
        Assert.Equal("173.245.48.5", custom.Resolve(Ctx("173.245.48.5", "203.0.113.9")).ToString());
    }

    [Fact]
    public void Default_ranges_cover_the_published_cloudflare_edge()
    {
        Assert.Equal(22, CloudflareRanges.Default.Count);
        foreach (var edge in new[] { "104.16.0.1", "172.64.5.5", "162.159.1.1", "2606:4700:4700::1111" })
            Assert.Equal("203.0.113.9", Resolver().Resolve(Ctx(edge, "203.0.113.9")).ToString());
    }

    [Fact]
    public void A_malformed_range_fails_fast()
    {
        Assert.Throws<InvalidOperationException>(() => new ClientIpResolver(["300.1.1.1/8"]));
    }

    [Fact]
    public void Remote_address_is_used_when_the_header_is_absent_even_if_a_forwarded_for_is_sent()
    {
        var ctx = Ctx("198.51.100.7");
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        Assert.Equal("198.51.100.7", Resolver().Resolve(ctx).ToString());
    }
}
