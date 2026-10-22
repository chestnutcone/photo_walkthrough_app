using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vela.Api.Auth;
using Vela.Api.Auth.EmailCodes;
using Vela.Api.Auth.Identity;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Auth.EmailCodes;

[Collection(PostgresCollection.Name)]
public class EmailCodeServiceTests(PostgresFixture pg)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private const string Pepper = "unit-test-pepper-0123456789-abcdefghij";
    private const string ReviewCode = "424242";

    private sealed record Harness(EmailCodeService Svc, FakeEmailSender Outbox, MutableTimeProvider Clock, VelaDbContext Db);

    private Harness Create(EmailCodeSettings? settings = null, IEmailSender? sender = null)
    {
        var clock = new MutableTimeProvider(Start);
        var db = pg.NewContext();
        var outbox = new FakeEmailSender();
        var jwt = new JwtOptions { SigningKey = "unit-test-signing-key-0123456789-abcdef" };
        var svc = new EmailCodeService(db, sender ?? outbox, new AccountResolver(db, clock),
            new TokenService(db, Microsoft.Extensions.Options.Options.Create(jwt), clock), clock,
            settings ?? new EmailCodeSettings { Pepper = Pepper }, NullLogger<EmailCodeService>.Instance);
        return new Harness(svc, outbox, clock, db);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";
    private static string NewIp() => $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}";
    private static string CodeFrom(FakeEmailSender outbox) =>
        Regex.Match(outbox.Sent.Last().TextBody, @"\b\d{6}\b").Value;
    private static string WrongCode(string real) => real == "000000" ? "000001" : "000000";

    [Fact]
    public async Task Request_sends_a_six_digit_code_and_stores_only_its_hash()
    {
        var h = Create();
        var email = NewEmail();

        await h.Svc.RequestAsync(email, NewIp());

        var mail = Assert.Single(h.Outbox.Sent);
        Assert.Equal(email, mail.To);
        Assert.DoesNotContain("<", mail.TextBody);
        Assert.Contains("10 minutes", mail.TextBody);
        var code = CodeFrom(h.Outbox);
        Assert.Matches(@"^\d{6}$", code);
        var row = await h.Db.EmailCodes.SingleAsync(c => c.Email == email);
        Assert.NotEqual(code, row.CodeHash);
        Assert.DoesNotContain(code, row.CodeHash);
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Pepper), Encoding.UTF8.GetBytes($"{email}:{code}")));
        Assert.Equal(expected, row.CodeHash);
        Assert.Equal(Start.AddMinutes(10), row.ExpiresAt);
    }

    [Fact]
    public async Task Correct_code_signs_in_and_creates_user()
    {
        var h = Create();
        var email = NewEmail();
        await h.Svc.RequestAsync("  " + email.ToUpperInvariant() + " ", NewIp());

        var result = await h.Svc.VerifyAsync(email, CodeFrom(h.Outbox), NewIp());

        Assert.False(string.IsNullOrEmpty(result.Pair.AccessToken));
        Assert.True(result.IsNew);
        var identity = await h.Db.Identities.SingleAsync(i => i.Subject == email);
        Assert.Equal("email", identity.Provider);
        Assert.True(await h.Db.Users.AnyAsync(u => u.Id == identity.UserId));

        await h.Svc.RequestAsync(email, NewIp());
        var again = await h.Svc.VerifyAsync(email, CodeFrom(h.Outbox), NewIp());
        Assert.False(again.IsNew);
    }

    [Fact]
    public async Task Code_expires_after_10_minutes()
    {
        var h = Create();
        var email = NewEmail();
        await h.Svc.RequestAsync(email, NewIp());
        var code = CodeFrom(h.Outbox);

        h.Clock.Advance(TimeSpan.FromMinutes(10));

        await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, code, NewIp()));
    }

    [Fact]
    public async Task Code_is_valid_just_before_expiry()
    {
        var h = Create();
        var email = NewEmail();
        await h.Svc.RequestAsync(email, NewIp());
        h.Clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));

        await h.Svc.VerifyAsync(email, CodeFrom(h.Outbox), NewIp());
    }

    [Fact]
    public async Task Sixth_attempt_fails_even_with_correct_code()
    {
        var h = Create();
        var email = NewEmail();
        await h.Svc.RequestAsync(email, NewIp());
        var code = CodeFrom(h.Outbox);

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, WrongCode(code), NewIp()));

        await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, code, NewIp()));
        Assert.False(await h.Db.Identities.AnyAsync(i => i.Subject == email));
    }

    [Fact]
    public async Task Fifth_attempt_with_correct_code_still_succeeds()
    {
        var h = Create();
        var email = NewEmail();
        await h.Svc.RequestAsync(email, NewIp());
        var code = CodeFrom(h.Outbox);
        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, WrongCode(code), NewIp()));

        await h.Svc.VerifyAsync(email, code, NewIp());
    }

    [Fact]
    public async Task Used_code_cannot_be_replayed()
    {
        var h = Create();
        var email = NewEmail();
        await h.Svc.RequestAsync(email, NewIp());
        var code = CodeFrom(h.Outbox);
        await h.Svc.VerifyAsync(email, code, NewIp());

        await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, code, NewIp()));
    }

    [Fact]
    public async Task Concurrent_verifies_of_the_same_code_succeed_exactly_once()
    {
        var h = Create();
        var email = NewEmail();
        await h.Svc.RequestAsync(email, NewIp());
        var code = CodeFrom(h.Outbox);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            var other = Create();
            try { await other.Svc.VerifyAsync(email, code, NewIp()); return true; }
            catch (InvalidCodeException) { return false; }
        }));

        Assert.Equal(1, results.Count(r => r));
    }

    [Fact]
    public async Task New_request_invalidates_previous_code()
    {
        var h = Create();
        var email = NewEmail();
        await h.Svc.RequestAsync(email, NewIp());
        var first = CodeFrom(h.Outbox);
        await h.Svc.RequestAsync(email, NewIp());
        var second = CodeFrom(h.Outbox);
        if (first == second) return; // 1-in-a-million collision: nothing to distinguish

        await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, first, NewIp()));
        await h.Svc.VerifyAsync(email, second, NewIp());
    }

    [Fact]
    public async Task Unknown_email_fails_with_the_same_error()
    {
        var h = Create();
        await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(NewEmail(), "123456", NewIp()));
    }

    [Fact]
    public async Task Rate_limit_blocks_requests_per_email_and_per_ip()
    {
        var h = Create();
        var email = NewEmail();
        var ip = NewIp();
        for (var i = 0; i < 5; i++) await h.Svc.RequestAsync(email, ip);

        var byEmail = await Assert.ThrowsAsync<RateLimitedException>(() => h.Svc.RequestAsync(email, NewIp()));
        Assert.InRange(byEmail.RetryAfter, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1));

        var ip2 = NewIp();
        for (var i = 0; i < 20; i++) await h.Svc.RequestAsync(NewEmail(), ip2);
        await Assert.ThrowsAsync<RateLimitedException>(() => h.Svc.RequestAsync(NewEmail(), ip2));

        h.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        await h.Svc.RequestAsync(email, ip);
        await h.Svc.RequestAsync(NewEmail(), ip2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("a@@b.test")]
    [InlineData("a@b@c.test")]
    [InlineData("@b.test")]
    [InlineData("a@")]
    public async Task Invalid_email_is_rejected(string email)
    {
        var h = Create();
        await Assert.ThrowsAsync<InvalidEmailException>(() => h.Svc.RequestAsync(email, NewIp()));
        Assert.Empty(h.Outbox.Sent);
    }

    [Fact]
    public async Task Email_longer_than_254_chars_is_rejected()
    {
        var h = Create();
        var email = new string('a', 250) + "@b.test";
        await Assert.ThrowsAsync<InvalidEmailException>(() => h.Svc.RequestAsync(email, NewIp()));
    }

    [Fact]
    public async Task Review_mode_off_rejects_fixed_code()
    {
        var email = NewEmail();
        var h = Create(new EmailCodeSettings { Pepper = Pepper, ReviewMode = false, ReviewEmails = [email], ReviewCode = ReviewCode });
        await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, ReviewCode, NewIp()));
        await h.Svc.RequestAsync(email, NewIp());
        Assert.Single(h.Outbox.Sent); // review email list is ignored when review mode is off
    }

    [Fact]
    public async Task Review_mode_on_accepts_fixed_code_for_allow_listed_email_without_sending()
    {
        var email = NewEmail();
        var h = Create(new EmailCodeSettings { Pepper = Pepper, ReviewMode = true, ReviewEmails = [email.ToUpperInvariant()], ReviewCode = ReviewCode });

        await h.Svc.RequestAsync(email, NewIp());
        var result = await h.Svc.VerifyAsync(email, ReviewCode, NewIp());

        Assert.Empty(h.Outbox.Sent);
        Assert.True(result.IsNew);
        await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, "111111", NewIp()));
        await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(NewEmail(), ReviewCode, NewIp()));
    }

    [Fact]
    public async Task Review_mode_on_still_sends_real_codes_to_other_emails()
    {
        var h = Create(new EmailCodeSettings { Pepper = Pepper, ReviewMode = true, ReviewEmails = [NewEmail()], ReviewCode = ReviewCode });
        await h.Svc.RequestAsync(NewEmail(), NewIp());
        Assert.Single(h.Outbox.Sent);
    }

    private sealed class ThrowingSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string textBody) => throw new InvalidOperationException("smtp down " + to);
    }

    [Fact]
    public async Task Sender_failure_does_not_surface_from_request()
    {
        var h = Create(sender: new ThrowingSender());
        await h.Svc.RequestAsync(NewEmail(), NewIp()); // must not throw
    }

    [Fact]
    public async Task Request_for_existing_and_new_accounts_behaves_identically()
    {
        var h = Create();
        var known = NewEmail();
        await h.Svc.RequestAsync(known, NewIp());
        await h.Svc.VerifyAsync(known, CodeFrom(h.Outbox), NewIp());

        var before = h.Outbox.Sent.Count;
        await h.Svc.RequestAsync(known, NewIp());
        await h.Svc.RequestAsync(NewEmail(), NewIp());

        Assert.Equal(before + 2, h.Outbox.Sent.Count);
        Assert.Equal(h.Outbox.Sent[^1].Subject, h.Outbox.Sent[^2].Subject);
    }

    [Fact]
    public async Task Suspended_account_fails_verify_with_account_suspended()
    {
        var h = Create();
        var email = NewEmail();
        await h.Svc.RequestAsync(email, NewIp());
        await h.Svc.VerifyAsync(email, CodeFrom(h.Outbox), NewIp());
        var uid = (await h.Db.Identities.SingleAsync(i => i.Subject == email)).UserId;
        await h.Db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, Start));

        await h.Svc.RequestAsync(email, NewIp());
        await Assert.ThrowsAsync<AccountSuspendedException>(() => h.Svc.VerifyAsync(email, CodeFrom(h.Outbox), NewIp()));
    }

    private Harness CreateReview(params string[] emails) =>
        Create(new EmailCodeSettings { Pepper = Pepper, ReviewMode = true, ReviewEmails = emails, ReviewCode = ReviewCode });

    [Fact]
    public async Task Review_code_is_locked_after_five_wrong_guesses_even_for_the_correct_code()
    {
        var email = NewEmail();
        var h = CreateReview(email);
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, "111111", NewIp()));

        await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, ReviewCode, NewIp()));
    }

    [Fact]
    public async Task Review_code_works_after_four_wrong_guesses()
    {
        var email = NewEmail();
        var h = CreateReview(email);
        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, "111111", NewIp()));

        await h.Svc.VerifyAsync(email, ReviewCode, NewIp());
    }

    [Fact]
    public async Task Review_lock_expires_after_the_window()
    {
        var email = NewEmail();
        var h = CreateReview(email);
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(email, "111111", NewIp()));

        h.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        await h.Svc.VerifyAsync(email, ReviewCode, NewIp());
    }

    [Fact]
    public async Task Review_lock_is_per_email()
    {
        var a = NewEmail();
        var b = NewEmail();
        var h = CreateReview(a, b);
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(a, "111111", NewIp()));

        await h.Svc.VerifyAsync(b, ReviewCode, NewIp());
    }

    [Fact]
    public async Task Concurrent_wrong_review_guesses_cannot_exceed_the_limit()
    {
        var email = NewEmail();
        var settings = new EmailCodeSettings { Pepper = Pepper, ReviewMode = true, ReviewEmails = [email], ReviewCode = ReviewCode };

        await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            await Assert.ThrowsAsync<InvalidCodeException>(() => Create(settings).Svc.VerifyAsync(email, "111111", NewIp()))));

        await using var db = pg.NewContext();
        Assert.Equal(5, await db.VerifyAttempts.CountAsync(a => a.Email == email && a.Kind == VerifyAttempt.ReviewFailure));
        await Assert.ThrowsAsync<InvalidCodeException>(() => Create(settings).Svc.VerifyAsync(email, ReviewCode, NewIp()));
    }

    [Fact]
    public async Task Verify_is_limited_per_ip_and_reports_exact_retry_after()
    {
        var h = Create();
        var ip = NewIp();
        for (var i = 0; i < 30; i++)
            await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(NewEmail(), "123456", ip));
        h.Clock.Advance(TimeSpan.FromMinutes(10));

        var ex = await Assert.ThrowsAsync<RateLimitedException>(() => h.Svc.VerifyAsync(NewEmail(), "123456", ip));

        Assert.Equal(TimeSpan.FromMinutes(50), ex.RetryAfter);
        h.Clock.Advance(TimeSpan.FromMinutes(50) + TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<InvalidCodeException>(() => h.Svc.VerifyAsync(NewEmail(), "123456", ip));
    }
}
