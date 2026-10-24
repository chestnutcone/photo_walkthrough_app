using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Works;

[Collection(PostgresCollection.Name)]
public class WorkTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg);
    public void Dispose() => _host.Dispose();

    private Task<TestUser> Artist() => _host.SignInOnboarded(ProfileTestHost.UniqueHandle());

    [Fact]
    public async Task Update_rejects_more_than_40_chapters()
    {
        var u = await Artist();
        var id = await Draft(_host, u);

        var r = await u.Put($"/v1/works/{id}", Update(chapters: Enumerable.Range(0, 41).Select(_ => Chapter())));

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("validation_failed", r.ErrorCode);
        Assert.Equal(40, (await u.Put($"/v1/works/{id}", Update(chapters: Enumerable.Range(0, 40).Select(_ => Chapter())))).Data.GetProperty("chapters").GetArrayLength());
    }

    [Theory]
    [InlineData(-0.01, 0.5)]
    [InlineData(1.01, 0.5)]
    [InlineData(0.5, -1)]
    [InlineData(0.5, 1.0001)]
    public async Task Update_rejects_x_or_y_outside_0_to_1(double x, double y)
    {
        var u = await Artist();
        var id = await Draft(_host, u);

        var r = await u.Put($"/v1/works/{id}", Update(chapters: [Chapter(x, y)]));

        Assert.Equal("validation_failed", r.ErrorCode);
    }

    [Fact]
    public async Task Update_accepts_pins_on_the_extreme_edges()
    {
        var u = await Artist();
        var id = await Draft(_host, u);

        var r = await u.Put($"/v1/works/{id}", Update(chapters: [Chapter(0, 1), Chapter(1, 0, 8.0), Chapter(0.5, 0.5, 1.0)]));

        Assert.Equal(HttpStatusCode.OK, r.Status);
    }

    [Theory]
    [InlineData(0.99)]
    [InlineData(8.01)]
    [InlineData(0.0)]
    public async Task Update_rejects_zoom_outside_1_to_8(double zoom)
    {
        var u = await Artist();
        var id = await Draft(_host, u);

        Assert.Equal("validation_failed", (await u.Put($"/v1/works/{id}", Update(chapters: [Chapter(zoom: zoom)]))).ErrorCode);
    }

    [Fact]
    public async Task Zoom_null_is_stored_as_stay_put_and_returned_as_null()
    {
        var u = await Artist();
        var id = await Draft(_host, u);

        var r = await u.Put($"/v1/works/{id}", Update(chapters: [Chapter(zoom: null), Chapter(zoom: 2.4)]));

        Assert.Equal(JsonValueKindNull, r.Data.GetProperty("chapters")[0].GetProperty("zoom").ValueKind);
        Assert.Equal(2.4, r.Data.GetProperty("chapters")[1].GetProperty("zoom").GetDouble());
        await using var db = _host.Db();
        Assert.Null((await db.Chapters.SingleAsync(c => c.WorkId == id && c.Index == 0)).Zoom);
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    [Fact]
    public async Task Update_replaces_chapters_wholesale_with_sequential_index()
    {
        var u = await Artist();
        var id = await Draft(_host, u);
        await u.Put($"/v1/works/{id}", Update(chapters: [Chapter(title: "a"), Chapter(title: "b"), Chapter(title: "c")]));

        var r = await u.Put($"/v1/works/{id}", Update("New", [Chapter(title: "z")], intro: "Hello", altText: "alt"));

        Assert.Equal(1, r.Data.GetProperty("chapters").GetArrayLength());
        await using var db = _host.Db();
        var rows = await db.Chapters.Where(c => c.WorkId == id).OrderBy(c => c.Index).ToListAsync();
        Assert.Equal([0], rows.Select(c => c.Index));
        Assert.Equal("z", rows[0].Title);
        var w = await db.Works.SingleAsync(x => x.Id == id);
        Assert.Equal(("New", "Hello", "alt"), (w.Title, w.Intro, w.AltText));
    }

    public static TheoryData<string, object> BadUpdates() => new()
    {
        { "title", Update(title: "") },
        { "title", Update(title: new string('a', 201)) },
        { "title", Update(title: "   ") },
        { "intro", Update(intro: new string('a', 1001)) },
        { "altText", Update(altText: new string('a', 301)) },
        { "title", Update(title: "bad\u0007title") },
        { "title", Update(chapters: [Chapter(title: "")]) },
        { "title", Update(chapters: [Chapter(title: new string('a', 121))]) },
        { "body", Update(chapters: [Chapter(body: new string('a', 1001))]) },
        { "meta", Update(chapters: [Chapter(meta: new string('a', 81))]) },
        { "altText", Update(chapters: [Chapter(altText: new string('a', 301))]) },
        { "body", Update(chapters: [Chapter(body: "nul\0byte")]) },
    };

    [Theory]
    [MemberData(nameof(BadUpdates))]
    public async Task Update_rejects_text_outside_limits_without_echoing_it(string _, object body)
    {
        var u = await Artist();
        var id = await Draft(_host, u);

        var r = await u.Put($"/v1/works/{id}", body);

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("validation_failed", r.ErrorCode);
        Assert.DoesNotContain("aaaa", r.Body.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Update_accepts_newlines_and_text_at_the_limits()
    {
        var u = await Artist();
        var id = await Draft(_host, u);

        var r = await u.Put($"/v1/works/{id}", Update(new string('t', 200),
            [Chapter(title: new string('c', 120), body: new string('b', 999) + "\n", meta: new string('m', 80), altText: new string('a', 300))],
            intro: new string('i', 1000), altText: new string('a', 300)));

        Assert.Equal(HttpStatusCode.OK, r.Status);
    }

    [Fact]
    public async Task Update_rejects_malformed_bodies()
    {
        var u = await Artist();
        var id = await Draft(_host, u);

        Assert.Equal("validation_failed", (await u.Put($"/v1/works/{id}", "{not json")).ErrorCode);
        Assert.Equal("validation_failed", (await u.Put($"/v1/works/{id}", new { title = "x" })).ErrorCode);
        Assert.Equal("validation_failed", (await u.Put($"/v1/works/{id}", new { title = "x", chapters = new[] { new { x = 0.5 } } })).ErrorCode);
    }

    [Fact]
    public async Task Update_is_refused_unless_the_work_is_a_draft()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);
        Assert.Equal(HttpStatusCode.OK, (await u.Post($"/v1/works/{id}/publish", new { })).Status);

        var r = await u.Put($"/v1/works/{id}", Update());

        Assert.Equal(HttpStatusCode.Conflict, r.Status);
        Assert.Equal("not_editable", r.ErrorCode);
    }

    [Fact]
    public async Task Non_owner_cannot_update_or_delete()
    {
        var owner = await Artist();
        var other = await Artist();
        var id = await ReadyDraft(_host, owner);

        var put = await other.Put($"/v1/works/{id}", Update("Hijack"));
        var del = await other.Delete($"/v1/works/{id}");
        var publish = await other.Post($"/v1/works/{id}/publish", new { });

        Assert.All(new[] { put, del, publish }, r => Assert.Equal("work_not_found", r.ErrorCode));
        Assert.Equal("Title", (await Load(_host, id)).Title);
    }

    [Fact]
    public async Task Write_operations_require_authentication()
    {
        var anon = _host.Anonymous();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsync($"/v1/works/{id}", new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.DeleteAsync($"/v1/works/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync($"/v1/works/{id}/publish", null)).StatusCode);
    }

    [Fact]
    public async Task Hostile_text_is_stored_verbatim_and_returned_unescaped_as_json()
    {
        var u = await Artist();
        var id = await Draft(_host, u);
        // Right-to-left text with a direction MARK is accepted; bidi OVERRIDES are refused (Security/HardeningTests).
        const string title = "<script>alert('x')</script> & \"q\" ‏مرحبا 🎨";
        const string body = "<img src=x onerror=1>\nline two ' OR 1=1 --";

        var r = await u.Put($"/v1/works/{id}", Update(title, [Chapter(title: "<b>c</b>", body: body)], intro: "<i>hi</i>"));
        var got = await u.Get($"/v1/works/{id}");

        Assert.Equal(title, r.Data.GetProperty("title").GetString());
        Assert.Equal(title, got.Data.GetProperty("title").GetString());
        Assert.Equal(body, got.Data.GetProperty("chapters")[0].GetProperty("body").GetString());
        Assert.Equal("<b>c</b>", got.Data.GetProperty("chapters")[0].GetProperty("title").GetString());
        Assert.Equal(title, (await Load(_host, id)).Title);
    }

    [Fact]
    public async Task Publish_with_zero_chapters_is_refused()
    {
        var u = await Artist();
        var id = await Draft(_host, u);
        await u.Put($"/v1/works/{id}", Update(chapters: []));

        var r = await u.Post($"/v1/works/{id}/publish", new { });

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("no_chapters", r.ErrorCode);
        Assert.Equal(WorkStatus.Draft, (await Load(_host, id)).Status);
    }

    [Fact]
    public async Task Publish_without_an_uploaded_original_is_refused()
    {
        var u = await Artist();
        var id = await Draft(_host, u, withImage: false);
        await u.Put($"/v1/works/{id}", Update());

        var r = await u.Post($"/v1/works/{id}/publish", new { });

        Assert.Equal("image_missing", r.ErrorCode);
        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
    }

    [Fact]
    public async Task Publish_of_a_corrupt_image_is_refused_and_leaves_the_draft()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);
        _host.Blobs.Put(BlobNames.WorkOriginal(id), [1, 2, 3, 4]);

        var r = await u.Post($"/v1/works/{id}/publish", new { });

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("invalid_image", r.ErrorCode);
        Assert.Equal(WorkStatus.Draft, (await Load(_host, id)).Status);
    }

    [Fact]
    public async Task Publish_of_an_expired_draft_is_refused()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);
        await Modify(_host, id, w => w.ExpiresAt = ProfileTestHost.Now.AddMinutes(-1));

        var r = await u.Post($"/v1/works/{id}/publish", new { });

        Assert.Equal(HttpStatusCode.Conflict, r.Status);
        Assert.Equal("draft_expired", r.ErrorCode);
    }

    [Fact]
    public async Task Publish_generates_variants_sets_pending_and_clears_expiry()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);

        var r = await u.Post($"/v1/works/{id}/publish", new { });

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal("pending", r.Data.GetProperty("status").GetString());
        var w = await Load(_host, id);
        Assert.Equal(WorkStatus.Pending, w.Status);
        Assert.Equal((400, 300), (w.Width, w.Height));
        Assert.Null(w.ExpiresAt);
        Assert.Equal(ProfileTestHost.Now, w.PendingSince);
        Assert.True(_host.Blobs.Exists($"works/{id}/feed.webp"));
    }

    [Fact]
    public async Task Publish_twice_is_not_editable()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);
        await u.Post($"/v1/works/{id}/publish", new { });

        Assert.Equal("not_editable", (await u.Post($"/v1/works/{id}/publish", new { })).ErrorCode);
    }

    [Fact]
    public async Task Deleting_removes_blobs_row_and_frees_the_quota_slot()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);
        await u.Post($"/v1/works/{id}/publish", new { });
        Assert.True(_host.Blobs.Exists(BlobNames.WorkOriginal(id)));
        Assert.Equal(1, (await u.Get("/v1/me")).Data.GetProperty("usedWorks").GetInt32());

        var r = await u.Delete($"/v1/works/{id}");

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, r.Data.ValueKind);
        Assert.DoesNotContain(_host.Blobs.Names, n => n.Contains(id.ToString()));
        await using var db = _host.Db();
        Assert.False(await db.Works.AnyAsync(w => w.Id == id));
        Assert.False(await db.Chapters.AnyAsync(c => c.WorkId == id));
        Assert.Equal(0, (await u.Get("/v1/me")).Data.GetProperty("usedWorks").GetInt32());
    }

    [Fact]
    public async Task Deleting_your_own_work_twice_is_404_the_second_time()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);

        Assert.Equal(HttpStatusCode.OK, (await u.Delete($"/v1/works/{id}")).Status);
        Assert.Equal("work_not_found", (await u.Delete($"/v1/works/{id}")).ErrorCode);
    }

    [Fact]
    public async Task Nonexistent_and_foreign_work_ids_give_identical_responses()
    {
        var owner = await Artist();
        var other = await Artist();
        var foreign = await ReadyDraft(_host, owner);
        var missing = Guid.NewGuid();

        async Task<string> Shape(Task<Vela.Api.Tests.Profiles.Reply> r) { var x = await r; return $"{(int)x.Status}|{x.Body.GetProperty("error")}"; }
        foreach (var call in new Func<Guid, Task<Vela.Api.Tests.Profiles.Reply>>[]
        {
            id => other.Put($"/v1/works/{id}", Update()),
            id => other.Post($"/v1/works/{id}/publish", new { }),
            id => other.Delete($"/v1/works/{id}"),
            id => other.Get($"/v1/works/{id}"),
        })
        {
            var a = await Shape(call(foreign));
            var b = await Shape(call(missing));
            Assert.Equal(b, a);
            Assert.StartsWith("404|", a);
        }
    }
}
