using Microsoft.Data.Sqlite;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public sealed class SqlMigratorTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    [Fact]
    public async Task TwoVersionTables_TrackTheirMigrationsIndependently()
    {
        await using var connection = new SqliteConnection($"Data Source={_dir.File("db.sqlite")}");
        await connection.OpenAsync();
        await SqlMigrator.ApplyAsync(connection, new[] { "CREATE TABLE a (x INTEGER)" }, CancellationToken.None);
        await SqlMigrator.ApplyAsync(connection, new[] { "CREATE TABLE b (x INTEGER)", "CREATE TABLE c (x INTEGER)" }, CancellationToken.None, "stats_schema_version");
        // Re-running both lists applies nothing new: each table remembers its own count.
        await SqlMigrator.ApplyAsync(connection, new[] { "CREATE TABLE a (x INTEGER)" }, CancellationToken.None);
        await SqlMigrator.ApplyAsync(connection, new[] { "CREATE TABLE b (x INTEGER)", "CREATE TABLE c (x INTEGER)" }, CancellationToken.None, "stats_schema_version");
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT MAX(version) FROM schema_version) || '/' || (SELECT MAX(version) FROM stats_schema_version)";
        Assert.Equal("1/2", (string?)await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task VersionTable_MustBeASimpleIdentifier()
    {
        await using var connection = new SqliteConnection($"Data Source={_dir.File("db.sqlite")}");
        await connection.OpenAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            SqlMigrator.ApplyAsync(connection, Array.Empty<string>(), CancellationToken.None, "x; DROP TABLE y"));
    }

    private static async Task<List<string>> Columns(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }
        return columns;
    }

    [Fact]
    public void AddColumn_BuildsTheAlter()
    {
        var migration = SqlMigration.AddColumn("stats_shots", "speed", "REAL NULL");
        Assert.Equal("ALTER TABLE stats_shots ADD COLUMN speed REAL NULL", migration.Sql);
        Assert.Equal("stats_shots", migration.Table);
        Assert.Equal("speed", migration.Column);
        Assert.Null(new SqlMigration("CREATE TABLE x (y INTEGER)").Column);
    }

    [Theory]
    [InlineData("t; DROP TABLE x", "y")]
    [InlineData("t", "y INTEGER, z")]
    [InlineData("T", "y")]
    [InlineData("", "y")]
    public void AddColumn_RejectsUnsafeIdentifiers(string table, string column) =>
        Assert.Throws<ArgumentException>(() => SqlMigration.AddColumn(table, column, "INTEGER NULL"));

    // A crash between the ALTER and its version row replays the step on the next start: it must not fail.
    [Fact]
    public async Task AddColumn_IsSkipped_WhenTheColumnAlreadyExists()
    {
        await using var connection = new SqliteConnection($"Data Source={_dir.File("db.sqlite")}");
        await connection.OpenAsync();
        var migrations = new[]
        {
            new SqlMigration("CREATE TABLE IF NOT EXISTS t (x INTEGER)"),
            SqlMigration.AddColumn("t", "y", "INTEGER NOT NULL DEFAULT 0"),
        };
        await SqlMigrator.ApplyAsync(connection, migrations, CancellationToken.None, "mig_version");
        await using (var reset = connection.CreateCommand())
        {
            reset.CommandText = "DELETE FROM mig_version";
            await reset.ExecuteNonQueryAsync();
        }
        Assert.Equal(2, await SqlMigrator.ApplyAsync(connection, migrations, CancellationToken.None, "mig_version"));
        Assert.Equal(new[] { "x", "y" }, await Columns(connection, "t"));
        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT MAX(version) FROM mig_version";
        Assert.Equal(2L, (long)(await version.ExecuteScalarAsync())!);
    }
}
