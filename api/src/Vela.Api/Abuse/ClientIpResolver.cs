using System.Net;
using Vela.Api.Common;

namespace Vela.Api.Abuse;

/// <summary>
/// The caller's address. CF-Connecting-IP is believed only when the TCP peer is inside the configured Cloudflare
/// ranges; from anywhere else the header is attacker-controlled and ignored.
/// </summary>
public sealed class ClientIpResolver : IClientIpResolver
{
    public const string Header = "CF-Connecting-IP";
    private readonly IPNetwork[] _trusted;

    public ClientIpResolver(IEnumerable<string> cloudflareRanges) =>
        _trusted = [.. cloudflareRanges.Select(Parse)];

    public IPAddress Resolve(HttpContext ctx)
    {
        if (ctx.Connection.RemoteIpAddress is not { } raw) return IPAddress.Loopback;
        var peer = Unmap(raw);
        if (!_trusted.Any(n => n.Contains(peer))) return peer;

        var values = ctx.Request.Headers[Header];
        return values.Count == 1 && ParseClient(values[0]) is { } client ? client : peer;
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
