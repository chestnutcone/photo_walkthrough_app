using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Auth;
using Vela.Api.Data;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class UserStateTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync() { _s = await AdminSite.Create(pg); _admin = await _s.NewAdminUser(); }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private async Task SetState(Guid id, DateTimeOffset? suspended, DateTimeOffset? deleted)
    {
        await using var db = _s.Host.Db();
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, suspended).SetProperty(u => u.DeletedAt, deleted));
    }

    // /v1/me checks the suspension itself, so a neutral authenticated endpoint shows what the token check does.
    private const string Probe = "/v1/notifications/unread-count";

    [Fact]
    public async Task Suspending_makes_the_old_access_token_fail_at_once_with_account_suspended_and_reinstating_restores_it()
    {
        var user = await _s.NewUser();
        Assert.Equal(HttpStatusCode.OK, (await user.Get(Probe)).Status); // warms the cache

        await _s.Mod(m => m.SuspendUserAsync(_admin, user.Id, "abuse"));
        var blocked = await user.Get(Probe);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.Status);
        Assert.Equal("account_suspended", blocked.ErrorCode);

        await _s.Mod(m => m.ReinstateUserAsync(_admin, user.Id, "appeal"));
        Assert.Equal(HttpStatusCode.OK, (await user.Get(Probe)).Status);
    }

    [Fact]
    public async Task Requesting_account_deletion_makes_the_old_access_token_fail_with_account_deleted()
    {
        var user = await _s.NewUser();
        Assert.Equal(HttpStatusCode.OK, (await user.Get(Probe)).Status);
        Assert.Equal(HttpStatusCode.OK, (await user.Delete("/v1/me")).Status);
        var r = await user.Get(Probe);
        Assert.Equal(HttpStatusCode.Forbidden, r.Status);
        Assert.Equal("account_deleted", r.ErrorCode);
    }

    [Fact]
    public async Task A_change_made_behind_the_cache_is_seen_after_the_ttl()
    {
        var user = await _s.NewUser();
        Assert.Equal(HttpStatusCode.OK, (await user.Get(Probe)).Status);

        await SetState(user.Id, ProfileTestHost.Now, null); // another instance suspended the user: no eviction here
        Assert.Equal(HttpStatusCode.OK, (await user.Get(Probe)).Status);
        _s.Host.Clock.Advance(TimeSpan.FromSeconds(21));
        var r = await user.Get(Probe);
        Assert.Equal(HttpStatusCode.Forbidden, r.Status);
        Assert.Equal("account_suspended", r.ErrorCode);
    }

    [Fact]
    public async Task A_token_for_a_user_that_no_longer_exists_gets_401_after_the_ttl()
    {
        var user = await _s.NewUser();
        Assert.Equal(HttpStatusCode.OK, (await user.Get("/v1/me")).Status);
        await using (var db = _s.Host.Db()) await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        _s.Host.Clock.Advance(TimeSpan.FromSeconds(21));
        var r = await user.Get("/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, r.Status);
        Assert.Equal("unauthorized", r.ErrorCode);
    }

    [Fact]
    public async Task A_suspended_users_token_on_a_public_endpoint_is_treated_as_anonymous()
    {
        var (_, work) = await _s.Story();
        var user = await _s.NewUser();
        await SetState(user.Id, ProfileTestHost.Now, null);
        _s.Host.Clock.Advance(TimeSpan.FromSeconds(21));
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Get(Probe)).Status);
        var pub = await user.Get($"/v1/works/{work}");
        Assert.Equal(HttpStatusCode.OK, pub.Status);
    }

    [Fact]
    public async Task A_cache_hit_runs_no_query()
    {
        var user = await _s.NewUser();
        var cache = new UserStateCache(_s.Host.Clock, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        await using (var real = _s.Host.Db())
            Assert.Equal(UserState.Active, await cache.GetAsync(user.Id, real));
        var builder = new DbContextOptionsBuilder<VelaDbContext>();
        builder.ConfigureVela("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1");
        await using var broken = new VelaDbContext(builder.Options);
        Assert.Equal(UserState.Active, await cache.GetAsync(user.Id, broken)); // would throw if it queried
        cache.Evict(user.Id);
        await Assert.ThrowsAnyAsync<Exception>(() => cache.GetAsync(user.Id, broken));
    }
}
