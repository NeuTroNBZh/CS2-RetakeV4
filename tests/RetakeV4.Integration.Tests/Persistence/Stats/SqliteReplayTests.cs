using Microsoft.Data.Sqlite;
using RetakeV4.Domain.Stats;
using RetakeV4.Persistence.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

public sealed class SqliteReplayTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private string DbFile => _dir.File("data/retakev4.db");

    private async Task<SqlStatsRepository> Repository(string serverKey = "eu-1")
    {
        var repository = StatsRepositories.Sqlite(DbFile, new StatsWriteOptions(serverKey, 1000, 100));
        await repository.InitializeAsync(CancellationToken.None);
        return repository;
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DbFile}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    [Fact]
    public async Task BestKeepsTheStrongest() => await ReplayScenarios.BestKeepsTheStrongest(await Repository(), "eu-1", Scalar);

    [Fact]
    public async Task HighlightsAndSavedReplaceTheOldest() => await ReplayScenarios.HighlightsAndSavedReplaceTheOldest(await Repository(), Scalar);

    [Fact]
    public async Task SharedReplay_StaysWhileOwned() => await ReplayScenarios.SharedReplayStaysWhileOwned(await Repository(), Scalar);

    [Fact]
    public async Task TwoServers_KeepTheCap() =>
        await ReplayScenarios.TwoServersKeepTheCap(await Repository("eu-1"), await Repository("eu-2"), Scalar, concurrent: false);

    // Two writers on one file at once: each replay transaction takes the write lock when it begins, so the decisions
    // never interleave (a deferred transaction would fail its read-to-write upgrade with SQLITE_BUSY).
    [Fact]
    public async Task TwoServers_KeepTheCap_Concurrently() =>
        await ReplayScenarios.TwoServersKeepTheCap(await Repository("eu-1"), await Repository("eu-2"), Scalar, concurrent: true);

    [Fact]
    public async Task SharedRound_EvictedConcurrently() =>
        await ReplayScenarios.SharedRoundEvictedConcurrently(await Repository("eu-1"), await Repository("eu-2"), Scalar);

    [Fact]
    public async Task SeveralClaims_InOneCall() => await ReplayScenarios.SeveralClaimsInOneCall(await Repository(), Scalar);

    [Fact]
    public async Task FailedWrite_RollsBack() => await ReplayScenarios.FailedWrite_RollsBack(await Repository(), Scalar);

    [Fact]
    public async Task LongFrames_RoundTrip() => await ReplayScenarios.LongFramesRoundTrip(await Repository(), Scalar);

    [Fact]
    public async Task UnknownPlayersAndNoClaims_StoreNothing() => await ReplayScenarios.UnknownPlayersAndNoClaims_StoreNothing(await Repository(), Scalar);

    // RawRetentionDays never removes a replay: the round's shots go, its frames stay.
    [Fact]
    public async Task Purge_KeepsReplays()
    {
        var repository = await Repository();
        var at = new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);
        var map = await repository.OpenMapAsync("de_mirage", at, CancellationToken.None);
        var ids = DerivedViewScenarios.NewIds(3);
        var round = await ReplayScenarios.WriteRound(repository, map, at, ids);
        await ReplayScenarios.Claim(repository, round, ids[0], ReplayReason.Saved, 1.0, new ReplayCaps(10, 10, 20), at);
        Assert.Equal(2L, await Scalar($"SELECT COUNT(*) FROM v_stats_shots WHERE round_id = {round.RoundId}"));

        await repository.PurgeRawAsync(at.AddDays(1), 500, CancellationToken.None);

        Assert.Equal(0L, await Scalar($"SELECT COUNT(*) FROM v_stats_shots WHERE round_id = {round.RoundId}"));
        Assert.Equal(1L, await Scalar($"SELECT COUNT(*) FROM v_stats_replays WHERE round_id = {round.RoundId}"));
        Assert.Equal(3L, await Scalar($"SELECT COUNT(*) FROM v_stats_replay_tracks WHERE round_id = {round.RoundId}"));
    }
}
