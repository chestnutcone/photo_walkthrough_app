using System.Net;

namespace Vela.Api.Common;

public interface IClientIpResolver
{
    IPAddress Resolve(HttpContext ctx);
}

/// <summary>Default: the connection's remote address. A15 replaces this with a Cloudflare-aware resolver.</summary>
public sealed class RemoteAddressClientIpResolver : IClientIpResolver
{
    public IPAddress Resolve(HttpContext ctx) => ctx.Connection.RemoteIpAddress ?? IPAddress.Loopback;
}
