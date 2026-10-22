using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class LifecycleTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync() { _s = await AdminSite.Create(pg); _admin = await _s.NewAdminUser(); }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private async Task Lifecycle(Func<IWorkVariantLifecycle, Task> call)
    {
        using var scope = _s.Host.Scope();
        await call(scope.ServiceProvider.GetRequiredService<IWorkVariantLifecycle>());
    }

    [Fact]
    public async Task Take_down_deletes_the_public_variants_keeps_the_original_and_purges_variants_and_the_share_page()
    {
        var (_, work) = await _s.Story();
        Assert.All(Variants(work), v => Assert.True(_s.Host.Blobs.Exists(v)));

        await Lifecycle(l => l.TakeDownAsync(work));

        Assert.All(Variants(work), v => Assert.False(_s.Host.Blobs.Exists(v)));
        Assert.True(_s.Host.Blobs.Exists(BlobNames.WorkOriginal(work)));
        Assert.All(Variants(work), v => Assert.Contains($"https://cdn.test/{v}", _s.Cdn.Purged));
        Assert.Contains($"/s/{work}", _s.Cdn.Purged);
    }

    [Fact]
    public async Task Restore_regenerates_the_variants_from_the_original_and_records_the_dimensions()
    {
        var (_, work) = await _s.Story();
        await Lifecycle(l => l.TakeDownAsync(work));
        await WorkTestKit.Modify(_s.Host, work, w => { w.Width = 0; w.Height = 0; });

        await Lifecycle(l => l.RestoreAsync(work));

        Assert.All(Variants(work), v => Assert.True(_s.Host.Blobs.Exists(v)));
        var w = await WorkTestKit.Load(_s.Host, work);
        Assert.Equal((400, 300), (w.Width, w.Height));
    }

    [Fact]
    public async Task Restore_throws_when_the_original_is_missing()
    {
        var (_, work) = await _s.Story();
        await Lifecycle(l => l.TakeDownAsync(work));
        await _s.Host.Blobs.DeletePrefixAsync(BlobNames.WorkOriginal(work));
        await Assert.ThrowsAnyAsync<Exception>(() => Lifecycle(l => l.RestoreAsync(work)));
    }

    [Fact]
    public async Task The_pipeline_takes_down_the_variants_of_an_auto_blocked_work()
    {
        var (_, work) = await _s.BlockedStory();
        Assert.Equal(WorkStatus.Blocked, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.All(Variants(work), v => Assert.False(_s.Host.Blobs.Exists(v)));
        Assert.True(_s.Host.Blobs.Exists(BlobNames.WorkOriginal(work)));
        Assert.Contains($"/s/{work}", _s.Cdn.Purged);
    }

    [Fact]
    public async Task Admin_removal_takes_the_variants_down_and_the_share_page_and_api_stop_serving_the_work()
    {
        var (owner, work) = await _s.Story();
        var stranger = await _s.NewUser();
        Assert.Equal(HttpStatusCode.OK, (await stranger.Get($"/v1/works/{work}")).Status);

        await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "not allowed"));

        Assert.All(Variants(work), v => Assert.False(_s.Host.Blobs.Exists(v)));
        Assert.Contains($"/s/{work}", _s.Cdn.Purged);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Get($"/v1/works/{work}")).Status);
        // The owner still sees the work, without image links that would now be dead.
        var mine = await owner.Get($"/v1/works/{work}");
        Assert.Equal(HttpStatusCode.OK, mine.Status);
        Assert.Equal("removed", mine.Data.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, mine.Data.GetProperty("imageUrl").ValueKind);
        var share = await _s.Host.Anonymous().GetAsync($"/s/{work}");
        Assert.Equal(HttpStatusCode.NotFound, share.StatusCode);
    }

    [Fact]
    public async Task Admin_view_of_a_blocked_work_tolerates_missing_variants()
    {
        var (_, work) = await _s.BlockedStory();
        var admin = await _s.NewUser();
        await WorkTestKit.MakeAdmin(_s.Host, admin.Id);
        var r = await admin.Get($"/v1/works/{work}");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal("blocked", r.Data.GetProperty("status").GetString());
    }
}
