using MySqlConnector;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public class MySqlMigratorTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RETAKEV4_MYSQL_TEST");

    // Several servers starting together on one database migrate concurrently. MySQL commits DDL implicitly, so the
    // migration transaction alone cannot protect the version row.
    [Fact]
    public async Task ConcurrentMigrations_OnAFreshDatabase_AllSucceed_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var prefix = "mig_" + new string(Guid.NewGuid().ToString("N").Select(c => (char)('a' + (c % 26))).Take(12).ToArray());
        var versionTable = prefix + "_version";
        var migrations = Enumerable.Range(1, 5).Select(i => $"CREATE TABLE IF NOT EXISTS {prefix}_{(char)('a' + i)} (x INT NOT NULL PRIMARY KEY)").ToList();
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                await using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();
                await SqlMigrator.ApplyAsync(connection, migrations, CancellationToken.None, versionTable);
            })));
            await using var check = new MySqlConnection(ConnectionString);
            await check.OpenAsync();
            await using var command = check.CreateCommand();
            command.CommandText = $"SELECT COUNT(*), MAX(version) FROM {versionTable}";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(migrations.Count, reader.GetInt32(1));
        }
        finally
        {
            await using var cleanup = new MySqlConnection(ConnectionString);
            await cleanup.OpenAsync();
            await using var drop = cleanup.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS {versionTable}, {string.Join(", ", Enumerable.Range(1, 5).Select(i => $"{prefix}_{(char)('a' + i)}"))}";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
