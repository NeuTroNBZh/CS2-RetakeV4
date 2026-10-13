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
}
