using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using RetakeV4.Domain.Stats;
using RetakeV4.Persistence;
using RetakeV4.Persistence.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

public sealed class StatsSchemaTests : IDisposable
{
    private static readonly string? MySqlConnectionString = Environment.GetEnvironmentVariable("RETAKEV4_MYSQL_TEST");
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private static Dictionary<string, List<string>> Contract(string file = "views.v1.json")
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "stats", file)));
        return document.RootElement.GetProperty("views").EnumerateObject()
            .ToDictionary(v => v.Name, v => v.Value.EnumerateArray().Select(c => c.GetString()!).ToList());
    }

    private static IEnumerable<KeyValuePair<string, List<string>>> AllContracts() => Contract().Concat(Contract("views.v2.json"));

    private async Task<SqliteConnection> MigratedSqlite()
    {
        var connection = new SqliteConnection($"Data Source={_dir.File("stats.db")}");
        await connection.OpenAsync();
        await SqlMigrator.ApplyAsync(connection, StatsSchema.Migrations(StatsDialect.Sqlite), CancellationToken.None, StatsSchema.VersionTable);
        return connection;
    }

    private static async Task<List<string>> SqliteColumns(DbConnection connection, string view)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{view}') ORDER BY cid";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }
        return columns;
    }

    private static async Task Execute(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    [Fact]
    public void ContractViews_AreExactlyTheDefinedViews() =>
        Assert.Equal(Contract().Keys.Order(StringComparer.Ordinal), StatsViews.Names.Order(StringComparer.Ordinal));

    // The v1 tables are frozen: a new counter needs an ALTER migration, never an edit of the v1 CREATE TABLE.
    [Fact]
    public void FrozenV1Columns_MatchTheCurrentCounters()
    {
        Assert.Equal(StatsCounters.Columns, StatsSchema.V1Counters);
        Assert.Equal(StatsCounters.WeaponColumns, StatsSchema.V1WeaponCounters);
    }

    [Fact]
    public void ContractCounters_FollowStatsCounters()
    {
        var scopes = Contract()["v_stats_player_scopes"];
        Assert.Equal(StatsCounters.Columns, scopes.Skip(3).Take(StatsCounters.Columns.Count));
    }

    [Fact]
    public async Task SqliteViews_MatchTheContract()
    {
        await using var connection = await MigratedSqlite();
        foreach (var (view, expected) in AllContracts())
        {
            Assert.Equal(expected, await SqliteColumns(connection, view));
        }
    }

    [Fact]
    public async Task Migrations_AreIdempotent()
    {
        await using (var connection = await MigratedSqlite())
        {
        }
        await using var again = await MigratedSqlite();
        await using var command = again.CreateCommand();
        command.CommandText = $"SELECT MAX(version) FROM {StatsSchema.VersionTable}";
        Assert.Equal((long)StatsSchema.Migrations(StatsDialect.Sqlite).Count, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task EveryViewIsQueryable_OnAnEmptyDatabase()
    {
        await using var connection = await MigratedSqlite();
        foreach (var view in StatsViews.Names.Concat(StatsViewsV2.Names))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {view}";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task SqliteMigrations_CanBeReappliedStatementByStatement()
    {
        await using var connection = await MigratedSqlite();
        foreach (var migration in StatsSchema.Migrations(StatsDialect.Sqlite).Where(m => m.Column is null))
        {
            await Execute(connection, migration.Sql);
        }
    }

    [Fact]
    public async Task SqliteIndexes_AreCreated()
    {
        await using var connection = await MigratedSqlite();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index'";
        var found = new HashSet<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            found.Add(reader.GetString(0));
        }
        var expected = StatsSchema.Indexes.Values.Concat(StatsSchema.V2Indexes.Values).SelectMany(i => i).Select(i => i.Name);
        Assert.All(expected, name => Assert.Contains(name, found));
    }

    [Fact]
    public void MySqlTables_DeclareTheirIndexesInline()
    {
        var migrations = StatsSchema.Migrations(StatsDialect.MySql);
        Assert.Contains(migrations, m => m.Sql.Contains("KEY ix_stats_kills_victim (victim)", StringComparison.Ordinal));
        Assert.Contains(migrations, m => m.Sql.Contains("KEY ix_stats_player_events_round (round_id)", StringComparison.Ordinal));
        Assert.Contains(migrations, m => m.Sql.Contains("KEY ix_stats_server_events_type (type, at)", StringComparison.Ordinal));
    }

    [Fact]
    public void OnConflict_PerDialect()
    {
        Assert.Equal(
            "ON DUPLICATE KEY UPDATE name = VALUES(name), kills = kills + VALUES(kills)",
            StatsDialect.MySql.OnConflict(new[] { "steam_id" }, new[] { "name" }, new[] { "kills" }));
        Assert.Equal(
            "ON CONFLICT (steam_id) DO UPDATE SET name = excluded.name, kills = kills + excluded.kills",
            StatsDialect.Sqlite.OnConflict(new[] { "steam_id" }, new[] { "name" }, new[] { "kills" }));
    }

    [Fact]
    public void OnConflictKeepLatest_PerDialect()
    {
        Assert.Equal(
            "ON DUPLICATE KEY UPDATE last_seen = GREATEST(last_seen, @at)",
            StatsDialect.MySql.OnConflictKeepLatest(new[] { "name" }, "last_seen", "@at"));
        Assert.Equal(
            "ON CONFLICT (name) DO UPDATE SET last_seen = MAX(last_seen, @at)",
            StatsDialect.Sqlite.OnConflictKeepLatest(new[] { "name" }, "last_seen", "@at"));
    }

    [Fact]
    public void SteamIds_RoundTripInBothDialects()
    {
        const ulong steamId = 76561198000000001UL;
        Assert.Equal(steamId, StatsDialect.Sqlite.ReadSteam(StatsDialect.Sqlite.SteamParam(steamId)));
        Assert.Equal(steamId, StatsDialect.MySql.ReadSteam(StatsDialect.MySql.SteamParam(steamId)));
    }

    [Fact]
    public async Task MySqlViews_MatchTheContract_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(MySqlConnectionString))
        {
            return;
        }
        await using var connection = new MySqlConnection(MySqlConnectionString);
        await connection.OpenAsync();
        await SqlMigrator.ApplyAsync(connection, StatsSchema.Migrations(StatsDialect.MySql), CancellationToken.None, StatsSchema.VersionTable);
        foreach (var (view, expected) in AllContracts())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COLUMN_NAME FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @view ORDER BY ORDINAL_POSITION
                """;
            command.Parameters.AddWithValue("@view", view);
            var columns = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
            Assert.Equal(expected, columns);
        }
    }

    [Fact]
    public void FrozenV2Counters_MatchStatsCounters() => Assert.Equal(StatsCounters.V2Columns, StatsSchema.V2Counters);

    [Fact]
    public void V1Entries_AreUnchanged_AndComeFirst()
    {
        foreach (var dialect in new[] { StatsDialect.Sqlite, StatsDialect.MySql })
        {
            var all = StatsSchema.Migrations(dialect);
            Assert.True(StatsSchema.V1Count(dialect) < all.Count);
            Assert.All(all.Take(StatsSchema.V1Count(dialect)), m => Assert.Null(m.Column));
            Assert.All(all.Take(StatsSchema.V1Count(dialect)), m => Assert.DoesNotContain("stats_player_events", m.Sql, StringComparison.Ordinal));
            Assert.StartsWith("ALTER TABLE stats_player_totals ADD COLUMN shots_still", all[StatsSchema.V1Count(dialect)].Sql, StringComparison.Ordinal);
        }
    }

    // A crash after an ALTER and before its version row replays the list: nothing may fail.
    [Fact]
    public async Task FullList_ReappliedFromVersionZero_Succeeds()
    {
        await using var connection = await MigratedSqlite();
        await Execute(connection, $"DELETE FROM {StatsSchema.VersionTable}");
        await SqlMigrator.ApplyAsync(connection, StatsSchema.Migrations(StatsDialect.Sqlite), CancellationToken.None, StatsSchema.VersionTable);
        Assert.Equal((long)StatsSchema.Migrations(StatsDialect.Sqlite).Count, await Scalar(connection, $"SELECT MAX(version) FROM {StatsSchema.VersionTable}"));
    }

    // A 4.8 database stops after the v1 entries; 4.9 adds the rest without losing rows.
    [Fact]
    public async Task V1Database_UpgradesToV2_KeepingItsRows()
    {
        await using var connection = new SqliteConnection($"Data Source={_dir.File("stats.db")}");
        await connection.OpenAsync();
        var all = StatsSchema.Migrations(StatsDialect.Sqlite);
        await SqlMigrator.ApplyAsync(connection, all.Take(StatsSchema.V1Count(StatsDialect.Sqlite)).ToList(), CancellationToken.None, StatsSchema.VersionTable);
        await Execute(connection,
            "INSERT INTO stats_players (steam_id, name, first_seen, last_seen, elo) VALUES (1, 'old', '2026-10-01 00:00:00.000', '2026-10-01 00:00:00.000', 1010)");
        await Execute(connection, "INSERT INTO stats_player_totals (steam_id, scope, rounds, kills) VALUES (1, 'all', 7, 3)");
        await SqlMigrator.ApplyAsync(connection, all, CancellationToken.None, StatsSchema.VersionTable);
        Assert.Equal(3L, await Scalar(connection, "SELECT kills FROM v_stats_players WHERE steam_id = 1"));
        Assert.Equal(0L, await Scalar(connection, "SELECT shots_still FROM stats_player_totals WHERE steam_id = 1"));
        Assert.Null(await Scalar(connection, "SELECT country FROM stats_players WHERE steam_id = 1"));
        Assert.Contains("seconds_vip", await SqliteColumns(connection, "stats_sessions"));
        Assert.Contains("still", await SqliteColumns(connection, "stats_shots"));
        Assert.Contains("health_end", await SqliteColumns(connection, "stats_round_players"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM stats_player_events"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM stats_presence"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM stats_server_events"));
        Assert.Equal(3L, await Scalar(connection, "SELECT kills FROM v2_stats_players WHERE steam_id = 1"));
        Assert.Equal(0L, await Scalar(connection, "SELECT shots_still FROM v2_stats_players WHERE steam_id = 1"));
        Assert.Null(await Scalar(connection, "SELECT still_accuracy FROM v2_stats_players WHERE steam_id = 1"));
    }

    [Fact]
    public void ContractV2Views_AreExactlyTheV2Views() =>
        Assert.Equal(Contract("views.v2.json").Keys.Order(StringComparer.Ordinal), StatsViewsV2.Names.Order(StringComparer.Ordinal));

    [Fact]
    public void V2Views_NeverReuseAV1Name() => Assert.Empty(StatsViewsV2.Names.Intersect(StatsViews.Names));

    [Fact]
    public void ContractV2Counters_FollowAllCounters()
    {
        var scopes = Contract("views.v2.json")["v2_stats_player_scopes"];
        Assert.Equal(StatsCounters.AllColumns, scopes.Skip(3).Take(StatsCounters.AllColumns.Count));
    }

    [Fact]
    public async Task MySqlMigrations_ReappliedFromVersionZero_Succeed_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(MySqlConnectionString))
        {
            return;
        }
        await using var connection = new MySqlConnection(MySqlConnectionString);
        await connection.OpenAsync();
        var migrations = StatsSchema.Migrations(StatsDialect.MySql);
        await SqlMigrator.ApplyAsync(connection, migrations, CancellationToken.None, StatsSchema.VersionTable);
        await Execute(connection, $"DELETE FROM {StatsSchema.VersionTable}");
        await SqlMigrator.ApplyAsync(connection, migrations, CancellationToken.None, StatsSchema.VersionTable);
        Assert.Equal((long)migrations.Count, Convert.ToInt64(await Scalar(connection, $"SELECT MAX(version) FROM {StatsSchema.VersionTable}")));
    }

    [Fact]
    public void DeleteBatchBefore_PerDialect()
    {
        Assert.Equal("DELETE FROM stats_presence WHERE at < @cutoff LIMIT 500", StatsDialect.MySql.DeleteBatchBefore("stats_presence", 500));
        Assert.Equal(
            "DELETE FROM stats_presence WHERE id IN (SELECT id FROM stats_presence WHERE at < @cutoff LIMIT 500)",
            StatsDialect.Sqlite.DeleteBatchBefore("stats_presence", 500));
    }
}
