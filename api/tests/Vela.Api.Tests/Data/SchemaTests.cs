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
        db2.Users.Add(NewUser());
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
}
