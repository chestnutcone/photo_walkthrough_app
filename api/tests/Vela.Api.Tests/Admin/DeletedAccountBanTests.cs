using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Account;
using Vela.Api.Admin;
using Vela.Api.Tests.Account;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

/// <summary>
/// FINALAPI I3: the abuser path is "post illegal content, get reported, press Delete account". While the hard delete is deferred
/// for review the identities still exist, so the admin must still be able to ban them; once the job runs they are gone.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DeletedAccountBanTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync() { _s = await AdminSite.Create(pg); _admin = await _s.NewAdminUser(); }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private async Task<AccountDeletionStatus> RequestDeletion(Guid userId)
    {
        using var scope = _s.Host.Scope();
        return await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().RequestAsync(userId);
    }

    private async Task<(string Provider, string Subject)> IdentityOf(Guid userId)
    {
        await using var db = _s.Host.Db();
        var i = await db.Identities.AsNoTracking().Where(x => x.UserId == userId).Select(x => new { x.Provider, x.Subject }).FirstAsync();
        return (i.Provider, i.Subject);
    }

    [Fact]
    public async Task Delete_while_under_review_then_mark_illegal_bans_the_identity_and_blocks_sign_up_after_the_hard_delete()
    {
        var (owner, work) = await _s.Story();
        var (provider, subject) = await IdentityOf(owner.Id);
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        Assert.Equal(AccountDeletionStates.DeferredReview, (await RequestDeletion(owner.Id)).State);

        var result = await _s.MarkIllegal(_admin, report);

        Assert.Equal(ModerationOutcome.Ok, result.Outcome);
        Assert.Equal(OwnerBan.Banned, result.OwnerBan);
        var user = await _s.LoadUser(owner.Id);
        Assert.NotNull(user.SuspendedAt);
        Assert.NotNull(user.DeletedAt); // still a deletion: the hard delete goes ahead later
        await using (var db = _s.Host.Db()) Assert.True(await db.BannedIdentities.AnyAsync(b => b.UserRef == owner.Id && b.Provider == provider));

        await AccountKit.RunJob(_s.Host, owner.Id);
        await using (var db = _s.Host.Db())
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == owner.Id));
            Assert.False(await db.Identities.AnyAsync(i => i.Subject == subject));
        }

        var sso = await _s.Host.Anonymous().PostAsJsonAsync("/v1/auth/sso", new { provider, idToken = $"{provider}:{subject}", nonce = "n" });
        Assert.Equal(HttpStatusCode.Forbidden, sso.StatusCode);
        var body = await sso.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("account_suspended", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_account_pending_deletion_can_be_banned_from_the_user_page_action()
    {
        var target = await _s.NewUser();
        await _s.Report(await _s.NewUser(), "user", target.Id, "harassment");
        await RequestDeletion(target.Id);

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.SuspendUserAsync(_admin, target.Id, "ban evasion")));

        await using var db = _s.Host.Db();
        Assert.True(await db.BannedIdentities.AnyAsync(b => b.UserRef == target.Id));
        Assert.Equal(1, await db.AdminActions.CountAsync(a => a.TargetId == target.Id && a.Action == "ban_user"));
    }

    [Fact]
    public async Task Mark_illegal_reports_an_owner_already_banned_as_such_instead_of_banning_again()
    {
        var (owner, work) = await _s.Story();
        await _s.Mod(m => m.SuspendUserAsync(_admin, owner.Id, "earlier ban"));

        var result = await _s.MarkIllegal(_admin, await _s.Report(await _s.NewUser(), "work", work, "illegal_content"));

        Assert.Equal(OwnerBan.AlreadyBanned, result.OwnerBan);
        Assert.False(result.BanRefused);
    }

    [Fact]
    public async Task Mark_illegal_reports_a_refused_ban_for_an_admin_owner()
    {
        var (owner, work) = await _s.Story();
        var email = $"adm-{Guid.NewGuid():N}@vela-admin.test";
        await using (var db = _s.Host.Db())
        {
            db.Identities.Add(new Vela.Api.Data.Entities.Identity { Id = Guid.NewGuid(), UserId = owner.Id, Provider = "email", Subject = email, Email = email });
            await db.SaveChangesAsync();
        }
        _s.Allow.Add(email);
        await Vela.Api.Tests.Works.WorkTestKit.MakeAdmin(_s.Host, owner.Id);

        var result = await _s.MarkIllegal(_admin, await _s.Report(await _s.NewUser(), "work", work, "illegal_content"));

        Assert.Equal(OwnerBan.Refused, result.OwnerBan);
        Assert.True(result.BanRefused);
    }

    [Theory]
    [InlineData(OwnerBan.Banned, "ok")]
    [InlineData(OwnerBan.AlreadyBanned, "ok_already_banned")]
    [InlineData(OwnerBan.Refused, "ban_refused")]
    [InlineData(OwnerBan.OwnerGone, "owner_gone")]
    public void Every_owner_ban_outcome_has_its_own_flash(OwnerBan ban, string flash)
    {
        Assert.Equal(flash, Vela.Api.Admin.Pages.ReportModel.MarkIllegalFlash(ban));
        Assert.True(Vela.Api.Admin.Pages.AdminPageModel.Flashes.ContainsKey(flash));
    }
}
