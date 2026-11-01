using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Share;

public sealed record Page(HttpStatusCode Status, string Body, HttpResponseMessage Response)
{
    public string Header(string name) => Response.Headers.TryGetValues(name, out var v) ? string.Join(",", v)
        : Response.Content.Headers.TryGetValues(name, out var c) ? string.Join(",", c) : "";
}

public static class ShareTestKit
{
    public static readonly Dictionary<string, string> Config = new()
    {
        ["Web:ViewerUrl"] = "https://viewer.example.test",
        ["Branding:AppName"] = "Brandy",
        ["Branding:Domain"] = "brandy.example.test",
    };

    public static async Task<Page> Fetch(this HttpClient client, string url, HttpMethod? method = null)
    {
        var r = await client.SendAsync(new HttpRequestMessage(method ?? HttpMethod.Get, url));
        return new Page(r.StatusCode, await r.Content.ReadAsStringAsync(), r);
    }

    public static async Task<Guid> Published(ProfileTestHost host, TestUser owner, string title = "Quiet harbour", string intro = "Morning light.")
    {
        var id = await ReadyDraft(host, owner, "Draft title");
        await Modify(host, id, w =>
        {
            w.Title = title; w.Intro = intro; w.Status = WorkStatus.Published;
            w.PublishedAt = ProfileTestHost.Now; w.Width = 400; w.Height = 300; w.ExpiresAt = null;
        });
        return id;
    }

    public static async Task SetDisplayName(ProfileTestHost host, Guid userId, string name)
    {
        await using var db = host.Db();
        await db.Profiles.Where(p => p.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, name));
    }
}
