using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using RetakeV4.Domain.Stats;
using RetakeV4.Persistence;
using RetakeV4.Persistence.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

[Collection(StatsMySqlCollection.Name)]
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
        var expected = StatsSchema.Indexes.Values.Concat(StatsSchema.V2Indexes.Values).Concat(StatsSchema.ReplayIndexes.Values).SelectMany(i => i).Select(i => i.Name);
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
        await Execute(connection, "INSERT INTO stats_sessions (steam_id, map_id, seconds_ct) VALUES (1, 1, 120)");
        await Execute(connection, """
            INSERT INTO stats_round_players (round_id, steam_id, side, survived, won, kills, deaths, assists, flash_assists, team_kills,
                damage, utility_damage, headshot_kills, shots, hits, headshot_hits, entry_kill, entry_death, trade_kills, traded_death,
                kast, clutch_vs, clutch_won, enemies_flashed, teammates_flashed, blind_seconds_inflicted, mvp, planted, defused,
                defused_with_kit, rating, elo_before, elo_after)
            VALUES (1, 1, 'CT', 1, 1, 2, 0, 0, 0, 0, 150, 0, 1, 10, 4, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1.2, 1000, 1010)
            """);
        await Execute(connection, "INSERT INTO stats_shots (round_id, steam_id, weapon, time_ms, tick) VALUES (1, 1, 'ak47', 500, 64)");
        await SqlMigrator.ApplyAsync(connection, all, CancellationToken.None, StatsSchema.VersionTable);
        Assert.Equal((long)all.Count, await Scalar(connection, $"SELECT MAX(version) FROM {StatsSchema.VersionTable}"));
        Assert.Equal(120L, await Scalar(connection, "SELECT seconds_ct FROM stats_sessions WHERE steam_id = 1"));
        Assert.Equal(0L, await Scalar(connection, "SELECT seconds_vip FROM stats_sessions WHERE steam_id = 1"));
        Assert.Null(await Scalar(connection, "SELECT ping_avg FROM stats_sessions WHERE steam_id = 1"));
        Assert.Equal(150L, await Scalar(connection, "SELECT damage FROM stats_round_players WHERE steam_id = 1"));
        Assert.Equal(0L, await Scalar(connection, "SELECT shots_still FROM stats_round_players WHERE steam_id = 1"));
        Assert.Null(await Scalar(connection, "SELECT health_end FROM stats_round_players WHERE steam_id = 1"));
        Assert.Equal("ak47", await Scalar(connection, "SELECT weapon FROM stats_shots WHERE steam_id = 1"));
        Assert.Null(await Scalar(connection, "SELECT speed FROM stats_shots WHERE steam_id = 1"));
        Assert.Null(await Scalar(connection, "SELECT still FROM stats_shots WHERE steam_id = 1"));
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

    // Golden SHA-256 of the v1 (4.8) entries as shipped: SqlMigrator applies entries by index, so editing one (or anything it
    // is built from: dialect, counters, v1 views) would silently diverge from deployed databases. Line endings are normalised
    // because the raw string literals follow the checkout's line endings.
    [Theory]
    [InlineData("mysql", "c784bb35ad3a7f949b4fd469aaa1238fd54e20de5b0d28b236538bac1f2227c4")]
    [InlineData("sqlite", "1f2404e470aa1c978689ed3ce610fdf0645f56035cdb5d13867a51aa01f82f6c")]
    public void V1Entries_MatchTheShippedSnapshot(string dialect, string expected)
    {
        var d = dialect == "mysql" ? StatsDialect.MySql : StatsDialect.Sqlite;
        var sql = string.Join("\n-- next --\n", StatsSchema.Migrations(d).Take(StatsSchema.V1Count(d)).Select(m => m.Sql.ReplaceLineEndings("\n")));
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
    }

    // No IP address is stored: no view (v1 or v2) exposes a column named after one.
    [Fact]
    public async Task NoView_HasAnIpAddressColumn()
    {
        await using var connection = await MigratedSqlite();
        foreach (var view in StatsViews.Names.Concat(StatsViewsV2.Names))
        {
            var columns = await SqliteColumns(connection, view);
            Assert.NotEmpty(columns);
            Assert.All(columns, column => Assert.DoesNotContain(
                column.Split('_'), token => token.Equals("ip", StringComparison.OrdinalIgnoreCase) || token.Equals("address", StringComparison.OrdinalIgnoreCase)));
        }
    }

    // Golden SHA-256 of the 4.9-a entries (sub-project 1). They are applied by index on databases already upgraded, so
    // they must never change; a later change to a v2 view is a new step (DROP VIEW + CREATE VIEW).
    [Theory]
    [InlineData("mysql", 51, "ac693523dee83b29be6ecc9270fc3d2e450c7d4505d1d7f6e6913946a310d6a9")]
    [InlineData("sqlite", 56, "8250fe10bdc052ede23356cdb89aa89e582feba74e7d121b414c9e4e3b0a3d34")]
    public void V2EventsEntries_MatchTheSnapshot(string dialect, int count, string expected)
    {
        var d = dialect == "mysql" ? StatsDialect.MySql : StatsDialect.Sqlite;
        var entries = StatsSchema.Migrations(d).Skip(StatsSchema.V1Count(d)).Take(StatsSchema.V2EventsCount(d) - StatsSchema.V1Count(d)).ToList();
        Assert.Equal(count, entries.Count);
        var sql = string.Join("\n-- next --\n", entries.Select(m => m.Sql.ReplaceLineEndings("\n")));
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
    }

    // Golden SHA-256 of the 4.9-b entries (sub-project 2), built from the frozen StatsViewsV2.Movement definitions: a later
    // change to a v2 view is a new step and must leave these entries untouched.
    [Theory]
    [InlineData("mysql", 37, "60a47b8b5aac323cbdda4f0e5b38d1fdd99a0f31a1f02290e9b5a064d6dcbfa7")]
    [InlineData("sqlite", 37, "aec6e965547a13dd56b6e080f633f788a49ba7a86b82ccb27a55eba1f9db9f82")]
    public void V2MovementEntries_MatchTheSnapshot(string dialect, int count, string expected)
    {
        var d = dialect == "mysql" ? StatsDialect.MySql : StatsDialect.Sqlite;
        var entries = StatsSchema.Migrations(d).Skip(StatsSchema.V2EventsCount(d)).Take(StatsSchema.V2MovementCount(d) - StatsSchema.V2EventsCount(d)).ToList();
        Assert.Equal(count, entries.Count);
        var sql = string.Join("\n-- next --\n", entries.Select(m => m.Sql.ReplaceLineEndings("\n")));
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
    }

    // Golden SHA-256 of the 4.9-c entries (sub-project 4), built from StatsSchema.SpecialKillCounters and the frozen
    // StatsViewsV2.Catalog* and StatsDerivedViews definitions: a later change to one of these views is a new step and
    // must leave these entries untouched.
    [Theory]
    [InlineData("mysql", 28, "95c8ae9393a809db1f7a72914684b133890618d16cde3861c1e3cd9015cb67d6")]
    [InlineData("sqlite", 28, "742066f096cf5a2a9566ccaee92d5544c84343ee0a4d3468994cceb24a1490b7")]
    public void V2CatalogEntries_MatchTheSnapshot(string dialect, int count, string expected)
    {
        var d = dialect == "mysql" ? StatsDialect.MySql : StatsDialect.Sqlite;
        var entries = StatsSchema.Migrations(d).Skip(StatsSchema.V2MovementCount(d)).Take(StatsSchema.V2CatalogCount(d) - StatsSchema.V2MovementCount(d)).ToList();
        Assert.Equal(count, entries.Count);
        var sql = string.Join("\n-- next --\n", entries.Select(m => m.Sql.ReplaceLineEndings("\n")));
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
    }

    // Every NOT NULL column of the v1 stats_round_players, zeros except survived, won, kast, rating and ELO.
    private static Task InsertRoundPlayer(DbConnection connection, long roundId, long steamId, string side) => Execute(connection, $"""
        INSERT INTO stats_round_players (round_id, steam_id, side, survived, won, kills, deaths, assists, flash_assists, team_kills,
            damage, utility_damage, headshot_kills, shots, hits, headshot_hits, entry_kill, entry_death, trade_kills, traded_death,
            kast, clutch_vs, clutch_won, enemies_flashed, teammates_flashed, blind_seconds_inflicted, mvp, planted, defused,
            defused_with_kit, rating, elo_before, elo_after)
        VALUES ({roundId}, {steamId}, '{side}', 1, 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            1,
            0, 0, 0, 0, 0, 0, 0, 0, 0,
            1.0, 1000, 1000)
        """);

    private static Task InsertPlayer(DbConnection connection, long steamId) => Execute(connection,
        $"INSERT INTO stats_players (steam_id, name, first_seen, last_seen, elo) VALUES ({steamId}, 'p{steamId}', '2026-10-01 00:00:00.000', '2026-10-01 00:00:00.000', 1000)");

    [Fact]
    public void FrozenMovementCounters_MatchStatsCounters() => Assert.Equal(StatsCounters.MovementColumns, StatsSchema.MovementCounters);

    [Fact]
    public void MovementEntries_FollowTheEventsEntries()
    {
        foreach (var dialect in new[] { StatsDialect.Sqlite, StatsDialect.MySql })
        {
            var movement = StatsSchema.Migrations(dialect)
                .Skip(StatsSchema.V2EventsCount(dialect))
                .Take(StatsSchema.V2MovementCount(dialect) - StatsSchema.V2EventsCount(dialect))
                .ToList();
            Assert.Equal($"ALTER TABLE stats_player_totals ADD COLUMN alive_ms {dialect.BigInt} NOT NULL DEFAULT 0", movement[0].Sql);
            Assert.Equal(StatsSchema.MovementCounters.Count + StatsSchema.MovementRoundColumns.Count, movement.Count(m => m.Column is not null));
            Assert.True(movement.FindLastIndex(m => m.Column is not null) < movement.FindIndex(m => m.Column is null));
            foreach (var view in StatsViewsV2.MovementViews)
            {
                var drop = movement.FindIndex(m => m.Sql == $"DROP VIEW IF EXISTS {view}");
                Assert.True(drop >= 0, view);
                Assert.StartsWith($"{dialect.CreateView} {view} AS", movement[drop + 1].Sql, StringComparison.Ordinal);
            }
        }
    }

    // A 4.9-a database (sub-project 1 only) gets the movement columns and the recreated views without losing rows.
    [Fact]
    public async Task V2EventsDatabase_UpgradesToMovement_KeepingItsRows()
    {
        await using var connection = new SqliteConnection($"Data Source={_dir.File("stats.db")}");
        await connection.OpenAsync();
        var all = StatsSchema.Migrations(StatsDialect.Sqlite);
        await SqlMigrator.ApplyAsync(connection, all.Take(StatsSchema.V2EventsCount(StatsDialect.Sqlite)).ToList(), CancellationToken.None, StatsSchema.VersionTable);
        await InsertPlayer(connection, 1);
        await Execute(connection, "INSERT INTO stats_player_totals (steam_id, scope, rounds, reloads) VALUES (1, 'all', 7, 2)");
        await InsertRoundPlayer(connection, 1, 1, "CT");
        Assert.DoesNotContain("alive_ms", await SqliteColumns(connection, "v2_stats_players"));
        await SqlMigrator.ApplyAsync(connection, all, CancellationToken.None, StatsSchema.VersionTable);
        Assert.Equal((long)all.Count, await Scalar(connection, $"SELECT MAX(version) FROM {StatsSchema.VersionTable}"));
        Assert.Contains("alive_ms", await SqliteColumns(connection, "v2_stats_players"));
        Assert.Contains("bait_rate", await SqliteColumns(connection, "v2_stats_player_scopes"));
        Assert.Contains("first_contact_ms", await SqliteColumns(connection, "v2_stats_round_players"));
        Assert.Equal(2L, await Scalar(connection, "SELECT reloads FROM v2_stats_players WHERE steam_id = 1"));
        Assert.Equal(0L, await Scalar(connection, "SELECT alive_samples FROM v2_stats_players WHERE steam_id = 1"));
        Assert.Null(await Scalar(connection, "SELECT walk_pct FROM v2_stats_players WHERE steam_id = 1"));
        Assert.Null(await Scalar(connection, "SELECT first_contact_ms FROM stats_round_players WHERE steam_id = 1"));
        Assert.Null(await Scalar(connection, "SELECT bait FROM stats_round_players WHERE steam_id = 1"));
        Assert.Equal(1L, await Scalar(connection, "SELECT COUNT(*) FROM v_stats_player_behaviour"));
        Assert.Equal(0L, await Scalar(connection, "SELECT bait_chances FROM v_stats_player_behaviour WHERE steam_id = 1"));
    }

    // Player 1 vs teammate 2 on rounds 1 and 2; player 3 has no teammate in contact; player 4 never made contact.
    [Fact]
    public async Task BehaviourView_ComparesFirstContactWithTeammates_OnTheSameRounds()
    {
        await using var connection = await MigratedSqlite();
        foreach (var id in new long[] { 1, 2, 3, 4 })
        {
            await InsertPlayer(connection, id);
        }
        foreach (var (round, id, side, contact) in new (long, long, string, int?)[]
        {
            (1, 1, "CT", 1000), (1, 2, "CT", 3000), (1, 3, "T", 2000), (2, 1, "CT", 2000), (2, 2, "CT", 2000), (2, 4, "CT", null),
        })
        {
            await InsertRoundPlayer(connection, round, id, side);
            if (contact is { } ms)
            {
                await Execute(connection, $"UPDATE stats_round_players SET first_contact_ms = {ms} WHERE round_id = {round} AND steam_id = {id}");
            }
        }
        await Execute(connection, """
            INSERT INTO stats_player_totals (steam_id, scope, first_contact_rounds, first_contact_ms_sum, engage_order_sum, bait_rounds,
                bait_chances, alive_samples, behind_smoke_samples, in_smoke_samples)
            VALUES (1, 'all', 2, 3000, 2, 1, 4, 200, 50, 10)
            """);
        Assert.Equal(2L, await Scalar(connection, "SELECT compared_rounds FROM v_stats_player_behaviour WHERE steam_id = 1"));
        Assert.Equal(1500.0, await Scalar(connection, "SELECT own_first_contact_ms FROM v_stats_player_behaviour WHERE steam_id = 1"));
        Assert.Equal(2500.0, await Scalar(connection, "SELECT team_first_contact_ms FROM v_stats_player_behaviour WHERE steam_id = 1"));
        Assert.Equal(0.6, await Scalar(connection, "SELECT slowness_index FROM v_stats_player_behaviour WHERE steam_id = 1"));
        Assert.Equal(1.667, await Scalar(connection, "SELECT slowness_index FROM v_stats_player_behaviour WHERE steam_id = 2"));
        Assert.Equal(1500.0, await Scalar(connection, "SELECT avg_first_contact_ms FROM v_stats_player_behaviour WHERE steam_id = 1"));
        Assert.Equal(1.0, await Scalar(connection, "SELECT avg_engage_order FROM v_stats_player_behaviour WHERE steam_id = 1"));
        Assert.Equal(0.25, await Scalar(connection, "SELECT bait_rate FROM v_stats_player_behaviour WHERE steam_id = 1"));
        Assert.Equal(25.0, await Scalar(connection, "SELECT behind_smoke_pct FROM v_stats_player_behaviour WHERE steam_id = 1"));
        Assert.Equal(5.0, await Scalar(connection, "SELECT in_smoke_pct FROM v_stats_player_behaviour WHERE steam_id = 1"));
        Assert.Equal(0L, await Scalar(connection, "SELECT compared_rounds FROM v_stats_player_behaviour WHERE steam_id = 3"));
        Assert.Null(await Scalar(connection, "SELECT slowness_index FROM v_stats_player_behaviour WHERE steam_id = 3"));
        Assert.Null(await Scalar(connection, "SELECT bait_rate FROM v_stats_player_behaviour WHERE steam_id = 3"));
        Assert.Equal(0L, await Scalar(connection, "SELECT bait_chances FROM v_stats_player_behaviour WHERE steam_id = 3"));
        Assert.Equal(0L, await Scalar(connection, "SELECT compared_rounds FROM v_stats_player_behaviour WHERE steam_id = 4"));
    }

    [Fact]
    public void IntCast_PerDialect()
    {
        Assert.Equal("CAST(SUM(x) AS SIGNED)", StatsDialect.MySql.IntCast("SUM(x)"));
        Assert.Equal("CAST(SUM(x) AS INTEGER)", StatsDialect.Sqlite.IntCast("SUM(x)"));
    }

    // 4.9-c: the special kill columns (totals, then round players), then one DROP VIEW + CREATE VIEW pair per view of
    // CatalogViews, right after the 4.9-b entries.
    [Fact]
    public void CatalogEntries_FollowTheMovementEntries()
    {
        foreach (var dialect in new[] { StatsDialect.Sqlite, StatsDialect.MySql })
        {
            var all = StatsSchema.Migrations(dialect);
            var catalog = all.Skip(StatsSchema.V2MovementCount(dialect)).Take(StatsSchema.V2CatalogCount(dialect) - StatsSchema.V2MovementCount(dialect)).ToList();
            var columns = StatsSchema.SpecialKillCounters.Count;
            Assert.Equal((2 * columns) + (2 * StatsViewsV2.CatalogViews.Count), catalog.Count);
            for (var i = 0; i < columns; i++)
            {
                var column = StatsSchema.SpecialKillCounters[i];
                Assert.Equal($"ALTER TABLE stats_player_totals ADD COLUMN {column} {dialect.BigInt} NOT NULL DEFAULT 0", catalog[i].Sql);
                Assert.Equal($"ALTER TABLE stats_round_players ADD COLUMN {column} {dialect.Int} NOT NULL DEFAULT 0", catalog[columns + i].Sql);
            }
            var views = catalog.Skip(2 * columns).ToList();
            Assert.All(views, m => Assert.Null(m.Column));
            for (var i = 0; i < StatsViewsV2.CatalogViews.Count; i++)
            {
                var view = StatsViewsV2.CatalogViews[i];
                Assert.Equal($"DROP VIEW IF EXISTS {view}", views[2 * i].Sql);
                Assert.StartsWith($"{dialect.CreateView} {view} AS", views[(2 * i) + 1].Sql, StringComparison.Ordinal);
            }
        }
    }

    // A 4.9-b database (sub-project 2) gets the derived views without losing rows.
    [Fact]
    public async Task V2MovementDatabase_UpgradesToCatalog_KeepingItsRows()
    {
        await using var connection = new SqliteConnection($"Data Source={_dir.File("stats.db")}");
        await connection.OpenAsync();
        var all = StatsSchema.Migrations(StatsDialect.Sqlite);
        await SqlMigrator.ApplyAsync(connection, all.Take(StatsSchema.V2MovementCount(StatsDialect.Sqlite)).ToList(), CancellationToken.None, StatsSchema.VersionTable);
        var seed = new ViewSeed(connection, StatsDialect.Sqlite);
        var server = await seed.Server("eu-1");
        var map = await seed.Map(server, "de_mirage", ViewSeed.Utc("2026-10-01 20:00:00"));
        await seed.Round(map, new RoundSeed(ViewSeed.Utc("2026-10-01 20:01:00"), PlayersCt: 3, PlayersT: 2, Winner: "CT"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'view' AND name = 'v_stats_side_balance'"));
        await SqlMigrator.ApplyAsync(connection, all, CancellationToken.None, StatsSchema.VersionTable);
        Assert.Equal((long)all.Count, await Scalar(connection, $"SELECT MAX(version) FROM {StatsSchema.VersionTable}"));
        Assert.Equal(1L, await Scalar(connection, "SELECT COUNT(*) FROM v_stats_rounds"));
        Assert.Equal(1L, await Scalar(connection, "SELECT rounds FROM v_stats_side_balance WHERE server = 'eu-1'"));
    }

    [Fact]
    public void UtcDayAndHour_PerDialect()
    {
        Assert.Equal("DATE_FORMAT(pr.at, '%Y-%m-%d')", StatsDialect.MySql.UtcDay("pr.at"));
        Assert.Equal("SUBSTR(pr.at, 1, 10)", StatsDialect.Sqlite.UtcDay("pr.at"));
        Assert.Equal("HOUR(pr.at)", StatsDialect.MySql.UtcHour("pr.at"));
        Assert.Equal("CAST(SUBSTR(pr.at, 12, 2) AS INTEGER)", StatsDialect.Sqlite.UtcHour("pr.at"));
    }

    [Fact]
    public void FrozenSpecialKillCounters_MatchStatsCounters() => Assert.Equal(StatsCounters.SpecialKillColumns, StatsSchema.SpecialKillCounters);

    // A 4.9-b database gets the special kill columns (0 on its rows) and the recreated player views, keeping its rows.
    [Fact]
    public async Task V2MovementDatabase_UpgradesToSpecialKills_KeepingItsRows()
    {
        await using var connection = new SqliteConnection($"Data Source={_dir.File("stats.db")}");
        await connection.OpenAsync();
        var all = StatsSchema.Migrations(StatsDialect.Sqlite);
        await SqlMigrator.ApplyAsync(connection, all.Take(StatsSchema.V2MovementCount(StatsDialect.Sqlite)).ToList(), CancellationToken.None, StatsSchema.VersionTable);
        await InsertPlayer(connection, 1);
        await Execute(connection, "INSERT INTO stats_player_totals (steam_id, scope, rounds, kills, bait_rounds) VALUES (1, 'all', 7, 3, 2)");
        await InsertRoundPlayer(connection, 1, 1, "CT");
        Assert.DoesNotContain("noscope_kills", await SqliteColumns(connection, "v2_stats_players"));
        await SqlMigrator.ApplyAsync(connection, all, CancellationToken.None, StatsSchema.VersionTable);
        Assert.Equal((long)all.Count, await Scalar(connection, $"SELECT MAX(version) FROM {StatsSchema.VersionTable}"));
        foreach (var column in StatsSchema.SpecialKillCounters)
        {
            Assert.Equal(0L, await Scalar(connection, $"SELECT {column} FROM v2_stats_players WHERE steam_id = 1"));
            Assert.Equal(0L, await Scalar(connection, $"SELECT {column} FROM v2_stats_player_scopes WHERE steam_id = 1 AND scope = 'all'"));
            Assert.Equal(0L, await Scalar(connection, $"SELECT {column} FROM stats_round_players WHERE steam_id = 1"));
            Assert.Contains(column, await SqliteColumns(connection, "v2_stats_round_players"));
        }
        Assert.Equal(3L, await Scalar(connection, "SELECT kills FROM v2_stats_players WHERE steam_id = 1"));
        Assert.Equal(2L, await Scalar(connection, "SELECT bait_rounds FROM v2_stats_players WHERE steam_id = 1"));
        Assert.Equal(0.0, await Scalar(connection, "SELECT noscope_kill_pct FROM v2_stats_players WHERE steam_id = 1"));
        Assert.Equal(1L, await Scalar(connection, "SELECT kast FROM stats_round_players WHERE steam_id = 1"));
    }

    [Fact]
    public void ReplayDialect_PerDialect()
    {
        Assert.Equal("MEDIUMTEXT", StatsDialect.MySql.LongText);
        Assert.Equal("TEXT", StatsDialect.Sqlite.LongText);
        Assert.Equal(" FOR UPDATE", StatsDialect.MySql.ForUpdate);
        Assert.Equal(string.Empty, StatsDialect.Sqlite.ForUpdate);
        var at = new DateTimeOffset(2026, 10, 14, 20, 15, 0, 123, TimeSpan.Zero);
        Assert.Equal(at, StatsDialect.Sqlite.ReadDate(StatsDialect.Sqlite.DateParam(at)));
        Assert.Equal(at, StatsDialect.MySql.ReadDate(StatsDialect.MySql.DateParam(at)));
    }

    // 4.9-d: the three replay tables, the SQLite index of stats_replay_owners, then one DROP VIEW + CREATE VIEW pair per
    // replay view, after the 4.9-c entries, and nothing after them yet.
    [Fact]
    public void ReplayEntries_FollowTheCatalogEntries()
    {
        foreach (var dialect in new[] { StatsDialect.Sqlite, StatsDialect.MySql })
        {
            var all = StatsSchema.Migrations(dialect);
            Assert.Equal(all.Count, StatsSchema.V2ReplaysCount(dialect));
            var replays = all.Skip(StatsSchema.V2CatalogCount(dialect)).ToList();
            var tables = new[] { "stats_replays", "stats_replay_owners", "stats_replay_tracks" };
            for (var i = 0; i < tables.Length; i++)
            {
                Assert.StartsWith($"CREATE TABLE IF NOT EXISTS {tables[i]} (", replays[i].Sql, StringComparison.Ordinal);
            }
            var indexes = dialect.IsMySql ? 0 : 1;
            if (!dialect.IsMySql)
            {
                Assert.Equal("CREATE INDEX IF NOT EXISTS ix_stats_replay_owners_steam ON stats_replay_owners (steam_id, reason)", replays[3].Sql);
            }
            var views = replays.Skip(tables.Length + indexes).ToList();
            Assert.Equal(2 * StatsReplayViews.Names.Count, views.Count);
            for (var i = 0; i < StatsReplayViews.Names.Count; i++)
            {
                var view = StatsReplayViews.Names[i];
                Assert.Equal($"DROP VIEW IF EXISTS {view}", views[2 * i].Sql);
                Assert.StartsWith($"{dialect.CreateView} {view} AS", views[(2 * i) + 1].Sql, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void MySqlReplayTables_UseMediumTextAndAnInlineKey()
    {
        var migrations = StatsSchema.Migrations(StatsDialect.MySql);
        Assert.Contains(migrations, m => m.Sql.Contains("frames MEDIUMTEXT NOT NULL", StringComparison.Ordinal));
        Assert.Contains(migrations, m => m.Sql.Contains("KEY ix_stats_replay_owners_steam (steam_id, reason)", StringComparison.Ordinal));
    }

    // A 4.9-c database (sub-project 4) gets the replay tables and views without losing rows.
    [Fact]
    public async Task V2CatalogDatabase_UpgradesToReplays_KeepingItsRows()
    {
        await using var connection = new SqliteConnection($"Data Source={_dir.File("stats.db")}");
        await connection.OpenAsync();
        var all = StatsSchema.Migrations(StatsDialect.Sqlite);
        await SqlMigrator.ApplyAsync(connection, all.Take(StatsSchema.V2CatalogCount(StatsDialect.Sqlite)).ToList(), CancellationToken.None, StatsSchema.VersionTable);
        var seed = new ViewSeed(connection, StatsDialect.Sqlite);
        var server = await seed.Server("eu-1");
        var map = await seed.Map(server, "de_mirage", ViewSeed.Utc("2026-10-01 20:00:00"));
        await seed.Round(map, new RoundSeed(ViewSeed.Utc("2026-10-01 20:01:00"), PlayersCt: 3, PlayersT: 2, Winner: "CT"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'stats_replays'"));
        await SqlMigrator.ApplyAsync(connection, all, CancellationToken.None, StatsSchema.VersionTable);
        Assert.Equal((long)all.Count, await Scalar(connection, $"SELECT MAX(version) FROM {StatsSchema.VersionTable}"));
        Assert.Equal(1L, await Scalar(connection, "SELECT COUNT(*) FROM v_stats_rounds"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM v_stats_replays"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM v_stats_replay_tracks"));
    }
}
