using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Works;

[Collection(PostgresCollection.Name)]
public class WorkReadTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg);
    public void Dispose() => _host.Dispose();

    private Task<TestUser> Artist(string name = "Test Artist") => _host.SignInOnboarded(ProfileTestHost.UniqueHandle(), name);

    private async Task<Guid> Published(TestUser owner, params object[] chapters)
    {
        var id = await ReadyDraft(_host, owner, "Published", chapters.Length == 0 ? null : chapters);
        await Modify(_host, id, w => { w.Status = WorkStatus.Published; w.PublishedAt = ProfileTestHost.Now; w.Width = 400; w.Height = 300; w.ExpiresAt = null; });
        return id;
    }

    [Fact]
    public async Task Draft_is_not_visible_to_other_users_or_anonymous()
    {
        var owner = await Artist();
        var other = await Artist();
        var id = await ReadyDraft(_host, owner);

        Assert.Equal("work_not_found", (await other.Get($"/v1/works/{id}")).ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.Anonymous().GetAsync($"/v1/works/{id}")).StatusCode);
        var own = await owner.Get($"/v1/works/{id}");
        Assert.Equal("draft", own.Data.GetProperty("status").GetString());
        Assert.True(own.Data.GetProperty("isOwner").GetBoolean());
    }

    [Fact]
    public async Task Published_work_is_public_with_ordered_chapters_artist_and_image_urls()
    {
        var owner = await Artist("Ada Lovelace");
        var id = await Published(owner, Chapter(title: "one"), Chapter(title: "two", zoom: 3));

        var r = await _host.Anonymous().GetAsync($"/v1/works/{id}");
        var body = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(["one", "two"], body.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("title").GetString()));
        Assert.Equal("Ada Lovelace", body.GetProperty("author").GetProperty("displayName").GetString());
        Assert.Equal(owner.Id, body.GetProperty("author").GetProperty("id").GetGuid());
        Assert.Equal($"https://cdn.test/works/{id}/thumb.webp", body.GetProperty("thumbUrl").GetString());
        Assert.Equal($"https://cdn.test/works/{id}/feed.webp", body.GetProperty("feedUrl").GetString());
        Assert.Equal($"https://cdn.test/works/{id}/full.webp", body.GetProperty("imageUrl").GetString());
        Assert.Equal((400, 300), (body.GetProperty("imageWidth").GetInt32(), body.GetProperty("imageHeight").GetInt32()));
        Assert.False(body.GetProperty("isOwner").GetBoolean());
        Assert.False(body.TryGetProperty("status", out var s) && s.ValueKind != System.Text.Json.JsonValueKind.Null);
        Assert.Contains(id.ToString(), body.GetProperty("shareUrl").GetString());
    }

    [Fact]
    public async Task Chapters_carry_reply_counts_excluding_hidden_replies_and_blocked_authors()
    {
        var owner = await Artist();
        var viewer = await Artist();
        var blocked = await Artist();
        var id = await Published(owner, Chapter(), Chapter());
        await using (var db = _host.Db())
        {
            Vela.Api.Data.Entities.Reply R(Guid author, int ch, bool hidden = false) => new() { Id = Guid.NewGuid(), WorkId = id, ChapterIndex = ch, AuthorId = author, Text = "t", Hidden = hidden, CreatedAt = ProfileTestHost.Now };
            db.Replies.AddRange(R(owner.Id, 0), R(owner.Id, 0), R(owner.Id, 0, hidden: true), R(blocked.Id, 0), R(owner.Id, 1));
            await db.SaveChangesAsync();
        }
        await viewer.Put($"/v1/blocks/{blocked.Id}");

        var r = await viewer.Get($"/v1/works/{id}");

        Assert.Equal([2, 1], r.Data.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("replyCount").GetInt32()));
        var all = await owner.Get($"/v1/works/{id}");
        Assert.Equal([3, 1], all.Data.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("replyCount").GetInt32()));
    }

    [Fact]
    public async Task Blocked_work_is_visible_only_to_its_owner_and_admins()
    {
        var owner = await Artist();
        var other = await Artist();
        var admin = await Artist();
        await MakeAdmin(_host, admin.Id);
        var id = await Published(owner);
        await Modify(_host, id, w => w.Status = WorkStatus.Blocked);

        Assert.Equal("work_not_found", (await other.Get($"/v1/works/{id}")).ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.Anonymous().GetAsync($"/v1/works/{id}")).StatusCode);
        Assert.Equal("blocked", (await owner.Get($"/v1/works/{id}")).Data.GetProperty("status").GetString());
        Assert.Equal("blocked", (await admin.Get($"/v1/works/{id}")).Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Report_hidden_work_is_visible_only_to_owner_and_admins()
    {
        var owner = await Artist();
        var other = await Artist();
        var admin = await Artist();
        await MakeAdmin(_host, admin.Id);
        var id = await Published(owner);
        await Modify(_host, id, w => w.ReportHidden = true);

        Assert.Equal("work_not_found", (await other.Get($"/v1/works/{id}")).ErrorCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.Get($"/v1/works/{id}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await admin.Get($"/v1/works/{id}")).Status);
    }

    [Fact]
    public async Task Work_of_a_blocked_suspended_or_deleted_owner_is_hidden_from_others()
    {
        var owner = await Artist();
        var viewer = await Artist();
        var id = await Published(owner);
        Assert.Equal(HttpStatusCode.OK, (await viewer.Get($"/v1/works/{id}")).Status);

        await viewer.Put($"/v1/blocks/{owner.Id}");
        Assert.Equal("work_not_found", (await viewer.Get($"/v1/works/{id}")).ErrorCode);
        await viewer.Delete($"/v1/blocks/{owner.Id}");

        await using var db = _host.Db();
        await db.Users.Where(u => u.Id == owner.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));
        Assert.Equal("work_not_found", (await viewer.Get($"/v1/works/{id}")).ErrorCode);
        await db.Users.Where(u => u.Id == owner.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, (DateTimeOffset?)null).SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        Assert.Equal("work_not_found", (await viewer.Get($"/v1/works/{id}")).ErrorCode);
    }

    [Fact]
    public async Task Unknown_work_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _host.Anonymous().GetAsync($"/v1/works/{Guid.NewGuid()}")).StatusCode);
    }
}
