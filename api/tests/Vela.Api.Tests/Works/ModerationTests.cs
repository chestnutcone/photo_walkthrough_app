using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Moderation;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Works;

public sealed class RecordingHandler : IDomainEventHandler<WorkPublished>
{
    public static readonly List<WorkPublished> Seen = [];
    public Task HandleAsync(WorkPublished evt, CancellationToken ct) { lock (Seen) Seen.Add(evt); return Task.CompletedTask; }
}

[Collection(PostgresCollection.Name)]
public class ModerationTests : IDisposable
{
    private readonly ProfileTestHost _host;
    private readonly FakeContentSafety _safety;
    private readonly PublishPipeline _pipeline;
    private static readonly TimeSpan Later = TimeSpan.FromSeconds(10);

    public ModerationTests(PostgresFixture pg)
    {
        _host = new ProfileTestHost(pg, configureServices: s => s.AddScoped<IDomainEventHandler<WorkPublished>, RecordingHandler>());
        _safety = _host.Services.GetRequiredService<FakeContentSafety>();
        _pipeline = _host.Services.GetRequiredService<PublishPipeline>();
    }

    public void Dispose() => _host.Dispose();

    private async Task<(TestUser Owner, Guid Id)> Pending(string title = "Title", IEnumerable<object>? chapters = null)
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var id = await ReadyDraft(_host, u, title, chapters);
        var r = await u.Post($"/v1/works/{id}/publish", new { });
        Assert.True(r.Body.GetProperty("success").GetBoolean(), r.Body.ToString());
        return (u, id);
    }

    private Task Run(TimeSpan? after = null) => _pipeline.RunOnceAsync(ProfileTestHost.Now + (after ?? Later), CancellationToken.None);

    private async Task<List<Report>> Flags(Guid id)
    {
        await using var db = _host.Db();
        return await db.Reports.AsNoTracking().Where(r => r.TargetId == id).ToListAsync();
    }

    [Fact]
    public async Task Clean_work_becomes_published_after_pipeline_runs_and_raises_the_event()
    {
        var (owner, id) = await Pending();
        RecordingHandler.Seen.Clear();

        await Run();

        var w = await Load(_host, id);
        Assert.Equal(WorkStatus.Published, w.Status);
        Assert.Equal(ProfileTestHost.Now + Later, w.PublishedAt);
        Assert.Empty(await Flags(id));
        Assert.Contains(RecordingHandler.Seen, e => e == new WorkPublished(id, owner.Id));
        Assert.Equal(1, _safety.ImageScans);
        Assert.True((await _host.Anonymous().GetAsync($"/v1/works/{id}")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Flagged_image_severity_4_sets_status_blocked_and_creates_queue_item()
    {
        var (_, id) = await Pending();
        _safety.ImageSeverity = 4;

        await Run();

        Assert.Equal(WorkStatus.Blocked, (await Load(_host, id)).Status);
        var flag = Assert.Single(await Flags(id));
        Assert.Equal(("auto_flag", "work", "auto:unsafe_image:4", "pending"), (flag.Source, flag.TargetType, flag.Reason, flag.Status));
    }

    [Fact]
    public async Task Flagged_text_severity_4_sets_status_blocked_and_creates_queue_item()
    {
        var (_, id) = await Pending(chapters: [Chapter(body: "this is [[unsafe]] text")]);

        await Run();

        Assert.Equal(WorkStatus.Blocked, (await Load(_host, id)).Status);
        Assert.Equal("auto:unsafe_text:6", Assert.Single(await Flags(id)).Reason);
    }

    [Fact]
    public async Task Severity_3_is_published()
    {
        var (_, id) = await Pending();
        _safety.ImageSeverity = 3;

        await Run();

        Assert.Equal(WorkStatus.Published, (await Load(_host, id)).Status);
    }

    [Fact]
    public async Task Long_text_is_scanned_in_chunks_of_at_most_9000_characters()
    {
        var (_, id) = await Pending(chapters: [Chapter(body: new string('a', 900)), Chapter(), Chapter(), Chapter()]);
        await Modify(_host, id, w => w.Intro = new string('i', 1000));
        await Modify(_host, id, w => w.AltText = new string('t', 300));
        _safety.TextRequests.Clear();

        await Run();

        Assert.NotEmpty(_safety.TextRequests);
        Assert.All(_safety.TextRequests, t => Assert.True(t.Length <= PublishPipeline.MaxTextChars));
        Assert.Contains(_safety.TextRequests, t => t.Contains("Title"));
    }

    [Fact]
    public async Task Marker_in_a_late_chunk_is_still_caught()
    {
        var chapters = Enumerable.Range(0, 12).Select(i => Chapter(body: new string('x', 900) + (i == 11 ? "[[unsafe]]" : ""))).ToList();
        var (_, id) = await Pending(chapters: chapters);

        await Run();

        Assert.Equal(WorkStatus.Blocked, (await Load(_host, id)).Status);
    }

    [Fact]
    public void Chunking_splits_without_losing_text()
    {
        var parts = new[] { new string('a', 5000), new string('b', 12000), new string('c', 100) };

        var chunks = PublishPipeline.Chunk(parts);

        Assert.All(chunks, c => Assert.True(c.Length <= PublishPipeline.MaxTextChars));
        var all = string.Concat(chunks);
        Assert.Equal((5000, 12000, 100), (all.Count(c => c == 'a'), all.Count(c => c == 'b'), all.Count(c => c == 'c')));
        Assert.Empty(PublishPipeline.Chunk(["", "  "]));
    }

    [Fact]
    public async Task Content_safety_outage_retries_then_publishes_with_scan_unavailable_flag()
    {
        var (_, id) = await Pending();
        _safety.Throw = true;

        await Run(Later);
        Assert.Equal((WorkStatus.Pending, 1), ((await Load(_host, id)).Status, (await Load(_host, id)).ScanAttempts));
        await Run(Later + TimeSpan.FromMinutes(1));
        Assert.Equal(1, (await Load(_host, id)).ScanAttempts);
        await Run(Later + TimeSpan.FromMinutes(4));
        Assert.Equal((WorkStatus.Pending, 2), ((await Load(_host, id)).Status, (await Load(_host, id)).ScanAttempts));
        Assert.Empty(await Flags(id));

        RecordingHandler.Seen.Clear();
        await Run(Later + TimeSpan.FromMinutes(8));

        var w = await Load(_host, id);
        Assert.Equal(WorkStatus.Published, w.Status);
        Assert.Equal("auto:scan_unavailable:0", Assert.Single(await Flags(id)).Reason);
        Assert.Contains(RecordingHandler.Seen, e => e.WorkId == id);
    }

    [Fact]
    public async Task Outage_that_recovers_publishes_normally_without_a_flag()
    {
        var (_, id) = await Pending();
        _safety.Throw = true;
        await Run(Later);
        _safety.Throw = false;

        await Run(Later + TimeSpan.FromMinutes(5));

        Assert.Equal(WorkStatus.Published, (await Load(_host, id)).Status);
        Assert.Empty(await Flags(id));
    }

    [Fact]
    public async Task Restored_work_is_not_blocked_again_on_republish()
    {
        var (_, id) = await Pending(chapters: [Chapter(body: "[[unsafe]]")]);
        await Modify(_host, id, w => w.ModerationOverride = true);
        _safety.ImageSeverity = 6;

        await Run();

        Assert.Equal(WorkStatus.Published, (await Load(_host, id)).Status);
        Assert.Equal(0, _safety.ImageScans);
        Assert.Empty(await Flags(id));
    }

    [Fact]
    public async Task Concurrent_sweeps_scan_each_pending_work_once()
    {
        var (_, id) = await Pending();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Run()));

        Assert.Equal(WorkStatus.Published, (await Load(_host, id)).Status);
        Assert.Equal(1, _safety.ImageScans);
    }

    [Fact]
    public async Task Background_sweep_skips_works_pending_for_less_than_the_minimum_age()
    {
        var (_, id) = await Pending();

        await _pipeline.RunOnceAsync(ProfileTestHost.Now + TimeSpan.FromSeconds(2), CancellationToken.None, PublishPipeline.MinPendingAge);
        Assert.Equal(WorkStatus.Pending, (await Load(_host, id)).Status);
        await _pipeline.RunOnceAsync(ProfileTestHost.Now + TimeSpan.FromSeconds(6), CancellationToken.None, PublishPipeline.MinPendingAge);
        Assert.Equal(WorkStatus.Published, (await Load(_host, id)).Status);
    }

    [Fact]
    public async Task Non_pending_works_are_never_scanned()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var id = await ReadyDraft(_host, u);

        await Run();

        Assert.Equal(WorkStatus.Draft, (await Load(_host, id)).Status);
        Assert.Equal(0, _safety.ImageScans);
    }
}
