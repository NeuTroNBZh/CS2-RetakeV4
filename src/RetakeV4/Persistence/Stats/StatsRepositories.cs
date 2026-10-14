using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace RetakeV4.Persistence.Stats;

public static class StatsRepositories
{
    public static IStatsRepository Create(DatabaseConfig database, string baseDirectory, StatsWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.Type switch
        {
            DatabaseType.MySql => MySql(database.MySqlConnectionString, options),
            DatabaseType.Sqlite => Sqlite(Path.Combine(baseDirectory, database.SqliteFile), options),
            _ => new NoOpStatsRepository(),
        };
    }

    public static SqlStatsRepository MySql(string connectionString, StatsWriteOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new SqlStatsRepository(StatsDialect.MySql, async ct =>
        {
            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }, options);
    }

    // WAL lets the preference store and the stats writer use the same file without blocking each other.
    public static SqlStatsRepository Sqlite(string file, StatsWriteOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = file, DefaultTimeout = 5 }.ToString();
        return new SqlStatsRepository(StatsDialect.Sqlite, async ct =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return connection;
        }, options);
    }
}
