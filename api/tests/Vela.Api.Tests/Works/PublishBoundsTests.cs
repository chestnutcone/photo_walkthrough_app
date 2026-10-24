using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Works;

/// <summary>S1b-1 over HTTP: the decode gate answers 503 busy, failed decodes remove the upload, and publish holds no row lock while it decodes.</summary>
[Collection(PostgresCollection.Name)]
public class PublishBoundsTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg, new Dictionary<string, string>
    {
        ["Images:MaxConcurrentDecodes"] = "1",
        ["Images:DecodeWaitSeconds"] = "0",
    });
    private readonly PostgresFixture _pg = pg;
    public void Dispose() => _host.Dispose();

    private Task<TestUser> Artist() => _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
    private ImageDecodeGate Gate => _host.Services.GetRequiredService<ImageDecodeGate>();

    private static async Task<(HttpStatusCode Status, string? Code, string? RetryAfter)> Send(TestUser u, HttpMethod method, string url, object body)
    {
        var r = await u.Client.SendAsync(new HttpRequestMessage(method, url) { Content = System.Net.Http.Json.JsonContent.Create(body) });
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        var code = json.GetProperty("error").ValueKind == JsonValueKind.Null ? null : json.GetProperty("error").GetProperty("code").GetString();
        return (r.StatusCode, code, r.Headers.TryGetValues("Retry-After", out var v) ? v.Single() : null);
    }

    [Fact]
    public void The_gate_is_configured_from_Images_settings()
    {
        Assert.Equal(1, Gate.Limit);
    }

    [Fact]
    public async Task Avatar_generation_without_a_free_decode_slot_is_503_busy_and_keeps_the_upload()
    {
        var u = await Artist();
        _host.Blobs.Put(BlobNames.AvatarOriginal(u.Id), ImageFixtures.Png(100, 100));

        (HttpStatusCode Status, string? Code, string? RetryAfter) r;
        using (await Gate.EnterAsync()) r = await Send(u, HttpMethod.Patch, "/v1/me/profile", new { avatarUploaded = true });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.Status);
        Assert.Equal("busy", r.Code);
        Assert.True(int.Parse(r.RetryAfter!) >= 1);
        Assert.True(_host.Blobs.Exists(BlobNames.AvatarOriginal(u.Id)));
        Assert.Equal(HttpStatusCode.OK, (await u.Patch("/v1/me/profile", new { avatarUploaded = true })).Status);
    }

    [Fact]
    public async Task Publish_without_a_free_decode_slot_is_503_busy_and_the_draft_can_be_published_later()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);

        (HttpStatusCode Status, string? Code, string? RetryAfter) r;
        using (await Gate.EnterAsync()) r = await Send(u, HttpMethod.Post, $"/v1/works/{id}/publish", new { });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.Status);
        Assert.Equal("busy", r.Code);
        Assert.NotNull(r.RetryAfter);
        var w = await Load(_host, id);
        Assert.Equal(WorkStatus.Draft, w.Status);
        Assert.Null(w.PublishClaimedUntil); // the claim was released
        Assert.Equal(HttpStatusCode.OK, (await u.Post($"/v1/works/{id}/publish", new { })).Status);
    }

    [Fact]
    public async Task A_failed_publish_decode_removes_the_upload()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);
        _host.Blobs.Put(BlobNames.WorkOriginal(id), ImageFixtures.Jpeg(800, 800).Take(300).ToArray());

        var r = await u.Post($"/v1/works/{id}/publish", new { });

        Assert.Equal("invalid_image", r.ErrorCode);
        Assert.False(_host.Blobs.Exists(BlobNames.WorkOriginal(id)));
        Assert.Equal(WorkStatus.Draft, (await Load(_host, id)).Status);
        Assert.Equal("image_missing", (await u.Post($"/v1/works/{id}/publish", new { })).ErrorCode); // nothing left to replay
    }

    [Fact]
    public async Task Publish_holds_no_row_lock_while_reading_and_decoding_and_a_second_publish_is_refused_meanwhile()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);
        var reading = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _host.Blobs.BeforeRead = async name =>
        {
            if (!name.StartsWith(BlobNames.Originals + "/", StringComparison.Ordinal)) return;
            reading.TrySetResult();
            await release.Task;
        };

        var publish = u.Post($"/v1/works/{id}/publish", new { });
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // While the image is being read, the row is not locked: NOWAIT would fail with 55P03 if it were.
        await using (var conn = new NpgsqlConnection(_pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id FROM works WHERE id = @id FOR UPDATE NOWAIT", conn, tx);
            cmd.Parameters.AddWithValue("id", id);
            Assert.Equal(id, (Guid)(await cmd.ExecuteScalarAsync())!);
            await tx.RollbackAsync();
        }
        var second = await u.Post($"/v1/works/{id}/publish", new { });
        Assert.Equal(HttpStatusCode.Conflict, second.Status);
        Assert.Equal("not_editable", second.ErrorCode);

        release.SetResult();
        var first = await publish;
        Assert.Equal(HttpStatusCode.OK, first.Status);
        var w = await Load(_host, id);
        Assert.Equal(WorkStatus.Pending, w.Status);
        Assert.Null(w.PublishClaimedUntil);
    }

    [Fact]
    public async Task A_stale_claim_from_a_crashed_publish_does_not_block_the_draft_forever()
    {
        var u = await Artist();
        var id = await ReadyDraft(_host, u);
        await Modify(_host, id, w => w.PublishClaimedUntil = ProfileTestHost.Now.AddMinutes(-1));

        Assert.Equal(HttpStatusCode.OK, (await u.Post($"/v1/works/{id}/publish", new { })).Status);
    }
}
