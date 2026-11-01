using System.Net;

namespace Vela.Api.Common;

public interface IClientIpResolver
{
    IPAddress Resolve(HttpContext ctx);
}
