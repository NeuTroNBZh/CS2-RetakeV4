using MySqlConnector;
using RetakeV4.Persistence.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

// The replay scenarios on MySQL 8 / MariaDB 11.4. Same collection as StatsSchemaTests (whose reapply test drops and
// recreates every view): never in parallel on the shared database. The two-server scenario runs its claims concurrently.
[Collection(StatsMySqlCollection.Name)]
public class MySqlReplayTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RETAKEV4_MYSQL_TEST");

    private static async Task<(SqlStatsRepository Repository, string Key)> Repository()
    {
        var key = DerivedViewScenarios.NewKey("rp");
        var repository = StatsRepositories.MySql(ConnectionString!, new StatsWriteOptions(key, 1000, 100));
        await repository.InitializeAsync(CancellationToken.None);
        return (repository, key);
    }

    private static async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    [Fact]
    public async Task BestKeepsTheStrongest_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var (repository, key) = await Repository();
        await ReplayScenarios.BestKeepsTheStrongest(repository, key, Scalar);
    }

    [Fact]
    public async Task HighlightsAndSavedReplaceTheOldest_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        await ReplayScenarios.HighlightsAndSavedReplaceTheOldest((await Repository()).Repository, Scalar);
    }

    [Fact]
    public async Task SharedReplay_StaysWhileOwned_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        await ReplayScenarios.SharedReplayStaysWhileOwned((await Repository()).Repository, Scalar);
    }

    [Fact]
    public async Task TwoServers_KeepTheCap_Concurrently_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        await ReplayScenarios.TwoServersKeepTheCap((await Repository()).Repository, (await Repository()).Repository, Scalar, concurrent: true);
    }

    // Two players sharing one round evict it at once from two connections: no ownerless replay, no deadlock nor
    // duplicate-key error.
    [Fact]
    public async Task SharedRound_EvictedConcurrently_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        await ReplayScenarios.SharedRoundEvictedConcurrently((await Repository()).Repository, (await Repository()).Repository, Scalar);
    }

    [Fact]
    public async Task LongFrames_RoundTrip_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        await ReplayScenarios.LongFramesRoundTrip((await Repository()).Repository, Scalar);
    }

    [Fact]
    public async Task UnknownPlayersAndNoClaims_StoreNothing_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        await ReplayScenarios.UnknownPlayersAndNoClaims_StoreNothing((await Repository()).Repository, Scalar);
    }
}
