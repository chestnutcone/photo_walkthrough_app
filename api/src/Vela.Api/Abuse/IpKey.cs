using System.Net;

namespace Vela.Api.Abuse;

/// <summary>Bucket key for an address: IPv4 as is, IPv6 by its /64 so a host cannot dodge a limit by rotating inside its own prefix.</summary>
public static class IpKey
{
    public static string Of(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return ip.ToString();
        return Convert.ToHexStringLower(ip.GetAddressBytes().AsSpan(0, 8)) + "::/64";
    }
}
