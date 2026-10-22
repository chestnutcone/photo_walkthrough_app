using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Account;
using Vela.Api.Admin;
using Vela.Api.Auth;
using Vela.Api.Auth.Identity;
using Vela.Api.Tests.Account;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class BanTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync() { _s = await AdminSite.Create(pg); _admin = await _s.NewAdminUser(); }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private static string Expected(string provider, string subject) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(AdminSite.Pepper), Encoding.UTF8.GetBytes(provider + ":" + subject)));

    private async Task<(Guid UserId, string Subject)> Account(string provider, string subject, string? email = null)
    {
        using var scope = _s.Host.Scope();
        var (id, _) = await scope.ServiceProvider.GetRequiredService<IAccountResolver>().SignInAsync(new VerifiedIdentity(provider, subject, email, email is not null));
        return (id, subject);
    }

    [Fact]
    public async Task Suspending_revokes_every_token_at_once_and_sign_in_returns_account_suspended()
    {
        var user = await _s.NewUser();
        using (var scope = _s.Host.Scope())
        {
            var pair = await scope.ServiceProvider.GetRequiredService<ITokenService>().IssueAsync(user.Id);
            Assert.Equal(HttpStatusCode.OK, (await _s.Host.Anonymous().PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = pair.RefreshToken })).StatusCode);
        }
        await using (var db = _s.Host.Db()) Assert.Contains(await db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync(), t => t.RevokedAt is null);

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.SuspendUserAsync(_admin, user.Id, "harassment")));

        Assert.NotNull((await _s.LoadUser(user.Id)).SuspendedAt);
        await using var db2 = _s.Host.Db();
        Assert.All(await db2.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync(), t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task Suspension_stores_only_hashes_for_every_identity_of_the_user()
    {
        var (userId, sub) = await Account("fake", "sub-" + Guid.NewGuid(), "ban.me@example.test");
        await using (var db = _s.Host.Db())
        {
            db.Identities.Add(new Vela.Api.Data.Entities.Identity { Id = Guid.NewGuid(), UserId = userId, Provider = "email", Subject = "ban.me@example.test", Email = "ban.me@example.test" });
            await db.SaveChangesAsync();
        }

        await _s.Mod(m => m.SuspendUserAsync(_admin, userId, "spam ring"));

        await using var db2 = _s.Host.Db();
        var rows = await db2.BannedIdentities.AsNoTracking().Where(b => b.UserRef == userId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Provider == "fake" && r.SubjectHash == Expected("fake", sub));
        Assert.Contains(rows, r => r.Provider == "email" && r.SubjectHash == Expected("email", "ban.me@example.test"));
        Assert.All(rows, r => Assert.Equal(64, r.SubjectHash.Length));
        var dump = string.Join("|", (await AccountKit.Query(_s.ConnectionString ?? _s.Host.Db().Database.GetConnectionString()!, "SELECT * FROM banned_identities")).SelectMany(x => x));
        Assert.DoesNotContain(sub, dump);
        Assert.DoesNotContain("ban.me", dump);
    }

    [Fact]
    public async Task Suspending_twice_is_harmless()
    {
        var (userId, _) = await Account("fake", "twice-" + Guid.NewGuid());
        await _s.Mod(m => m.SuspendUserAsync(_admin, userId, "one"));
        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.SuspendUserAsync(_admin, userId, "two")));
        await using var db = _s.Host.Db();
        Assert.Equal(1, await db.BannedIdentities.CountAsync(b => b.UserRef == userId));
    }

    [Fact]
    public async Task Suspending_resolves_pending_reports_about_that_user()
    {
        var target = await _s.NewUser();
        var report = await _s.Report(await _s.NewUser(), "user", target.Id);
        await _s.Mod(m => m.SuspendUserAsync(_admin, target.Id, "abusive"));
        Assert.Equal("actioned", (await _s.LoadReport(report)).Status);
    }

    [Theory]
    [InlineData("google")]
    [InlineData("apple")]
    [InlineData("email")]
    [InlineData("fake")]
    public async Task A_banned_identity_cannot_sign_up_again_even_after_the_account_is_deleted_and_can_after_reinstate(string provider)
    {
        var subject = provider == "email" ? $"gone-{Guid.NewGuid():N}@example.test" : "sub-" + Guid.NewGuid();
        var (userId, _) = await Account(provider, subject, provider == "email" ? subject : null);
        await _s.Mod(m => m.SuspendUserAsync(_admin, userId, "evasion"));
        // The person deletes the account to start over (the job needs the deletion mark).
        await using (var db = _s.Host.Db())
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(x => x.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        await AccountKit.RunJob(_s.Host, userId);
        await using (var db = _s.Host.Db()) Assert.False(await db.Users.AnyAsync(u => u.Id == userId));

        using (var scope = _s.Host.Scope())
            await Assert.ThrowsAsync<AccountSuspendedException>(() => scope.ServiceProvider.GetRequiredService<IAccountResolver>()
                .SignInAsync(new VerifiedIdentity(provider, subject, null, false)));
        await using (var db = _s.Host.Db()) Assert.False(await db.Identities.AnyAsync(i => i.Subject == subject));

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.ReinstateUserAsync(_admin, userId, "appeal accepted")));

        var (fresh, _) = await Account(provider, subject);
        Assert.NotEqual(userId, fresh);
    }

    [Fact]
    public async Task The_sso_endpoint_and_the_email_code_flow_answer_403_account_suspended_for_a_banned_identity()
    {
        var email = $"evader-{Guid.NewGuid():N}@example.test";
        var (userId, sub) = await Account("fake", "sso-" + Guid.NewGuid());
        await _s.Mod(m => m.SuspendUserAsync(_admin, userId, "x"));
        await using (var db = _s.Host.Db()) await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(x => x.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        await AccountKit.RunJob(_s.Host, userId);

        var api = _s.Host.Anonymous();
        var sso = await api.PostAsJsonAsync("/v1/auth/sso", new { provider = "fake", idToken = $"fake:{sub}", nonce = "n" });
        Assert.Equal(HttpStatusCode.Forbidden, sso.StatusCode);
        var body = await sso.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("account_suspended", body.GetProperty("error").GetProperty("code").GetString());
        var suspendedMessage = body.GetProperty("error").GetProperty("message").GetString();

        // Email: a banned address, via the real code flow.
        var (emailUser, _) = await Account("email", email, email);
        await _s.Mod(m => m.SuspendUserAsync(_admin, emailUser, "x"));
        await using (var db = _s.Host.Db()) await db.Users.Where(u => u.Id == emailUser).ExecuteUpdateAsync(x => x.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        await AccountKit.RunJob(_s.Host, emailUser);
        await api.PostAsJsonAsync("/v1/auth/email/request", new { email });
        var code = _s.NewBrowser().LatestCode(email);
        var verify = await api.PostAsJsonAsync("/v1/auth/email/verify", new { email, code });
        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        var vb = await verify.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("account_suspended", vb.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(suspendedMessage, vb.GetProperty("error").GetProperty("message").GetString());
        await using var db2 = _s.Host.Db();
        Assert.False(await db2.Identities.AnyAsync(i => i.Subject == email));
    }

    [Fact]
    public async Task Reinstating_clears_suspension_and_removes_that_users_ban_rows_only()
    {
        var (a, _) = await Account("fake", "a-" + Guid.NewGuid());
        var (b, _) = await Account("fake", "b-" + Guid.NewGuid());
        await _s.Mod(m => m.SuspendUserAsync(_admin, a, "x"));
        await _s.Mod(m => m.SuspendUserAsync(_admin, b, "x"));

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.ReinstateUserAsync(_admin, a, "appeal")));

        Assert.Null((await _s.LoadUser(a)).SuspendedAt);
        Assert.NotNull((await _s.LoadUser(b)).SuspendedAt);
        await using var db = _s.Host.Db();
        Assert.Equal(0, await db.BannedIdentities.CountAsync(x => x.UserRef == a));
        Assert.Equal(1, await db.BannedIdentities.CountAsync(x => x.UserRef == b));
    }

    [Fact]
    public async Task Ban_rows_and_audit_rows_outlive_a_deleted_banned_account()
    {
        var (userId, _) = await Account("fake", "keep-" + Guid.NewGuid());
        await _s.Mod(m => m.SuspendUserAsync(_admin, userId, "x"));
        await using (var db = _s.Host.Db()) await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(x => x.SetProperty(u => u.DeletedAt, ProfileTestHost.Now));
        await AccountKit.RunJob(_s.Host, userId);

        await using var db2 = _s.Host.Db();
        Assert.Equal(1, await db2.BannedIdentities.CountAsync(b => b.UserRef == userId));
        Assert.Equal(1, await db2.AdminActions.CountAsync(a => a.TargetId == userId));
    }
}
