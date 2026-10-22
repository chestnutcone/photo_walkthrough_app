using Reply = Vela.Api.Tests.Profiles.Reply;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;

namespace Vela.Api.Tests.Notifications;

public sealed class ThrowAfterNotifyHandler : IDomainEventHandler<ReplyAdded>
{
    public Task HandleAsync(ReplyAdded evt, CancellationToken ct) => throw new InvalidOperationException("handler failed");
}

public static class NotificationKit
{
    public static ProfileTestHost NewHost(Vela.Api.Tests.Support.PostgresFixture pg, string? cs = null, Action<IServiceCollection>? extra = null) =>
        new(pg, null, cs, extra);

    public static Task<TestUser> User(ProfileTestHost h) => h.SignInOnboarded(ProfileTestHost.UniqueHandle());

    public static async Task<Guid> SeedWork(ProfileTestHost h, Guid artist, string title = "A story", bool reportHidden = false)
    {
        await using var db = h.Db();
        var w = new Work { Id = Guid.NewGuid(), ArtistId = artist, Title = title, Status = WorkStatus.Published, ReportHidden = reportHidden, ImageBlob = "b", Width = 3, Height = 4, CreatedAt = ProfileTestHost.Now, PublishedAt = ProfileTestHost.Now };
        db.Works.Add(w);
        for (var i = 0; i < 2; i++) db.Chapters.Add(new Chapter { WorkId = w.Id, Index = i, X = 0.5, Y = 0.5, Title = "c", Body = "b" });
        await db.SaveChangesAsync();
        return w.Id;
    }

    public static async Task PublishEvent(ProfileTestHost h, Guid work, Guid artist)
    {
        using var scope = h.Scope();
        await scope.ServiceProvider.GetRequiredService<IDomainEventBus>().PublishAsync(new WorkPublished(work, artist), CancellationToken.None);
    }

    public static async Task<List<Notification>> Rows(ProfileTestHost h, Guid user)
    {
        await using var db = h.Db();
        return await db.Notifications.AsNoTracking().Where(n => n.UserId == user).OrderBy(n => n.CreatedAt).ToListAsync();
    }

    public static async Task<Reply> Items(TestUser u, string query = "") => await u.Get("/v1/notifications" + query);

    public static List<JsonElement> Array(Reply r) => r.Data.EnumerateArray().ToList();

    public static async Task<int> Unread(TestUser u) => (await u.Get("/v1/notifications/unread-count")).Data.GetProperty("count").GetInt32();

    public static string ReplyUrl(Guid work, int chapter = 0) => $"/v1/works/{work}/chapters/{chapter}/replies";

    public static async Task SetUser(ProfileTestHost h, Guid id, Action<User> change)
    {
        await using var db = h.Db();
        var u = await db.Users.SingleAsync(x => x.Id == id);
        change(u);
        await db.SaveChangesAsync();
    }
}
