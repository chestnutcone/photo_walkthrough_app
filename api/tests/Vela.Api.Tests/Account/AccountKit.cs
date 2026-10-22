using ApiReply = Vela.Api.Tests.Profiles.Reply;
using Reply = Vela.Api.Data.Entities.Reply;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Vela.Api.Account;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Account;

public sealed class RecordingCdnPurger : ICdnPurger
{
    public List<string> Purged { get; } = [];
    public Func<Task>? OnPurge { get; set; }

    public async Task PurgeAsync(IReadOnlyList<string> publicUrlsOrPaths)
    {
        if (OnPurge is { } hook) await hook();
        Purged.AddRange(publicUrlsOrPaths);
    }
}

public sealed class RecordingAppleRevoker : IAppleTokenRevoker
{
    public List<Guid> Revoked { get; } = [];

    public Task RevokeAsync(Identity identity)
    {
        Revoked.Add(identity.Id);
        return Task.CompletedTask;
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _lines = [];
    public IReadOnlyList<string> Lines { get { lock (_lines) return [.. _lines]; } }
    public ILogger CreateLogger(string categoryName) => new Capture(this);
    public void Dispose() { }

    private sealed class Capture(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines) owner._lines.Add(formatter(state, exception) + (exception is null ? "" : " | " + exception));
        }
    }
}

/// <summary>Everything the account tests share: a seeded world with a row in every table for user U.</summary>
public sealed record World(
    TestUser U, TestUser O, TestUser T, Guid UWork, Guid OWork, Guid TWork,
    Guid UReplyOnO, Guid OReplyOnU, Guid TReplyOnO, Guid UExportJob, Guid OExportJob);

public static class AccountKit
{
    public const string ULogin = "U.Login@Secret-Mail.test";
    public const string UAppleEmail = "u.apple@relay.test";
    public const string OLogin = "o.login@other-mail.test";
    public const string StrangerEmail = "stranger@nowhere.test";
    public const string UNote = "my private report note";
    public const string UInquiryMessage = "hello from U, please reply";
    public const string OInquiryMessage = "secret message from O to U";
    public static readonly Guid Dummy = Guid.Parse("00000000-0000-0000-0000-00000000dead");

    public static ProfileTestHost NewHost(PostgresFixture pg, string cs, RecordingCdnPurger? cdn = null, RecordingAppleRevoker? apple = null, Action<IServiceCollection>? extra = null) =>
        new(pg, null, cs, s =>
        {
            s.AddSingleton<ICdnPurger>(cdn ?? new RecordingCdnPurger());
            s.AddSingleton<IAppleTokenRevoker>(apple ?? new RecordingAppleRevoker());
            extra?.Invoke(s);
        });

    public static async Task<TestUser> Onboarded(ProfileTestHost h, string? email, string name)
    {
        var u = await h.SignIn(email);
        Assert.Equal(HttpStatusCode.OK, (await u.Onboard(ProfileTestHost.UniqueHandle(), name)).Status);
        return u;
    }

    public static async Task<World> Seed(ProfileTestHost h)
    {
        var u = await Onboarded(h, ULogin, "Una U");
        var o = await Onboarded(h, OLogin, "Otto O");
        var t = await Onboarded(h, "t.login@third.test", "Tess T");
        var now = ProfileTestHost.Now;
        await using var db = h.Db();

        db.Identities.Add(new Identity { Id = Guid.NewGuid(), UserId = u.Id, Provider = "apple", Subject = "apple-" + Guid.NewGuid(), Email = UAppleEmail });
        Guid uw = Guid.NewGuid(), ow = Guid.NewGuid(), tw = Guid.NewGuid();
        foreach (var (id, owner, title) in new[] { (uw, u.Id, "U story"), (ow, o.Id, "O story"), (tw, t.Id, "T story") })
        {
            db.Works.Add(new Work { Id = id, ArtistId = owner, Title = title, Intro = "intro of " + title, Status = WorkStatus.Published, ImageBlob = "b", Width = 3, Height = 4, CreatedAt = now, PublishedAt = now, AltText = "alt " + title });
            for (var i = 0; i < 2; i++) db.Chapters.Add(new Chapter { WorkId = id, Index = i, X = 0.25, Y = 0.75, Zoom = i == 0 ? 2.4 : null, Title = $"{title} ch{i}", Body = "body", Meta = "meta", AltText = "chalt" });
        }
        Guid uro = Guid.NewGuid(), oru = Guid.NewGuid(), tro = Guid.NewGuid();
        db.Replies.AddRange(
            new Reply { Id = uro, WorkId = ow, ChapterIndex = 0, AuthorId = u.Id, Text = "U reply on O", CreatedAt = now },
            new Reply { Id = oru, WorkId = uw, ChapterIndex = 0, AuthorId = o.Id, Text = "O reply on U", CreatedAt = now },
            new Reply { Id = tro, WorkId = ow, ChapterIndex = 0, AuthorId = t.Id, Text = "T reply on O", CreatedAt = now },
            new Reply { Id = Guid.NewGuid(), WorkId = uw, ChapterIndex = 1, AuthorId = u.Id, Text = "U reply on own", Hidden = true, HiddenBy = HiddenBy.Report, CreatedAt = now });
        db.Follows.AddRange(
            new Follow { FollowerId = u.Id, FolloweeId = o.Id, CreatedAt = now }, new Follow { FollowerId = o.Id, FolloweeId = u.Id, CreatedAt = now },
            new Follow { FollowerId = t.Id, FolloweeId = o.Id, CreatedAt = now }, new Follow { FollowerId = o.Id, FolloweeId = t.Id, CreatedAt = now });
        db.Blocks.AddRange(new Block { BlockerId = u.Id, BlockedId = t.Id, CreatedAt = now }, new Block { BlockerId = o.Id, BlockedId = u.Id, CreatedAt = now });
        db.Notifications.AddRange(
            Note(u.Id, "Reply", o.Id, uw, oru, now), Note(o.Id, "Follower", u.Id, null, null, now), Note(o.Id, "Reply", u.Id, ow, uro, now),
            Note(t.Id, "Reply", o.Id, uw, oru, now), Note(t.Id, "NewStory", o.Id, ow, null, now), Note(o.Id, "Follower", t.Id, null, null, now));
        db.Inquiries.AddRange(
            Inquiry(u.Id, o.Id, ow, UInquiryMessage, "u.reply@mail.test", now), Inquiry(o.Id, u.Id, uw, OInquiryMessage, "o.reply@mail.test", now),
            Inquiry(t.Id, o.Id, null, "T to O", "t.reply@mail.test", now));
        db.Reports.AddRange(
            Report("user_report", u.Id, "work", ow, UNote, now), Report("user_report", o.Id, "work", uw, null, now),
            Report("user_report", o.Id, "reply", uro, null, now), Report("user_report", o.Id, "user", u.Id, null, now),
            Report("user_report", t.Id, "reply", oru, null, now), Report("auto_flag", null, "work", uw, null, now),
            Report("user_report", t.Id, "work", ow, "t note", now));
        db.EmailCodes.AddRange(Code(ULogin.ToLowerInvariant(), now), Code(UAppleEmail, now), Code(StrangerEmail, now));
        db.VerifyAttempts.AddRange(Attempt(ULogin.ToLowerInvariant(), now), Attempt(StrangerEmail, now));
        var uj = new ExportJob { Id = Guid.NewGuid(), UserId = u.Id, Status = "ready", RequestedAt = now.AddDays(-3), CompletedAt = now.AddDays(-3), ArchiveBlob = $"exports/{u.Id}/seed.zip" };
        var oj = new ExportJob { Id = Guid.NewGuid(), UserId = o.Id, Status = "pending", RequestedAt = now };
        db.ExportJobs.AddRange(uj, oj);
        await db.Profiles.Where(p => p.UserId == u.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.AvatarBlob, $"avatars/{u.Id}/avatar-256.webp").SetProperty(p => p.PublicEmail, "u.public@mail.test"));
        await db.SaveChangesAsync();

        foreach (var (owner, wid) in new[] { (u.Id, uw), (o.Id, ow), (t.Id, tw) })
        {
            h.Blobs.Put($"works/{wid}/thumb.webp", [1]); h.Blobs.Put($"works/{wid}/full.webp", [2]);
            h.Blobs.Put($"originals/works/{wid}", ImageFixtures.Jpeg(40, 30));
            h.Blobs.Put($"avatars/{owner}/avatar-256.webp", ImageFixtures.WebP(16, 16));
            h.Blobs.Put($"originals/avatars/{owner}", ImageFixtures.Png(16, 16));
        }
        h.Blobs.Put($"exports/{u.Id}/seed.zip", [9]); h.Blobs.Put($"exports/{o.Id}/seed.zip", [9]);
        return new World(u, o, t, uw, ow, tw, uro, oru, tro, uj.Id, oj.Id);
    }

    private static Notification Note(Guid user, string type, Guid? actor, Guid? work, Guid? reply, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), UserId = user, Type = type, ActorId = actor, WorkId = work, ReplyId = reply, ChapterIndex = work is null ? null : 0, CreatedAt = at };

    private static Inquiry Inquiry(Guid sender, Guid artist, Guid? work, string msg, string email, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), SenderId = sender, ArtistId = artist, Type = "Commission", SenderEmail = email, WorkId = work, Message = msg, ClientIp = "203.0.113.9", CreatedAt = at };

    private static Report Report(string source, Guid? reporter, string type, Guid target, string? note, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), Source = source, Reason = "spam", TargetType = type, TargetId = target, Note = note, ReporterId = reporter, CreatedAt = at };

    private static EmailCode Code(string email, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), Email = email, CodeHash = "h", CreatedAt = at, ExpiresAt = at.AddMinutes(10), RequestIp = "203.0.113.7" };

    private static VerifyAttempt Attempt(string email, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), Email = email, Ip = "203.0.113.7", CreatedAt = at };

    public static async Task RunJob(ProfileTestHost h, Guid userId)
    {
        using var scope = h.Scope();
        await scope.ServiceProvider.GetRequiredService<DeletionJob>().RunAsync(userId, CancellationToken.None);
    }

    /// <summary>Test tokens expire on the test clock, so after advancing it a user needs a fresh access token.</summary>
    public static async Task Renew(ProfileTestHost h, TestUser u)
    {
        using var scope = h.Scope();
        var pair = await scope.ServiceProvider.GetRequiredService<Vela.Api.Auth.ITokenService>().IssueAsync(u.Id);
        u.Client.DefaultRequestHeaders.Authorization = new("Bearer", pair.AccessToken);
    }

    public static async Task<ApiReply> RequestDelete(TestUser u) => await u.Delete("/v1/me");

    /// <summary>Marks the account deleted through the public endpoint and runs the hard-delete job once.</summary>
    public static async Task DeleteAndRun(ProfileTestHost h, TestUser u)
    {
        Assert.Equal(HttpStatusCode.OK, (await RequestDelete(u)).Status);
        await RunJob(h, u.Id);
    }

    public static async Task<long> Count(string cs, string table, string column, Guid id)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM \"{table}\" WHERE \"{column}\" = @id";
        cmd.Parameters.AddWithValue("id", id);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<List<string[]>> Query(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await r.ReadAsync()) rows.Add(Enumerable.Range(0, r.FieldCount).Select(i => r.GetValue(i)?.ToString() ?? "").ToArray());
        return rows;
    }

    public static JsonElement Json(ApiReply r, string field) => r.Data.GetProperty(field);
}
