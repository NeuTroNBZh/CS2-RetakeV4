using Microsoft.Data.Sqlite;
using RetakeV4.Domain.Stats;
using RetakeV4.Persistence.Stats;
using static RetakeV4.Integration.Tests.Persistence.Stats.StatsSamples;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

public sealed class SqliteStatsRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);
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
    public async Task Initialize_TwiceIsHarmless()
    {
        await Repository();
        await Repository();
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM v_stats_players"));
    }

    [Fact]
    public async Task WriteRound_FillsRawTablesAndViews()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        var roundId = await repository.WriteRoundAsync(mapId, Round(Now), CancellationToken.None);
        Assert.Equal("eu-1", await Scalar($"SELECT server FROM v_stats_rounds WHERE round_id = {roundId}"));
        Assert.Equal("A", await Scalar("SELECT plant_site FROM v_stats_rounds"));
        Assert.Equal(2L, await Scalar("SELECT COUNT(*) FROM v_stats_round_players"));
        Assert.Equal(1L, await Scalar("SELECT COUNT(*) FROM v_stats_shots"));
        Assert.Equal(1L, await Scalar("SELECT COUNT(*) FROM v_stats_damages"));
        Assert.Equal(4.0, await Scalar("SELECT victim_x FROM v_stats_kills"));
        Assert.Equal(1L, await Scalar("SELECT COUNT(*) FROM v_stats_grenades WHERE type = 'Flash'"));
        Assert.Equal(1L, await Scalar($"SELECT kills FROM v_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(100.0, await Scalar($"SELECT adr FROM v_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(100.0, await Scalar($"SELECT accuracy FROM v_stats_players WHERE steam_id = {Ct}"));
        Assert.Null(await Scalar($"SELECT kd FROM v_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(1L, await Scalar($"SELECT plants FROM v_stats_players WHERE steam_id = {T}"));
        Assert.Equal("m4a1", await Scalar($"SELECT weapon FROM v_stats_player_weapons WHERE steam_id = {Ct}"));
    }

    [Fact]
    public async Task Aggregates_AddUp_AcrossRoundsAndScopes()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(Now), CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(Now.AddMinutes(1)), CancellationToken.None);
        Assert.Equal(2L, await Scalar($"SELECT rounds FROM v_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(2L, await Scalar($"SELECT kills FROM v_stats_players WHERE steam_id = {Ct}"));
        foreach (var scope in new[] { "map:de_mirage", "site:A", "roundtype:FullBuy", "side:CT" })
        {
            Assert.Equal(2L, await Scalar($"SELECT kills FROM v_stats_player_scopes WHERE steam_id = {Ct} AND scope = '{scope}'"));
        }
        Assert.Equal(2L, await Scalar($"SELECT kills FROM v_stats_player_weapons WHERE steam_id = {Ct}"));
    }

    [Fact]
    public async Task RankedRound_UpdatesEloAndHistory()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(Now), CancellationToken.None);
        Assert.Equal(1012L, await Scalar($"SELECT elo FROM v_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(988L, await Scalar($"SELECT elo FROM v_stats_players WHERE steam_id = {T}"));
        Assert.Equal(1012L, await Scalar($"SELECT elo FROM v_stats_elo_history WHERE steam_id = {Ct} AND day = '2026-10-13'"));
        Assert.Equal(1L, await Scalar($"SELECT steam_id IS NOT NULL FROM v_stats_leaderboard WHERE steam_id = {Ct}"));
    }

    [Fact]
    public async Task UnrankedRound_KeepsEloAndHistoryEmpty()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(Now, ranked: false), CancellationToken.None);
        Assert.Equal(1000L, await Scalar($"SELECT elo FROM v_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM v_stats_elo_history"));
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM v_stats_leaderboard"));
    }

    [Fact]
    public async Task TwoServers_SharingTheDatabase_AddTheirEloDeltas()
    {
        var first = await Repository("eu-1");
        var second = await Repository("eu-2");
        await first.WriteRoundAsync(await first.OpenMapAsync("de_mirage", Now, CancellationToken.None), Round(Now), CancellationToken.None);
        // The second server still believes the player is at 1000: its delta is applied relatively.
        await second.WriteRoundAsync(await second.OpenMapAsync("de_inferno", Now, CancellationToken.None), Round(Now), CancellationToken.None);
        Assert.Equal(1024L, await Scalar($"SELECT elo FROM v_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(2L, await Scalar("SELECT COUNT(*) FROM stats_servers"));
    }

    [Fact]
    public async Task Sessions_RecordPlaytime()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        var session = await repository.OpenSessionAsync(mapId, Ct, "ct", Now, CancellationToken.None);
        await repository.CloseSessionAsync(session, mapId, Ct, Now.AddMinutes(10), 2, new Playtime(300, 200, 60, 40), CancellationToken.None);
        await repository.CloseSessionAsync(null, mapId, Ct, Now.AddMinutes(30), null, new Playtime(10, 0, 0, 0), CancellationToken.None);
        Assert.Equal(2L, await Scalar("SELECT COUNT(*) FROM v_stats_sessions"));
        Assert.Equal(300L, await Scalar($"SELECT seconds_ct FROM v_stats_sessions WHERE session_id = {session}"));
        Assert.Equal(2L, await Scalar($"SELECT disconnect_reason FROM v_stats_sessions WHERE session_id = {session}"));
        Assert.Equal(610L, await Scalar($"SELECT seconds_total FROM v_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(2L, await Scalar($"SELECT sessions FROM v_stats_players WHERE steam_id = {Ct}"));
    }

    [Fact]
    public async Task CloseMap_SetsTheEnd()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.CloseMapAsync(mapId, Now.AddMinutes(40), CancellationToken.None);
        Assert.Equal("2026-10-13 20:40:00.000", await Scalar($"SELECT ended_at FROM v_stats_maps WHERE map_id = {mapId}"));
    }

    [Fact]
    public async Task Purge_DropsOldShotsAndDamagesOnly()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(Now.AddDays(-40)), CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(Now), CancellationToken.None);
        var deleted = await repository.PurgeRawAsync(Now.AddDays(-30), 1, CancellationToken.None);
        Assert.Equal(2, deleted);
        Assert.Equal(1L, await Scalar("SELECT COUNT(*) FROM v_stats_shots"));
        Assert.Equal(1L, await Scalar("SELECT COUNT(*) FROM v_stats_damages"));
        Assert.Equal(2L, await Scalar("SELECT COUNT(*) FROM v_stats_kills"));
        Assert.Equal(0, await repository.PurgeRawAsync(Now.AddDays(-30), 1, CancellationToken.None));
    }

    [Fact]
    public async Task Names_AreStoredVerbatim()
    {
        const string name = "O'Brien \"x\" 🔥; DROP TABLE stats_players; --";
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(Now, ctName: name), CancellationToken.None);
        Assert.Equal(name, await Scalar($"SELECT name FROM v_stats_players WHERE steam_id = {Ct}"));
    }

    [Fact]
    public async Task LoadElo_ReturnsKnownPlayersOnly()
    {
        var repository = await Repository();
        Assert.Empty(await repository.LoadEloAsync(Array.Empty<ulong>(), CancellationToken.None));
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(Now), CancellationToken.None);
        var elo = await repository.LoadEloAsync(new[] { Ct, 42UL }, CancellationToken.None);
        Assert.Equal(1012, elo[Ct]);
        Assert.False(elo.ContainsKey(42UL));
    }
}
