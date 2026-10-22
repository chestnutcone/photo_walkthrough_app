using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Data;

[Collection(PostgresCollection.Name)]
public class SchemaTests(PostgresFixture pg)
{
    private static readonly Guid FreemiumId = Tier.FreemiumId;

    private static User NewUser() => new() { Id = Guid.NewGuid(), TierId = FreemiumId, CreatedAt = DateTimeOffset.UtcNow };

    private static Profile NewProfile(Guid userId, string handle) => new()
    {
        UserId = userId, Handle = handle, DisplayName = "N", Bio = "", City = "", Discipline = "",
        Availability = "", Website = "", Social = "", Studio = "", ReplyTime = "",
    };

    [Fact]
    public async Task Migrations_apply_to_empty_database()
    {
        await using var db = pg.NewContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.True(await db.Database.CanConnectAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Freemium_tier_is_seeded_with_max_works_20()
    {
        await using var db = pg.NewContext();
        var tier = await db.Tiers.SingleAsync(t => t.Name == "Freemium");
        Assert.Equal(20, tier.MaxWorks);
    }

    [Fact]
    public async Task Identity_provider_subject_is_unique()
    {
        var subject = Guid.NewGuid().ToString();
        await using var db = pg.NewContext();
        var u1 = NewUser(); var u2 = NewUser();
        db.Users.AddRange(u1, u2);
        db.Identities.Add(new Identity { Id = Guid.NewGuid(), UserId = u1.Id, Provider = "google", Subject = subject });
        await db.SaveChangesAsync();

        await using var db2 = pg.NewContext();
        db2.Identities.Add(new Identity { Id = Guid.NewGuid(), UserId = u2.Id, Provider = "google", Subject = subject });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)ex.InnerException!).SqlState);
    }

    [Fact]
    public async Task Handle_is_unique_case_insensitively()
    {
        var handle = "mara_" + Guid.NewGuid().ToString("N")[..8];
        await using var db = pg.NewContext();
        var u1 = NewUser(); var u2 = NewUser();
        db.Users.AddRange(u1, u2);
        db.Profiles.Add(NewProfile(u1.Id, handle));
        await db.SaveChangesAsync();

        // Mixed case is rejected by the lower(handle) check constraint.
        await using var db2 = pg.NewContext();
        var u3 = NewUser(); db2.Users.Add(u3);
        db2.Profiles.Add(NewProfile(u3.Id, handle.ToUpperInvariant()));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)ex.InnerException!).SqlState);

        // Same lowercase handle is rejected by the unique index.
        await using var db3 = pg.NewContext();
        db3.Profiles.Add(NewProfile(u2.Id, handle));
        var ex2 = await Assert.ThrowsAsync<DbUpdateException>(() => db3.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)ex2.InnerException!).SqlState);
    }

    [Fact]
    public async Task Work_status_is_stored_as_lowercase_string_and_user_delete_cascades()
    {
        await using var db = pg.NewContext();
        var u = NewUser(); db.Users.Add(u);
        var work = new Work
        {
            Id = Guid.NewGuid(), ArtistId = u.Id, Title = "T", Intro = "", Status = WorkStatus.Pending,
            ImageBlob = "b", Width = 3, Height = 4, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Works.Add(work);
        db.Chapters.Add(new Chapter { WorkId = work.Id, Index = 0, X = 0, Y = 1, Zoom = null, Title = "c", Body = "b" });
        await db.SaveChangesAsync();

        var status = await db.Database.SqlQuery<string>($"select status as \"Value\" from works where id = {work.Id}").SingleAsync();
        Assert.Equal("pending", status);

        await db.Users.Where(x => x.Id == u.Id).ExecuteDeleteAsync();
        Assert.False(await db.Works.AnyAsync(w => w.Id == work.Id));
        Assert.False(await db.Chapters.AnyAsync(c => c.WorkId == work.Id));
    }

    [Fact]
    public async Task Profile_services_and_shows_round_trip_as_jsonb()
    {
        await using var db = pg.NewContext();
        var u = NewUser(); db.Users.Add(u);
        var p = NewProfile(u.Id, "j" + Guid.NewGuid().ToString("N")[..10]);
        p.Services.Add(new ServiceItem("Portrait", "Oil", "$900"));
        p.Shows.Add(new ShowItem("2024", "Group show"));
        db.Profiles.Add(p);
        await db.SaveChangesAsync();

        await using var db2 = pg.NewContext();
        var loaded = await db2.Profiles.SingleAsync(x => x.UserId == u.Id);
        Assert.Equal("Portrait", loaded.Services.Single().Title);
        Assert.Equal("Group show", loaded.Shows.Single().Text);
    }

    private async Task<Guid> NewWorkAsync()
    {
        await using var db = pg.NewContext();
        var u = NewUser(); db.Users.Add(u);
        var w = new Work
        {
            Id = Guid.NewGuid(), ArtistId = u.Id, Title = "T", Intro = "", Status = WorkStatus.Draft,
            ImageBlob = "b", Width = 3, Height = 4, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Works.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    }

    private async Task<string?> SaveChapterAsync(Guid workId, int index, double x, double y, double? zoom)
    {
        await using var db = pg.NewContext();
        db.Chapters.Add(new Chapter { WorkId = workId, Index = index, X = x, Y = y, Zoom = zoom, Title = "c", Body = "b" });
        try { await db.SaveChangesAsync(); return null; }
        catch (DbUpdateException e) { return ((PostgresException)e.InnerException!).SqlState; }
    }

    [Theory]
    [InlineData(-0.01, 0.5, null, 1)]
    [InlineData(1.01, 0.5, null, 2)]
    [InlineData(0.5, -0.01, null, 3)]
    [InlineData(0.5, 1.01, null, 4)]
    [InlineData(0.5, 0.5, 0.0, 5)]
    [InlineData(0.5, 0.5, -1.0, 6)]
    [InlineData(0.5, 0.5, null, -1)]
    [InlineData(0.5, 0.5, null, 40)]
    public async Task Chapter_out_of_range_values_violate_check_constraint(double x, double y, double? zoom, int index)
    {
        var workId = await NewWorkAsync();
        // Index is only the failing field for -1 and 40; other rows use a unique valid index.
        Assert.Equal(PostgresErrorCodes.CheckViolation, await SaveChapterAsync(workId, index, x, y, zoom));
    }

    [Theory]
    [InlineData(0.0, 1.0, null, 0)]
    [InlineData(1.0, 0.0, 2.4, 39)]
    [InlineData(0.0, 0.0, null, 1)]
    public async Task Chapter_edge_values_are_accepted(double x, double y, double? zoom, int index)
    {
        var workId = await NewWorkAsync();
        Assert.Null(await SaveChapterAsync(workId, index, x, y, zoom));
    }

    [Fact]
    public async Task Works_has_published_artist_index()
    {
        await using var db = pg.NewContext();
        var def = await db.Database.SqlQuery<string>(
            $"select indexdef as \"Value\" from pg_indexes where tablename = 'works' and indexdef ilike '%(artist_id, published_at, id)%'")
            .SingleAsync();
        Assert.Contains("status", def);
        Assert.Contains("published", def);
    }

    [Fact]
    public async Task Deleting_reporter_keeps_report_with_null_reporter()
    {
        await using var db = pg.NewContext();
        var u = NewUser(); db.Users.Add(u);
        var r = new Report
        {
            Id = Guid.NewGuid(), Source = "user", Reason = "spam", TargetType = "work",
            TargetId = Guid.NewGuid(), ReporterId = u.Id, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Reports.Add(r);
        await db.SaveChangesAsync();

        await db.Users.Where(x => x.Id == u.Id).ExecuteDeleteAsync();

        await using var db2 = pg.NewContext();
        var kept = await db2.Reports.SingleAsync(x => x.Id == r.Id);
        Assert.Null(kept.ReporterId);
    }
}
