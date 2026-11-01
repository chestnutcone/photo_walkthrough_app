using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Admin;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class AdminReconcileTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    public async Task InitializeAsync()
    {
        _s = await AdminSite.Create(pg, isolated: true);
        _s.Allow.Add("keeper@vela-admin.test"); // keeps the admin area switched on when the test delists its own admin
    }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private async Task<bool> IsAdmin(Guid id) => (await _s.LoadUser(id)).IsAdmin;
    private Task<int> Reconcile() => _s.Host.Services.GetRequiredService<AdminReconciler>().RunOnceAsync();

    [Fact]
    public async Task The_session_validator_clears_the_flag_when_the_email_left_the_allow_list()
    {
        var b = await _s.AdminBrowser();
        var id = await _s.AdminId();
        Assert.True(await IsAdmin(id));
        _s.Allow.Remove(_s.AdminEmail);

        Assert.NotEqual(HttpStatusCode.OK, (await b.Get("/admin")).Status);

        Assert.False(await IsAdmin(id));
    }

    [Fact]
    public async Task A_login_attempt_with_a_delisted_email_clears_the_flag()
    {
        await _s.AdminBrowser();
        var id = await _s.AdminId();
        _s.Allow.Remove(_s.AdminEmail);
        var b = _s.NewBrowser();
        await b.Post("/admin/login?handler=Verify", new Dictionary<string, string> { ["Email"] = _s.AdminEmail, ["Code"] = "123456" });
        Assert.False(await IsAdmin(id));
    }

    [Fact]
    public async Task Reconciliation_clears_every_admin_not_on_the_list_and_keeps_current_ones()
    {
        var keep = await _s.NewAdminUser();
        var gone = await _s.NewAdminUser();
        var noEmail = (await _s.NewUser()).Id;
        await Vela.Api.Tests.Works.WorkTestKit.MakeAdmin(_s.Host, noEmail);
        _s.Allow.Remove(await _s.EmailOf(gone));

        Assert.Equal(2, await Reconcile());

        Assert.True(await IsAdmin(keep));
        Assert.False(await IsAdmin(gone));
        Assert.False(await IsAdmin(noEmail));
        Assert.Equal(0, await Reconcile());
    }

    [Fact]
    public async Task After_reconciliation_the_ex_admin_loses_admin_visibility_of_blocked_works()
    {
        var (_, work) = await _s.BlockedStory();
        var admin = await _s.Host.SignInOnboarded(Vela.Api.Tests.Profiles.ProfileTestHost.UniqueHandle());
        var email = $"vis-{Guid.NewGuid():N}@vela-admin.test";
        await using (var db = _s.Host.Db())
        {
            db.Identities.Add(new Vela.Api.Data.Entities.Identity { Id = Guid.NewGuid(), UserId = admin.Id, Provider = "email", Subject = email, Email = email });
            await db.SaveChangesAsync();
        }
        _s.Allow.Add(email);
        await Vela.Api.Tests.Works.WorkTestKit.MakeAdmin(_s.Host, admin.Id);
        Assert.Equal(HttpStatusCode.OK, (await admin.Get($"/v1/works/{work}")).Status);

        _s.Allow.Remove(email);
        await Reconcile();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.Get($"/v1/works/{work}")).Status);
    }

    [Fact]
    public async Task An_ex_admin_can_be_banned_but_a_current_admin_cannot()
    {
        var actor = await _s.NewAdminUser();
        var current = await _s.NewAdminUser();
        var ex = await _s.NewAdminUser();
        _s.Allow.Remove(await _s.EmailOf(ex));

        Assert.Equal(ModerationOutcome.CannotBanAdmin, await _s.Mod(m => m.SuspendUserAsync(actor, current, "x")));
        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.SuspendUserAsync(actor, ex, "x")));
        Assert.NotNull((await _s.LoadUser(ex)).SuspendedAt);
    }
}
