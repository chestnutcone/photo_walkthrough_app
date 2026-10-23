using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Admin;

namespace Vela.Api.Tests.Legal;

public sealed class MutableIp(string ip) : IClientIpResolver
{
    public string Ip { get; set; } = ip;
    public IPAddress Resolve(HttpContext ctx) => IPAddress.Parse(Ip);
}

public sealed class ThrowingEmailSender : IEmailSender
{
    public Task SendAsync(string to, string subject, string textBody) => throw new InvalidOperationException("smtp down");
}

public sealed record Raw(HttpStatusCode Status, string Text, HttpResponseMessage Response)
{
    public JsonElement Json => JsonDocument.Parse(Text).RootElement;
    public string? ErrorCode => Json.GetProperty("error").ValueKind == JsonValueKind.Null ? null : Json.GetProperty("error").GetProperty("code").GetString();
    public string? ErrorMessage => Json.GetProperty("error").ValueKind == JsonValueKind.Null ? null : Json.GetProperty("error").GetProperty("message").GetString();
    public Guid NoticeId => Json.GetProperty("data").GetProperty("noticeId").GetGuid();
}

public static class DmcaKit
{
    public static Dictionary<string, object?> Valid(string reference = "https://app.example/s/00000000-0000-0000-0000-000000000000") => new()
    {
        ["claimantName"] = "Carla Claimant",
        ["claimantEmail"] = "carla@claims.test",
        ["claimantAddress"] = "1 Main Street\nSpringfield",
        ["claimantPhone"] = "+1 555 0100",
        ["workReference"] = reference,
        ["originalWorkDescription"] = "My photograph, first published in 2019.",
        ["goodFaithStatement"] = true,
        ["perjuryStatement"] = true,
        ["signatureName"] = "Carla Claimant",
    };

    public static async Task<Raw> PostNotice(this HttpClient client, object body)
    {
        var r = await client.PostAsJsonAsync("/v1/legal/dmca-notices", body);
        return new Raw(r.StatusCode, await r.Content.ReadAsStringAsync(), r);
    }

    public static async Task<Raw> PostRaw(this HttpClient client, string json)
    {
        var r = await client.PostAsync("/v1/legal/dmca-notices", new StringContent(json, Encoding.UTF8, "application/json"));
        return new Raw(r.StatusCode, await r.Content.ReadAsStringAsync(), r);
    }

    public static async Task<List<DmcaNotice>> Notices(this AdminSite s)
    {
        await using var db = s.Host.Db();
        return await db.DmcaNotices.AsNoTracking().OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).ToListAsync();
    }

    public static async Task<DmcaNotice> Notice(this AdminSite s, Guid id)
    {
        await using var db = s.Host.Db();
        return await db.DmcaNotices.AsNoTracking().SingleAsync(n => n.Id == id);
    }

    public static async Task<List<Report>> DmcaReports(this AdminSite s)
    {
        await using var db = s.Host.Db();
        return await db.Reports.AsNoTracking().Where(r => r.Source == "dmca").OrderBy(r => r.CreatedAt).ToListAsync();
    }
}
