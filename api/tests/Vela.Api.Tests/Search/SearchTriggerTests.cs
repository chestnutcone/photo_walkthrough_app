using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Vela.Api.Data;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Search;

/// <summary>Covers the triggers that maintain works.search_text (insert, update, delete paths) and the migration backfill.</summary>
[Collection(PostgresCollection.Name)]
public class SearchTriggerTests(PostgresFixture pg) : IDisposable
{
    private const string BeforeSearchMigration = "20261022093641_InquiryDetails";
    private const string SearchMigration = "20261022094705_AddSearchIndexes";
    private readonly List<ProfileTestHost> _hosts = [];
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private async Task<(ProfileTestHost Host, TestUser Artist, TestUser Viewer)> Setup()
    {
        var h = new ProfileTestHost(pg, null, await pg.CreateIsolatedDatabaseAsync());
        _hosts.Add(h);
        return (h, await h.SignInOnboarded(ProfileTestHost.UniqueHandle("t")), await h.SignInOnboarded(ProfileTestHost.UniqueHandle("v")));
    }

    private static async Task<string> SearchText(ProfileTestHost h, Guid workId)
    {
        await using var db = h.Db();
        return await db.Works.Where(w => w.Id == workId).Select(w => w.SearchText).SingleAsync();
    }

    private static async Task<bool> Finds(TestUser viewer, string q, Guid workId) =>
        (await viewer.Get($"/v1/search?q={Uri.EscapeDataString(q)}&type=stories")).Data.GetProperty("stories")
            .EnumerateArray().Any(e => e.GetProperty("id").GetGuid() == workId);

    private static async Task<Guid> Publish(ProfileTestHost h, Guid artist, string title, string intro, params string[] chapters)
    {
        await using var db = h.Db();
        var id = Guid.NewGuid();
        db.Works.Add(new Vela.Api.Data.Entities.Work
        {
            Id = id, ArtistId = artist, Title = title, Intro = intro, Status = Vela.Api.Data.Entities.WorkStatus.Published,
            ImageBlob = "b", Width = 3, Height = 4, CreatedAt = ProfileTestHost.Now, PublishedAt = ProfileTestHost.Now,
        });
        await db.SaveChangesAsync();
        for (var i = 0; i < chapters.Length; i++)
            db.Chapters.Add(new Vela.Api.Data.Entities.Chapter { WorkId = id, Index = i, X = .5, Y = .5, Title = chapters[i], Body = "b" });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Updating_a_work_title_changes_what_is_searchable()
    {
        var (h, artist, viewer) = await Setup();
        var id = await Publish(h, artist.Id, "Wandering Heron", "", "Shore");
        Assert.True(await Finds(viewer, "heron", id));

        await using (var db = h.Db())
            await db.Works.Where(w => w.Id == id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Title, "Quiet Otter").SetProperty(w => w.Intro, "riverbank dawn"));

        Assert.Equal("quiet otter riverbank dawn shore", (await SearchText(h, id)).Replace("  ", " "));
        Assert.False(await Finds(viewer, "heron", id));
        Assert.True(await Finds(viewer, "otter", id));
        Assert.True(await Finds(viewer, "riverbank", id));
    }

    [Fact]
    public async Task Updating_a_chapter_title_changes_what_is_searchable()
    {
        var (h, artist, viewer) = await Setup();
        var id = await Publish(h, artist.Id, "Plain", "", "Lanterns at dusk", "Second");
        Assert.True(await Finds(viewer, "lanterns", id));

        await using (var db = h.Db())
            await db.Chapters.Where(c => c.WorkId == id && c.Index == 0).ExecuteUpdateAsync(s => s.SetProperty(c => c.Title, "Fireflies at night"));

        Assert.False(await Finds(viewer, "lanterns", id));
        Assert.True(await Finds(viewer, "fireflies", id));
        Assert.Contains("second", await SearchText(h, id));
    }

    [Fact]
    public async Task Deleting_a_chapter_and_replacing_all_chapters_removes_their_text()
    {
        var (h, artist, viewer) = await Setup();
        var id = await Publish(h, artist.Id, "Plain", "", "Amber harbour", "Violet meadow", "Crimson canyon");

        await using (var db = h.Db())
            await db.Chapters.Where(c => c.WorkId == id && c.Index == 1).ExecuteDeleteAsync();
        Assert.False(await Finds(viewer, "violet", id));
        Assert.True(await Finds(viewer, "crimson", id));

        // The updateWork path: ExecuteDelete of every chapter, then new ones are inserted.
        await using (var db = h.Db())
            await db.Chapters.Where(c => c.WorkId == id).ExecuteDeleteAsync();
        Assert.Equal("plain", (await SearchText(h, id)).Trim());
        Assert.False(await Finds(viewer, "amber", id));

        await using (var db = h.Db())
        {
            db.Chapters.Add(new Vela.Api.Data.Entities.Chapter { WorkId = id, Index = 0, X = .5, Y = .5, Title = "Teal lagoon", Body = "b" });
            await db.SaveChangesAsync();
        }
        Assert.True(await Finds(viewer, "lagoon", id));
        Assert.False(await Finds(viewer, "crimson", id));
    }

    [Fact]
    public async Task Migration_backfills_search_text_for_existing_works()
    {
        var cs = await FreshDatabaseAsync();
        var builder = new DbContextOptionsBuilder<VelaDbContext>();
        builder.ConfigureVela(cs);
        await using (var db = new VelaDbContext(builder.Options))
            await db.GetService<IMigrator>().MigrateAsync(BeforeSearchMigration);

        var artist = Guid.NewGuid(); var work = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await Exec(conn, $"""
                INSERT INTO users (id, tier_id, created_at, is_admin, age_confirmed_at, terms_accepted_at)
                VALUES ('{artist}', '5b0c1c3e-7a3d-4c55-9a52-1f0e2d6b7a01', now(), false, now(), now());
                INSERT INTO profiles (user_id, handle, display_name, bio, city, discipline, availability, website, social, studio, reply_time, services, shows)
                VALUES ('{artist}', 'backfiller', 'Back Filler', '', '', '', '', '', '', '', '', '[]', '[]');
                INSERT INTO works (id, artist_id, title, intro, status, report_hidden, moderation_override, image_blob, width, height, created_at, published_at, scan_attempts, publish_failures)
                VALUES ('{work}', '{artist}', 'Old Lighthouse', 'Pre Existing Intro', 'published', false, false, 'b', 3, 4, now(), now(), 0, 0);
                INSERT INTO chapters (work_id, index, x, y, title, body) VALUES ('{work}', 0, 0.5, 0.5, 'Foghorn Chapter', 'b'), ('{work}', 1, 0.5, 0.5, 'Second Light', 'b');
                """);
        }

        await using (var db = new VelaDbContext(builder.Options))
            await db.GetService<IMigrator>().MigrateAsync(SearchMigration);

        var host = new ProfileTestHost(pg, null, cs);
        _hosts.Add(host);
        Assert.Equal("old lighthouse pre existing intro foghorn chapter second light", await SearchText(host, work));
        var viewer = await host.SignInOnboarded(ProfileTestHost.UniqueHandle("v"));
        Assert.True(await Finds(viewer, "foghorn", work));
        Assert.True(await Finds(viewer, "pre existing", work));
    }

    private async Task<string> FreshDatabaseAsync()
    {
        var name = "iso_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(pg.ConnectionString))
        {
            await admin.OpenAsync();
            await Exec(admin, $"CREATE DATABASE {name}");
        }
        return new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
