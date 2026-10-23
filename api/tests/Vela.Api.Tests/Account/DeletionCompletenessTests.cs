using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Account.AccountKit;

namespace Vela.Api.Tests.Account;

/// <summary>
/// Guards the deletion job against schema drift. It reads the LIVE schema, so a new table or a new user-referencing column
/// fails here (naming the table and column) until the seed helper populates it and the deletion job removes it.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DeletionCompletenessTests(PostgresFixture pg)
{
    /// <summary>Tables that hold no user data at all.</summary>
    private static readonly HashSet<string> NonUserTables = ["__EFMigrationsHistory", "tiers"];

    /// <summary>
    /// Tables deletion must NOT clear, with the reason. They hold no foreign key to users, so a deleted account leaves them
    /// untouched: bans must survive deletion (else deleting an account would lift a ban) and the audit trail must stay complete.
    /// </summary>
    private static readonly Dictionary<string, string> RetainedTables = new()
    {
        ["banned_identities"] = "retained for safety: HMAC hashes only, so a banned person cannot evade the ban by deleting the account",
        ["evidence_holds"] = "retained for law enforcement: illegal-content evidence is held for at least a year (user_id, work_id and reply_id are plain ids, no foreign keys) and account deletion must never purge it",
        ["admin_actions"] = "retained for legal accountability: the moderation audit trail must outlive admin and target accounts",
    };

    /// <summary>
    /// uuid "_id" columns that cannot hold a user id, with the reason. Everything else ending in "_id" is treated as a
    /// possible user reference: it must be seeded with the user's id and must be empty of it after deletion.
    /// </summary>
    private static readonly Dictionary<string, string> NotUserReferences = new()
    {
        ["chapters.work_id"] = "points at works; chapters are deleted with the user's works",
        ["replies.work_id"] = "points at works; replies on the user's works are deleted explicitly",
        ["notifications.work_id"] = "points at works; notifications about the user's works are deleted explicitly",
        ["notifications.reply_id"] = "points at replies; notifications about the user's replies are deleted explicitly",
        ["inquiries.work_id"] = "points at works; inquiries about the user's works are deleted explicitly",
        ["reports.target_id"] = "polymorphic (work|reply|user); the job deletes by target_type and the test checks it separately",
        ["refresh_tokens.family_id"] = "a random session id",
        ["users.tier_id"] = "points at tiers",
    };

    [Fact]
    public async Task After_job_runs_no_table_has_any_row_referencing_the_user()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        using var h = NewHost(pg, cs);
        var w = await Seed(h);
        var userId = w.U.Id;
        var tables = (await Query(cs, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'"))
            .Select(r => r[0]).Where(t => !NonUserTables.Contains(t) && !RetainedTables.ContainsKey(t)).OrderBy(t => t).ToList();
        var fkColumns = (await Query(cs,
            "SELECT cl.relname, a.attname FROM pg_constraint c JOIN pg_class cl ON cl.oid = c.conrelid " +
            "JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey) " +
            "WHERE c.contype = 'f' AND c.confrelid = 'users'::regclass")).Select(r => (Table: r[0], Column: r[1])).ToList();
        var idColumns = (await Query(cs,
            "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public' AND data_type = 'uuid' " +
            "AND (column_name LIKE '%\\_id' ESCAPE '\\')")).Select(r => (Table: r[0], Column: r[1]))
            .Where(c => !NonUserTables.Contains(c.Table) && !RetainedTables.ContainsKey(c.Table) && !NotUserReferences.ContainsKey($"{c.Table}.{c.Column}")).ToList();
        var checkedColumns = fkColumns.Concat(idColumns).Distinct().OrderBy(c => c.Table).ThenBy(c => c.Column).ToList();

        // Not vacuous: every user table has rows, and every checked column holds the user's id before the job.
        foreach (var t in tables)
            Assert.True((long.Parse((await Query(cs, $"SELECT count(*) FROM \"{t}\""))[0][0])) > 0, $"seed helper leaves table '{t}' empty: seed it and make the deletion job remove its rows");
        foreach (var (table, column) in checkedColumns)
            Assert.True(await Count(cs, table, column, userId) > 0, $"seed helper never puts the user's id in {table}.{column}: seed it and make the deletion job clear it");
        Assert.Contains(("identities", "user_id"), checkedColumns);
        Assert.Contains(("blocks", "blocked_id"), checkedColumns);
        Assert.Contains(("notifications", "actor_id"), checkedColumns);

        await DeleteAndRun(h, w.U);

        foreach (var (table, column) in checkedColumns)
            Assert.True(await Count(cs, table, column, userId) == 0, $"deletion left rows in {table}.{column} still referencing the user");
        Assert.Equal(0, await Count(cs, "users", "id", userId));
        Assert.Equal(0, await Count(cs, "reports", "target_id", userId));
        foreach (var work in new[] { w.UWork })
            Assert.Equal(0, await Count(cs, "reports", "target_id", work));
        Assert.Equal(0, await Count(cs, "reports", "target_id", w.UReplyOnO));
        Assert.Equal(0, await Count(cs, "reports", "target_id", w.OReplyOnU));
        Assert.Equal(0, await Count(cs, "notifications", "work_id", w.UWork));
        Assert.Equal(0, await Count(cs, "notifications", "reply_id", w.UReplyOnO));
        Assert.Equal(0, await Count(cs, "notifications", "reply_id", w.OReplyOnU));
        Assert.Equal(0, await Count(cs, "inquiries", "work_id", w.UWork));
        // The two other users are intact.
        Assert.Equal(1, await Count(cs, "users", "id", w.O.Id));
        Assert.Equal(1, await Count(cs, "users", "id", w.T.Id));
    }

    [Fact]
    public async Task Retained_tables_exist_and_keep_their_rows_when_the_account_is_deleted()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        using var h = NewHost(pg, cs);
        var w = await Seed(h);
        await using (var db = h.Db())
        {
            db.BannedIdentities.Add(new Vela.Api.Data.Entities.BannedIdentity { Provider = "fake", SubjectHash = new string('a', 64), UserRef = w.U.Id, CreatedAt = ProfileTestHost.Now });
            db.AdminActions.Add(new Vela.Api.Data.Entities.AdminAction { Id = Guid.NewGuid(), AdminId = w.O.Id, Action = "ban_user", TargetType = "user", TargetId = w.U.Id, Reason = "x", At = ProfileTestHost.Now });
            db.AdminActions.Add(new Vela.Api.Data.Entities.AdminAction { Id = Guid.NewGuid(), AdminId = w.U.Id, Action = "remove_work", TargetType = "work", TargetId = w.UWork, Reason = "x", At = ProfileTestHost.Now });
            await db.SaveChangesAsync();
        }
        var tables = (await Query(cs, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'")).Select(r => r[0]).ToHashSet();
        foreach (var t in RetainedTables.Keys) Assert.Contains(t, tables);

        await DeleteAndRun(h, w.U);

        Assert.Equal(1, await Count(cs, "banned_identities", "user_ref", w.U.Id));
        Assert.Equal(1, await Count(cs, "admin_actions", "target_id", w.U.Id));
        Assert.Equal(1, await Count(cs, "admin_actions", "admin_id", w.U.Id));
    }

    [Fact]
    public async Task The_not_a_user_reference_list_only_names_columns_that_exist()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var existing = (await Query(cs, "SELECT table_name || '.' || column_name FROM information_schema.columns WHERE table_schema = 'public' AND data_type = 'uuid'"))
            .Select(r => r[0]).ToHashSet();

        foreach (var entry in NotUserReferences.Keys)
            Assert.True(existing.Contains(entry), $"allow-list entry {entry} no longer exists in the schema: remove it");
    }
}
