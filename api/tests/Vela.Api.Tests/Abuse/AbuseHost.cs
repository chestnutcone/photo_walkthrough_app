using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Common;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Abuse;

/// <summary>Resolves the client address from a test header so each test controls its own address.</summary>
public sealed class HeaderIp : IClientIpResolver
{
    public IPAddress Resolve(HttpContext ctx) =>
        IPAddress.Parse(ctx.Request.Headers["X-Test-Ip"].FirstOrDefault() ?? "127.0.0.1");
}

public sealed record Raw(HttpResponseMessage Response, string Text)
{
    public HttpStatusCode Status => Response.StatusCode;
    public JsonElement Json => JsonDocument.Parse(Text).RootElement;
    public string? ErrorCode => Json.GetProperty("error").ValueKind == JsonValueKind.Null ? null : Json.GetProperty("error").GetProperty("code").GetString();
}

public static class AbuseKit
{
    public static string NewIp() => $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";
    public static string NewEmail() => $"ab-{Guid.NewGuid():N}@example.test";

    /// <summary>A host on its own database. Pass useHeaderIp to control the client address with X-Test-Ip.</summary>
    public static async Task<ProfileTestHost> Host(PostgresFixture pg, Dictionary<string, string>? settings = null, bool useHeaderIp = true, string? cs = null) =>
        new(pg, settings, cs ?? await pg.CreateIsolatedDatabaseAsync(),
            s => { if (useHeaderIp) s.AddSingleton<IClientIpResolver, HeaderIp>(); });

    public static async Task<Raw> Send(HttpClient c, HttpMethod method, string url, string? json = null, string? ip = null,
        Action<HttpRequestMessage>? tweak = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (ip is not null) req.Headers.Add("X-Test-Ip", ip);
        tweak?.Invoke(req);
        var r = await c.SendAsync(req);
        return new Raw(r, await r.Content.ReadAsStringAsync());
    }

    public static Task<Raw> EmailRequest(HttpClient c, string email, string? ip = null, Action<HttpRequestMessage>? tweak = null) =>
        Send(c, HttpMethod.Post, "/v1/auth/email/request", JsonSerializer.Serialize(new { email }), ip ?? NewIp(), tweak);
}
