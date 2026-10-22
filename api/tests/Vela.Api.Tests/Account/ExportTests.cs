using System.Net;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Account.AccountKit;
using static Vela.Api.Tests.Account.ExportKit;

namespace Vela.Api.Tests.Account;

[Collection(PostgresCollection.Name)]
public class ExportTests(PostgresFixture pg) : IDisposable
{
    private readonly List<Vela.Api.Tests.Profiles.ProfileTestHost> _hosts = [];

    private async Task<(Vela.Api.Tests.Profiles.ProfileTestHost Host, World World)> Seeded()
    {
        var h = NewHost(pg, await pg.CreateIsolatedDatabaseAsync());
        _hosts.Add(h);
        return (h, await Seed(h));
    }

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private static async Task<string> HandleOf(Vela.Api.Tests.Profiles.ProfileTestHost h, Guid id)
    {
        await using var db = h.Db();
        return (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Profiles, p => p.UserId == id)).Handle;
    }

    [Fact]
    public async Task Export_contains_profile_works_replies_follows_and_images()
    {
        var (h, w) = await Seeded();

        var (id, zip) = await RequestAndBuild(h, w.U);

        Assert.Equal(
            new[] { "README.txt", "account.json", "blocks.json", "follows.json", "images/avatar.webp", $"images/works/{w.UWork}/original.jpg",
                    "inquiries_received_summary.json", "inquiries_sent.json", "notifications.json", "profile.json", "replies.json", "reports_filed.json", "works.json" }.Order(),
            zip.Names.Order());
        var profile = zip.Json("profile.json");
        Assert.Equal("Una U", profile.GetProperty("displayName").GetString());
        Assert.Equal("u.public@mail.test", profile.GetProperty("publicEmail").GetString());
        Assert.Equal(await HandleOf(h, w.U.Id), profile.GetProperty("handle").GetString());
        var account = zip.Json("account.json");
        Assert.Equal(w.U.Id, account.GetProperty("userId").GetGuid());
        Assert.Equal("Freemium", account.GetProperty("tier").GetString());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, account.GetProperty("ageConfirmedAt").ValueKind);
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, account.GetProperty("termsAcceptedAt").ValueKind);
        var identities = account.GetProperty("identities").EnumerateArray().Select(i => (i.GetProperty("provider").GetString(), i.GetProperty("email").GetString()!.ToLowerInvariant())).ToList();
        Assert.Contains(("apple", UAppleEmail), identities);
        Assert.Contains(identities, i => i.Item2 == ULogin.ToLowerInvariant());
        var works = zip.Json("works.json").EnumerateArray().Single();
        Assert.Equal(w.UWork, works.GetProperty("id").GetGuid());
        Assert.Equal("published", works.GetProperty("status").GetString());
        Assert.Equal("U story", works.GetProperty("title").GetString());
        Assert.Equal("alt U story", works.GetProperty("altText").GetString());
        var chapters = works.GetProperty("chapters").EnumerateArray().ToList();
        Assert.Equal(2, chapters.Count);
        Assert.Equal(0.25, chapters[0].GetProperty("x").GetDouble());
        Assert.Equal(2.4, chapters[0].GetProperty("zoom").GetDouble());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, chapters[1].GetProperty("zoom").ValueKind);
        Assert.Equal("meta", chapters[0].GetProperty("meta").GetString());
        var replies = zip.Json("replies.json").EnumerateArray().ToList();
        Assert.Equal(["U reply on O", "U reply on own"], replies.Select(r => r.GetProperty("text").GetString()).Order(StringComparer.Ordinal).ToList());
        Assert.Contains(replies, r => r.GetProperty("hidden").GetBoolean());
        var follows = zip.Json("follows.json");
        var oHandle = await HandleOf(h, w.O.Id);
        Assert.Equal([oHandle], follows.GetProperty("following").EnumerateArray().Select(x => x.GetString()!).ToList());
        Assert.Equal([oHandle], follows.GetProperty("followers").EnumerateArray().Select(x => x.GetString()!).ToList());
        Assert.Equal([await HandleOf(h, w.T.Id)], zip.Json("blocks.json").GetProperty("blocked").EnumerateArray().Select(x => x.GetString()!).ToList());
        Assert.Equal("reply", zip.Json("notifications.json").EnumerateArray().Single().GetProperty("type").GetString());
        var sent = zip.Json("inquiries_sent.json").EnumerateArray().Single();
        Assert.Equal(oHandle, sent.GetProperty("artistHandle").GetString());
        Assert.Equal(UInquiryMessage, sent.GetProperty("message").GetString());
        Assert.Equal("u.reply@mail.test", sent.GetProperty("replyEmail").GetString());
        Assert.Equal(1, zip.Json("inquiries_received_summary.json").GetProperty("count").GetInt32());
        Assert.Equal(UNote, zip.Json("reports_filed.json").EnumerateArray().Single().GetProperty("note").GetString());
        Assert.Equal(h.Blobs.Bytes($"originals/works/{w.UWork}"), zip.Bytes($"images/works/{w.UWork}/original.jpg"));
        Assert.Equal(h.Blobs.Bytes($"avatars/{w.U.Id}/avatar-256.webp"), zip.Bytes("images/avatar.webp"));
        Assert.Contains("profile.json", zip.Text("README.txt"));
        var get = await w.U.Get($"/v1/me/export/{id}");
        Assert.Equal("ready", get.Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Other_users_data_is_not_in_export()
    {
        var (h, w) = await Seeded();

        var (_, zip) = await RequestAndBuild(h, w.U);

        var all = zip.AllText;
        foreach (var secret in new[] { OLogin, "t.login@third.test", "Otto O", "Tess T", OInquiryMessage, "o.reply@mail.test", "T reply on O", "O reply on U", "203.0.113", "token", "code_hash", StrangerEmail })
            Assert.DoesNotContain(secret, all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(zip.Names, n => n.Contains(w.OWork.ToString()) || n.Contains(w.TWork.ToString()) || n.Contains(w.O.Id.ToString()));
        Assert.Equal(["count"], zip.Json("inquiries_received_summary.json").EnumerateObject().Select(p => p.Name).ToList());
    }

    [Fact]
    public async Task The_archive_is_a_private_exports_blob_and_the_job_stores_its_expiry()
    {
        var (h, w) = await Seeded();
        var r = await Request(w.U);
        var id = r.Data.GetProperty("id").GetGuid();
        Assert.Equal("pending", r.Data.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, r.Data.GetProperty("downloadUrl").ValueKind);

        await RunWorker(h);

        var get = await w.U.Get($"/v1/me/export/{id}");
        Assert.Equal("ready", get.Data.GetProperty("status").GetString());
        Assert.Equal(h.Clock.GetUtcNow().AddDays(7), get.Data.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(h.Clock.GetUtcNow(), get.Data.GetProperty("createdAt").GetDateTimeOffset());
        Assert.StartsWith($"https://blob.test/exports/{w.U.Id}/{id}.zip?", get.Data.GetProperty("downloadUrl").GetString());
        Assert.Throws<ArgumentException>(() => h.Blobs.PublicUrl($"exports/{w.U.Id}/{id}.zip"));
    }

    [Fact]
    public async Task The_ready_email_is_plain_text_without_a_link_or_user_content()
    {
        var (h, w) = await Seeded();

        await RequestAndBuild(h, w.U);

        var mail = Assert.Single(EmailsTo(h, ULogin, UAppleEmail));
        Assert.Equal("Your Vela data export is ready", mail.Subject);
        Assert.Contains("open the app", mail.TextBody, StringComparison.OrdinalIgnoreCase);
        foreach (var banned in new[] { "http", "Una U", "U story", UInquiryMessage, "u.public@mail.test", w.UWork.ToString(), ".zip" })
            Assert.DoesNotContain(banned, mail.Subject + mail.TextBody);
    }

    [Fact]
    public async Task Concurrent_workers_build_and_announce_an_export_once()
    {
        var (h, w) = await Seeded();
        await Request(w.U);

        await Task.WhenAll(RunWorker(h), RunWorker(h), RunWorker(h));

        Assert.Single(EmailsTo(h, ULogin, UAppleEmail));
        Assert.Single(h.Blobs.Names, n => n.StartsWith($"exports/{w.U.Id}/") && n != $"exports/{w.U.Id}/seed.zip");
    }

    [Fact]
    public async Task A_deleted_account_cannot_request_an_export()
    {
        var (h, w) = await Seeded();
        await RequestDelete(w.U);

        var r = await Request(w.U);

        Assert.Equal(HttpStatusCode.Forbidden, r.Status);
        Assert.Equal("account_deleted", r.ErrorCode);
    }
}
