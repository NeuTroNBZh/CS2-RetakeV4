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

    private static Dictionary<string, List<string>> Contract()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "stats", "views.v1.json")));
        return document.RootElement.GetProperty("views").EnumerateObject()
            .ToDictionary(v => v.Name, v => v.Value.EnumerateArray().Select(c => c.GetString()!).ToList());
    }

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

    [Fact]
    public void ContractViews_AreExactlyTheDefinedViews() =>
        Assert.Equal(Contract().Keys.Order(StringComparer.Ordinal), StatsViews.All.Select(v => v.Name).Order(StringComparer.Ordinal));

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
        foreach (var (view, expected) in Contract())
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
        foreach (var view in StatsViews.All)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {view.Name}";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public void BothDialects_HaveTheSameNumberOfMigrations() =>
        Assert.Equal(StatsSchema.Migrations(StatsDialect.Sqlite).Count, StatsSchema.Migrations(StatsDialect.MySql).Count);

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
        foreach (var (view, expected) in Contract())
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
}
