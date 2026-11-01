using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Vela.Api.Data;

namespace Vela.Api.Tests.Support;

/// <summary>One Postgres container per collection; migrations are applied once at startup.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public string ConnectionString => _container.GetConnectionString();

    public VelaDbContext NewContext()
    {
        var builder = new DbContextOptionsBuilder<VelaDbContext>();
        builder.ConfigureVela(ConnectionString);
        return new VelaDbContext(builder.Options);
    }

    /// <summary>Creates a fresh migrated database on the same container, for tests that need no other data.</summary>
    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var name = "iso_" + Guid.NewGuid().ToString("N");
        await using (var admin = new Npgsql.NpgsqlConnection(ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {name}";
            await cmd.ExecuteNonQueryAsync();
        }
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString) { Database = name, Pooling = false }.ConnectionString;
        var builder = new DbContextOptionsBuilder<VelaDbContext>();
        builder.ConfigureVela(cs);
        await using var db = new VelaDbContext(builder.Options);
        await db.Database.MigrateAsync();
        return cs;
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
