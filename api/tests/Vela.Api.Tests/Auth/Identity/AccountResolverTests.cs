using Microsoft.EntityFrameworkCore;
using Vela.Api.Auth.Identity;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Auth.Identity;

[Collection(PostgresCollection.Name)]
public class AccountResolverTests(PostgresFixture pg)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static string Sub() => "s-" + Guid.NewGuid();

    private AccountResolver Create(out Vela.Api.Data.VelaDbContext db)
    {
        db = pg.NewContext();
        return new AccountResolver(db, new MutableTimeProvider(Now));
    }

    [Fact]
    public async Task Resolver_creates_user_on_first_sign_in_and_reuses_it_on_second()
    {
        var sub = Sub();
        var id = new VerifiedIdentity("apple", sub, "a@x.test", true);

        var first = await Create(out _).SignInAsync(id);
        var second = await Create(out var db).SignInAsync(id);

        Assert.True(first.IsNew);
        Assert.False(second.IsNew);
        Assert.Equal(first.UserId, second.UserId);
        var user = await db.Users.SingleAsync(u => u.Id == first.UserId);
        Assert.Equal(Tier.FreemiumId, user.TierId);
        Assert.Null(user.AgeConfirmedAt);
        Assert.Null(user.TermsAcceptedAt);
        Assert.Equal(Now, user.CreatedAt);
        Assert.False(await db.Profiles.AnyAsync(p => p.UserId == first.UserId));
        Assert.Equal("a@x.test", (await db.Identities.SingleAsync(i => i.Subject == sub)).Email);
    }

    [Fact]
    public async Task Same_email_via_different_providers_creates_two_users()
    {
        var sub = Sub();
        var a = await Create(out _).SignInAsync(new VerifiedIdentity("apple", sub, "same@x.test", true));
        var g = await Create(out _).SignInAsync(new VerifiedIdentity("google", sub, "same@x.test", true));
        Assert.NotEqual(a.UserId, g.UserId);
    }

    [Fact]
    public async Task Same_email_with_different_subject_is_not_matched()
    {
        var a = await Create(out _).SignInAsync(new VerifiedIdentity("apple", Sub(), "same@x.test", true));
        var b = await Create(out _).SignInAsync(new VerifiedIdentity("apple", Sub(), "same@x.test", true));
        Assert.NotEqual(a.UserId, b.UserId);
    }

    [Fact]
    public async Task Suspended_user_is_refused()
    {
        var id = new VerifiedIdentity("fake", Sub(), null, false);
        var r = await Create(out _).SignInAsync(id);
        await using (var db = pg.NewContext())
            await db.Users.Where(u => u.Id == r.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, Now));
        await Assert.ThrowsAsync<AccountSuspendedException>(() => Create(out _).SignInAsync(id));
    }

    [Fact]
    public async Task Soft_deleted_user_is_refused_and_not_recreated()
    {
        var id = new VerifiedIdentity("fake", Sub(), null, false);
        var r = await Create(out _).SignInAsync(id);
        await using (var db = pg.NewContext())
            await db.Users.Where(u => u.Id == r.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, Now));
        await Assert.ThrowsAsync<AccountDeletedException>(() => Create(out _).SignInAsync(id));
    }

    [Fact]
    public async Task Hard_deleted_user_signs_in_as_a_new_user()
    {
        var id = new VerifiedIdentity("fake", Sub(), null, false);
        var r = await Create(out _).SignInAsync(id);
        await using (var db = pg.NewContext())
            await db.Users.Where(u => u.Id == r.UserId).ExecuteDeleteAsync();
        var again = await Create(out _).SignInAsync(id);
        Assert.True(again.IsNew);
        Assert.NotEqual(r.UserId, again.UserId);
    }

    [Fact]
    public async Task Concurrent_first_sign_ins_create_exactly_one_user()
    {
        var id = new VerifiedIdentity("fake", Sub(), null, false);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Create(out var _db).SignInAsync(id)));
        Assert.Single(results.Select(r => r.UserId).Distinct());
        Assert.Single(results, r => r.IsNew);
    }
}
