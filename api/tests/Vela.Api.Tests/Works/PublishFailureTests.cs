using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Moderation;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Works;

public sealed class PoisonHandler : IDomainEventHandler<WorkPublished>
{
    public static readonly HashSet<Guid> Poison = [];
    public Task HandleAsync(WorkPublished evt, CancellationToken ct) =>
        Poison.Contains(evt.WorkId) ? throw new InvalidOperationException("poison work") : Task.CompletedTask;
}

[Collection(PostgresCollection.Name)]
public class PublishFailureTests : IDisposable
{
    private readonly ProfileTestHost _host;
    private readonly PublishPipeline _pipeline;

    public PublishFailureTests(PostgresFixture pg)
    {
        _host = new ProfileTestHost(pg, null, null, s => s.AddScoped<IDomainEventHandler<WorkPublished>, PoisonHandler>());
        _pipeline = _host.Services.GetRequiredService<PublishPipeline>();
        PoisonHandler.Poison.Clear();
    }

    public void Dispose() { PoisonHandler.Poison.Clear(); _host.Dispose(); }

    private async Task<Guid> Pending()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var id = await ReadyDraft(_host, u);
        Assert.True((await u.Post($"/v1/works/{id}/publish", new { })).Body.GetProperty("success").GetBoolean());
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        return id;
    }

    [Fact]
    public async Task One_poison_work_does_not_starve_the_queue_and_is_retried_after_the_gap()
    {
        var poison = await Pending();      // oldest, so it is claimed first
        var healthy = await Pending();
        PoisonHandler.Poison.Add(poison);
        var now = ProfileTestHost.Now + TimeSpan.FromMinutes(1);

        await _pipeline.RunOnceAsync(now, CancellationToken.None);

        var bad = await Load(_host, poison);
        Assert.Equal(WorkStatus.Pending, bad.Status);
        Assert.Null(bad.PublishedAt);
        Assert.Equal(1, bad.PublishFailures);
        Assert.Equal(WorkStatus.Published, (await Load(_host, healthy)).Status);

        await _pipeline.RunOnceAsync(now.AddSeconds(30), CancellationToken.None);
        Assert.Equal(1, (await Load(_host, poison)).PublishFailures); // not retried inside the gap

        PoisonHandler.Poison.Clear();
        await _pipeline.RunOnceAsync(now + PublishPipeline.RetryGap + TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(WorkStatus.Published, (await Load(_host, poison)).Status);
    }
}
