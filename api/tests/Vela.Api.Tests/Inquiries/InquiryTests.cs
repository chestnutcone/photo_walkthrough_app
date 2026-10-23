using Reply = Vela.Api.Tests.Profiles.Reply;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Common;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Notifications.NotificationKit;

namespace Vela.Api.Tests.Inquiries;

public sealed class ThrowingEmailSender : IEmailSender
{
    public Task SendAsync(string to, string subject, string textBody) => throw new InvalidOperationException("smtp down: " + to);
}

public sealed class ProbeEmailSender(Func<Task> onSend) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string textBody) => await onSend();
}

[Collection(PostgresCollection.Name)]
public class InquiryTests(PostgresFixture pg) : IDisposable
{
    private const string ArtistLogin = "artist.login@secret-mail.test";
    private readonly List<ProfileTestHost> _hosts = [];
    private async Task<ProfileTestHost> Host(string? cs = null, Action<IServiceCollection>? extra = null)
    {
        // These tests exercise the inquiry service's own caps, so lift the edge policy that would answer first.
        var h = NewHost(pg, cs ?? await pg.CreateIsolatedDatabaseAsync(), extra,
            new Dictionary<string, string> { ["Abuse:RateLimits:public-form:Permit"] = "1000" });
        _hosts.Add(h);
        return h;
    }
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private static async Task<TestUser> Artist(ProfileTestHost h, string? email = ArtistLogin)
    {
        var u = await h.SignIn(email);
        Assert.Equal(HttpStatusCode.OK, (await u.Onboard(ProfileTestHost.UniqueHandle())).Status);
        return u;
    }

    private static object Body(string type = "Commission", string email = "buyer@example.test", string message = "Hello there", Guid? workId = null) =>
        new { type, email, message, workId };

    private static string Url(Guid artist) => $"/v1/artists/{artist}/inquiries";

    private static FakeEmailSender Outbox(ProfileTestHost h) => h.Services.GetRequiredService<FakeEmailSender>();

    [Fact]
    public async Task Sending_saves_the_inquiry_and_emails_the_artist_without_leaking_their_address()
    {
        var h = await Host(); var artist = await Artist(h); var buyer = await User(h);
        var work = await SeedWork(h, artist.Id, "Harbor");

        var r = await buyer.Post(Url(artist.Id), Body("Buy a print", "buyer@example.test", "Line one\nLine two", work));

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.DoesNotContain(ArtistLogin, r.Body.GetRawText());
        Assert.Equal("Buy a print", r.Data.GetProperty("type").GetString());
        Assert.Equal(artist.Id, r.Data.GetProperty("artistId").GetGuid());
        var mail = Assert.Single(Outbox(h).Sent);
        Assert.Equal(ArtistLogin, mail.To);
        Assert.Equal("New Buy a print inquiry via Vela", mail.Subject);
        Assert.Contains("buyer@example.test", mail.TextBody);
        Assert.Contains("Harbor", mail.TextBody);
        Assert.Contains("Line one\nLine two", mail.TextBody);
        await using var db = h.Db();
        var row = await db.Inquiries.SingleAsync();
        Assert.NotNull(row.EmailedAt);
        Assert.Equal(buyer.Id, row.SenderId);
        Assert.NotNull(row.ClientIp);
    }

    [Fact]
    public async Task Inquiry_is_saved_before_email_is_sent_and_survives_email_failure()
    {
        ProfileTestHost? host = null; var seenDuringSend = (Rows: -1, Emailed: true);
        var h = host = await Host(null, s => s.AddSingleton<IEmailSender>(new ProbeEmailSender(async () =>
        {
            await using var db = host!.Db();
            seenDuringSend = (await db.Inquiries.CountAsync(), await db.Inquiries.AnyAsync(i => i.EmailedAt != null));
        })));
        var artist = await Artist(h); var buyer = await User(h);
        Assert.Equal(HttpStatusCode.OK, (await buyer.Post(Url(artist.Id), Body())).Status);
        Assert.Equal((1, false), seenDuringSend);

        var failing = await Host(null, s => s.AddSingleton<IEmailSender, ThrowingEmailSender>());
        var artist2 = await Artist(failing); var buyer2 = await User(failing);
        var r = await buyer2.Post(Url(artist2.Id), Body());

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.True(r.Body.GetProperty("success").GetBoolean());
        Assert.DoesNotContain(ArtistLogin, r.Body.GetRawText());
        await using var db2 = failing.Db();
        Assert.Null((await db2.Inquiries.SingleAsync()).EmailedAt);
    }

    [Fact]
    public async Task Artist_without_a_login_email_still_gets_a_saved_inquiry_and_nothing_is_sent()
    {
        var h = await Host(); var artist = await Artist(h, null); var buyer = await User(h);

        var r = await buyer.Post(Url(artist.Id), Body());

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Empty(Outbox(h).Sent);
        await using var db = h.Db();
        Assert.Null((await db.Inquiries.SingleAsync()).EmailedAt);
    }

    [Fact]
    public async Task Inquiry_requires_valid_email_with_at_sign_and_non_empty_message()
    {
        var h = await Host(); var artist = await Artist(h); var buyer = await User(h);
        var bad = new (object Body, string Field)[]
        {
            (Body(type: "Spam"), "type"), (Body(type: ""), "type"),
            (Body(email: "no-at-sign"), "email"), (Body(email: "a@b"), "email"), (Body(email: "a b@c.test"), "email"),
            (Body(email: "x@" + new string('d', 250) + ".test"), "email"), (Body(email: "a@b.test\r\nBcc: x@y.test"), "email"),
            (Body(message: ""), "message"), (Body(message: "  \n\t "), "message"), (Body(message: new string('m', 2001)), "message"),
            (Body(message: "bad\u0007bell"), "message"), (Body(message: "tab\there"), "message"),
            (new { type = "Commission", email = "a@b.test" }, "message"), ("not json", "body"),
        };

        foreach (var (body, field) in bad)
        {
            var r = await buyer.Post(Url(artist.Id), body);
            Assert.Equal(HttpStatusCode.BadRequest, r.Status);
            Assert.Equal("validation_failed", r.ErrorCode);
            Assert.Contains(field, r.Body.GetProperty("error").GetProperty("message").GetString());
            Assert.DoesNotContain("no-at-sign", r.Body.GetRawText());
        }
        Assert.Equal(HttpStatusCode.OK, (await buyer.Post(Url(artist.Id), Body(message: new string('m', 2000)))).Status);
        Assert.Equal(HttpStatusCode.OK, (await buyer.Post(Url(artist.Id), Body(message: "  trimmed  "))).Status);
        await using var db = h.Db();
        Assert.Contains(await db.Inquiries.ToListAsync(), i => i.Message == "trimmed");
        Assert.Equal(2, await db.Inquiries.CountAsync());
    }

    [Fact]
    public async Task Inquiry_target_must_be_a_visible_artist_and_not_self()
    {
        var h = await Host(); var buyer = await User(h); var artist = await Artist(h); var raw = await h.SignIn();
        var blockedBy = await User(h); var deleted = await Artist(h); var other = await Artist(h);
        await blockedBy.Put($"/v1/blocks/{buyer.Id}");
        await SetUser(h, deleted.Id, u => u.DeletedAt = ProfileTestHost.Now);
        var otherWork = await SeedWork(h, other.Id);
        var draft = await SeedWork(h, artist.Id);
        await using (var db = h.Db()) await db.Works.Where(w => w.Id == draft).ExecuteUpdateAsync(s => s.SetProperty(w => w.ReportHidden, true));

        foreach (var id in new[] { Guid.NewGuid(), raw.Id, blockedBy.Id, deleted.Id })
        {
            var r = await buyer.Post(Url(id), Body());
            Assert.Equal(HttpStatusCode.NotFound, r.Status);
            Assert.Equal("artist_not_found", r.ErrorCode);
        }
        var self = await artist.Post(Url(artist.Id), Body());
        Assert.Equal("cannot_inquire_self", self.ErrorCode);
        Assert.Equal(HttpStatusCode.BadRequest, self.Status);
        foreach (var w in new[] { otherWork, draft, Guid.NewGuid() })
        {
            var r = await buyer.Post(Url(artist.Id), Body(workId: w));
            Assert.Equal(HttpStatusCode.BadRequest, r.Status);
            Assert.Equal("validation_failed", r.ErrorCode);
            Assert.Contains("workId", r.Body.GetProperty("error").GetProperty("message").GetString());
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Anonymous().PostAsync(Url(artist.Id), null)).StatusCode);
        Assert.Empty(Outbox(h).Sent);
    }

    [Fact]
    public async Task Inquiry_rate_limited_per_sender_and_ip()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var h = await Host(cs); var buyer = await User(h);
        var artists = new List<TestUser>();
        for (var i = 0; i < 5; i++) artists.Add(await Artist(h));

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await buyer.Post(Url(artists[0].Id), Body())).Status);
        var perArtist = await buyer.Client.PostAsJsonAsync(Url(artists[0].Id), Body());
        Assert.Equal(HttpStatusCode.TooManyRequests, perArtist.StatusCode);
        Assert.NotNull(perArtist.Headers.RetryAfter);
        Assert.Equal("rate_limited", (await buyer.Post(Url(artists[0].Id), Body())).ErrorCode);

        foreach (var (idx, times) in new[] { (1, 3), (2, 3), (3, 1) })
            for (var i = 0; i < times; i++) Assert.Equal(HttpStatusCode.OK, (await buyer.Post(Url(artists[idx].Id), Body())).Status);
        var perSender = await buyer.Post(Url(artists[4].Id), Body());
        Assert.Equal(HttpStatusCode.TooManyRequests, perSender.Status);

        await using (var db = h.Db())
            await db.Inquiries.ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedAt, ProfileTestHost.Now.AddHours(-25)));
        Assert.Equal(HttpStatusCode.OK, (await buyer.Post(Url(artists[4].Id), Body())).Status);
    }

    [Fact]
    public async Task Inquiry_rate_limited_per_client_ip_across_senders()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var h = await Host(cs); var artist = await Artist(h); var other = await Artist(h);
        await using (var db = h.Db())
        {
            for (var i = 0; i < 30; i++)
                db.Inquiries.Add(new Vela.Api.Data.Entities.Inquiry { Id = Guid.NewGuid(), SenderId = other.Id, ArtistId = artist.Id, Type = "Commission", SenderEmail = "x@y.test", Message = "m", ClientIp = "127.0.0.1", CreatedAt = ProfileTestHost.Now.AddHours(-1) });
            await db.SaveChangesAsync();
        }
        var buyer = await User(h);

        var r = await buyer.Post(Url(artist.Id), Body());

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
        Assert.Equal("rate_limited", r.ErrorCode);
    }

    [Fact]
    public async Task Inquiry_email_body_does_not_contain_html_injection_unescaped()
    {
        var h = await Host(); var artist = await Artist(h); var buyer = await User(h);
        var message = "<script>alert(1)</script> <b>hi</b>\nSubject: evil\r\nمرحبا 🎨";

        var r = await buyer.Post(Url(artist.Id), Body(message: message.Replace("\r", "")));

        Assert.Equal(HttpStatusCode.OK, r.Status);
        var mail = Assert.Single(Outbox(h).Sent);
        Assert.DoesNotContain("<", mail.Subject);
        Assert.DoesNotContain("\n", mail.Subject);
        Assert.Equal("New Commission inquiry via Vela", mail.Subject);
        Assert.Contains("<script>alert(1)</script>", mail.TextBody);
        Assert.DoesNotContain("<html", mail.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("مرحبا 🎨", mail.TextBody);
    }

    [Fact]
    public async Task Artist_login_email_never_appears_in_any_response()
    {
        var h = await Host(); var artist = await Artist(h); var buyer = await User(h);
        var work = await SeedWork(h, artist.Id);
        var raws = new List<string>();

        raws.Add((await buyer.Post(Url(artist.Id), Body(workId: work))).Body.GetRawText());
        raws.Add((await buyer.Post(Url(artist.Id), Body(email: "bad"))).Body.GetRawText());
        raws.Add((await buyer.Get($"/v1/artists/{artist.Id}")).Body.GetRawText());
        raws.Add((await buyer.Post(Url(Guid.NewGuid()), Body())).Body.GetRawText());
        raws.Add((await artist.Get("/v1/notifications")).Body.GetRawText());

        Assert.All(raws, raw => Assert.DoesNotContain("secret-mail", raw));
    }
}
