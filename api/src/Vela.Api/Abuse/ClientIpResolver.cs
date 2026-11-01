using System.Net;
using Vela.Api.Common;

namespace Vela.Api.Abuse;

/// <summary>
/// The caller's address. CF-Connecting-IP is believed only when the TCP peer is inside the configured Cloudflare
/// ranges; from anywhere else the header is attacker-controlled and ignored.
/// Behind a reverse proxy that terminates the connection (Azure Container Apps ingress) the TCP peer is the proxy, so
/// <c>Abuse:TrustedProxyHops</c> says how many proxies append to X-Forwarded-For: the connecting address is then the
/// entry that many places from the right (entries further left are client-controlled and never read).
/// </summary>
public sealed class ClientIpResolver : IClientIpResolver
{
    public const string Header = "CF-Connecting-IP";
    public const string ForwardedFor = "X-Forwarded-For";
    private readonly IPNetwork[] _trusted;
    private readonly int _hops;

    public ClientIpResolver(IEnumerable<string> cloudflareRanges, int trustedProxyHops = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trustedProxyHops);
        _trusted = [.. cloudflareRanges.Select(Parse)];
        _hops = trustedProxyHops;
    }

    public IPAddress Resolve(HttpContext ctx)
    {
        if (ctx.Connection.RemoteIpAddress is not { } raw) return IPAddress.Loopback;
        var peer = ConnectingAddress(ctx, Unmap(raw));
        if (!_trusted.Any(n => n.Contains(peer))) return peer;

        var values = ctx.Request.Headers[Header];
        return values.Count == 1 && ParseClient(values[0]) is { } client ? client : peer;
    }

    /// <summary>The address that connected to the outermost trusted proxy, or the TCP peer when X-Forwarded-For cannot be used.</summary>
    private IPAddress ConnectingAddress(HttpContext ctx, IPAddress tcpPeer)
    {
        if (_hops == 0) return tcpPeer;
        var entries = ctx.Request.Headers[ForwardedFor].SelectMany(v => (v ?? "").Split(',')).ToArray();
        return entries.Length >= _hops && ParseClient(entries[^_hops]) is { } ip ? ip : tcpPeer;
    }

    private static IPAddress? ParseClient(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || !IPAddress.TryParse(text, out var ip)) return null;
        // TryParse accepts shorthand such as "1.2"; only full dotted quads and colon forms are real client addresses.
        var isV4 = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        if (isV4 ? text.Count(c => c == '.') != 3 || text.Contains(':') : !text.Contains(':')) return null;
        return Unmap(ip);
    }

    private static IPAddress Unmap(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    private static IPNetwork Parse(string cidr) =>
        IPNetwork.TryParse(cidr.Trim(), out var net)
            ? net
            : throw new InvalidOperationException($"Abuse:CloudflareRanges contains an invalid CIDR range: '{cidr}'.");
}
