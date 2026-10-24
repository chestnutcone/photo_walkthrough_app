using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Data;

[Collection(PostgresCollection.Name)]
public class MigrateOnStartupTests(PostgresFixture pg)
{
    private async Task<string> EmptyDatabase()
    {
        var name = "empty_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(pg.ConnectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE {name}";
        await cmd.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }

    private static async Task<bool> HasSchema(ProfileTestHost host)
    {
        await using var db = host.Db();
        return (await db.Database.GetAppliedMigrationsAsync()).Any();
    }

    [Fact]
    public async Task Setting_applies_pending_migrations_when_the_host_starts()
    {
        using var host = new ProfileTestHost(pg, new Dictionary<string, string> { ["Database:MigrateOnStartup"] = "true" }, await EmptyDatabase());
        host.Anonymous(); // builds the host
        Assert.True(await HasSchema(host));
    }

    [Fact]
    public async Task Without_the_setting_the_database_is_left_alone()
    {
        using var host = new ProfileTestHost(pg, null, await EmptyDatabase());
        host.Anonymous();
        Assert.False(await HasSchema(host));
    }
}
