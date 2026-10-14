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
        await repository.CloseSessionAsync(session, mapId, Ct, Now.AddMinutes(10), 2, new Playtime(300, 200, 60, 40), SessionActivity.None, CancellationToken.None);
        await repository.CloseSessionAsync(null, mapId, Ct, Now.AddMinutes(30), null, new Playtime(10, 0, 0, 0), SessionActivity.None, CancellationToken.None);
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
    public async Task LongTexts_AreClippedToTheColumnSize()
    {
        var name = new string('n', 200);
        var longText = new string('w', 100);
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync(longText, Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(Now, ctName: name, weapon: longText), CancellationToken.None);
        Assert.Equal(name[..128], await Scalar($"SELECT name FROM v_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(longText[..64], await Scalar("SELECT map_name FROM v_stats_maps"));
        Assert.Equal(longText[..64], await Scalar("SELECT weapon FROM v_stats_shots"));
        Assert.Equal(longText[..64], await Scalar($"SELECT weapon FROM v_stats_player_weapons WHERE steam_id = {Ct}"));
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

    [Fact]
    public async Task WriteRound_V2_FillsTheNewColumnsEventsAndAggregates()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, RoundV2(Now), CancellationToken.None);
        Assert.Equal(1L, await Scalar($"SELECT shots_still FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(1L, await Scalar($"SELECT hits_still FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(87L, await Scalar($"SELECT health_end FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(50L, await Scalar($"SELECT armor_end FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(0L, await Scalar($"SELECT health_end FROM v2_stats_round_players WHERE steam_id = {T}"));
        Assert.Equal(100L, await Scalar($"SELECT damage_taken FROM v2_stats_round_players WHERE steam_id = {T}"));
        Assert.Equal(20.0, await Scalar("SELECT speed FROM v2_stats_shots"));
        Assert.Equal(1L, await Scalar("SELECT crouched FROM v2_stats_shots"));
        Assert.Equal(0L, await Scalar("SELECT scoped FROM v2_stats_shots"));
        Assert.Equal(1L, await Scalar("SELECT still FROM v2_stats_shots"));
        Assert.Equal("reload_full_clip", await Scalar($"SELECT type FROM v_stats_player_events WHERE steam_id = {Ct}"));
        Assert.Equal("m4a1:20/30", await Scalar($"SELECT value FROM v_stats_player_events WHERE steam_id = {Ct}"));
        Assert.Equal("B", await Scalar($"SELECT site FROM v_stats_player_events WHERE steam_id = {T}"));
        Assert.Equal(1L, await Scalar($"SELECT reloads_full FROM v2_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(1L, await Scalar($"SELECT clutch_defuses FROM v2_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(100.0, await Scalar($"SELECT still_accuracy FROM v2_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(100L, await Scalar($"SELECT damage_taken FROM v2_stats_players WHERE steam_id = {T}"));
        Assert.Equal(1L, await Scalar($"SELECT jumps FROM v2_stats_player_scopes WHERE steam_id = {T} AND scope = 'site:B'"));
    }

    [Fact]
    public async Task Sessions_RecordNetworkVipAndCountry()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        var session = await repository.OpenSessionAsync(mapId, Ct, "ct", Now, CancellationToken.None);
        await repository.SetCountryAsync(session, Ct, "FR", CancellationToken.None);
        await repository.CloseSessionAsync(session, mapId, Ct, Now.AddMinutes(10), 0, new Playtime(300, 0, 0, 60) { SecondsVip = 45 },
            new SessionActivity(42, 80), CancellationToken.None);
        await repository.CloseSessionAsync(null, mapId, Ct, Now.AddMinutes(30), null, new Playtime(10, 0, 0, 0), new SessionActivity(60, 120), CancellationToken.None);
        Assert.Equal("FR", await Scalar($"SELECT country FROM v2_stats_sessions WHERE session_id = {session}"));
        Assert.Equal(45L, await Scalar($"SELECT seconds_vip FROM v2_stats_sessions WHERE session_id = {session}"));
        Assert.Equal(42L, await Scalar($"SELECT ping_avg FROM v2_stats_sessions WHERE session_id = {session}"));
        Assert.Equal(80L, await Scalar($"SELECT ping_max FROM v2_stats_sessions WHERE session_id = {session}"));
        Assert.Equal("FR", await Scalar($"SELECT country FROM v_stats_player_network WHERE steam_id = {Ct}"));
        Assert.Equal(51.0, await Scalar($"SELECT ping_avg FROM v_stats_player_network WHERE steam_id = {Ct}"));
        Assert.Equal(120L, await Scalar($"SELECT ping_max FROM v_stats_player_network WHERE steam_id = {Ct}"));
        Assert.Equal(45L, await Scalar($"SELECT seconds_vip FROM v_stats_player_network WHERE steam_id = {Ct}"));
        Assert.Equal(2L, await Scalar($"SELECT sessions_with_ping FROM v_stats_player_network WHERE steam_id = {Ct}"));
    }

    [Fact]
    public async Task PresenceAndServerEvents_AreWritten_BeforeAnyMap()
    {
        var repository = await Repository("eu-2");
        await repository.WriteServerEventsAsync(new[]
        {
            new ServerEvent(Now, ServerEventTypes.PluginStart, null, "4.8.1"), new ServerEvent(Now, ServerEventTypes.SlowHandler, "Hud", "tick", 3, 120),
        }, CancellationToken.None);
        await repository.WritePresenceAsync(new PresenceSnapshot(Now, "de_nuke", 8, 2, 1), CancellationToken.None);
        Assert.Equal("eu-2", await Scalar("SELECT server FROM v_stats_server_health WHERE type = 'plugin_start'"));
        Assert.Equal("4.8.1", await Scalar("SELECT value FROM v_stats_server_health WHERE type = 'plugin_start'"));
        Assert.Equal(3L, await Scalar("SELECT occurrences FROM v_stats_server_health WHERE type = 'slow_handler'"));
        Assert.Equal(120L, await Scalar("SELECT max_ms FROM v_stats_server_health WHERE type = 'slow_handler'"));
        Assert.Equal(8L, await Scalar("SELECT humans FROM v_stats_presence WHERE map_name = 'de_nuke'"));
        Assert.Equal(1L, await Scalar("SELECT queued FROM v_stats_presence WHERE map_name = 'de_nuke'"));
        Assert.Equal(1L, await Scalar("SELECT COUNT(*) FROM stats_servers"));
    }

    [Fact]
    public async Task Purge_AlsoDropsOldEventsPresenceAndServerEvents()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, RoundV2(Now.AddDays(-40)), CancellationToken.None);
        await repository.WriteRoundAsync(mapId, RoundV2(Now), CancellationToken.None);
        await repository.WritePresenceAsync(new PresenceSnapshot(Now.AddDays(-40), "de_mirage", 1, 0, 0), CancellationToken.None);
        await repository.WritePresenceAsync(new PresenceSnapshot(Now, "de_mirage", 1, 0, 0), CancellationToken.None);
        await repository.WriteServerEventsAsync(new[] { new ServerEvent(Now.AddDays(-40), ServerEventTypes.Uptime, null, "600") }, CancellationToken.None);
        await repository.WriteServerEventsAsync(new[] { new ServerEvent(Now, ServerEventTypes.Uptime, null, "600") }, CancellationToken.None);
        // Old round: 1 shot, 1 damage, 2 player events; plus 1 presence snapshot and 1 server event.
        Assert.Equal(6, await repository.PurgeRawAsync(Now.AddDays(-30), 1, CancellationToken.None));
        Assert.Equal(2L, await Scalar("SELECT COUNT(*) FROM v_stats_player_events"));
        Assert.Equal(1L, await Scalar("SELECT COUNT(*) FROM v_stats_presence"));
        Assert.Equal(1L, await Scalar("SELECT COUNT(*) FROM v_stats_server_health"));
        Assert.Equal(2L, await Scalar("SELECT COUNT(*) FROM v2_stats_round_players WHERE steam_id = " + Ct));
        Assert.Equal(0, await repository.PurgeRawAsync(Now.AddDays(-30), 1, CancellationToken.None));
    }

    [Fact]
    public async Task Purge_OfTimeTables_RunsEvenWithoutOldRounds()
    {
        var repository = await Repository();
        await repository.WritePresenceAsync(new PresenceSnapshot(Now.AddDays(-40), "de_mirage", 1, 0, 0), CancellationToken.None);
        Assert.Equal(1, await repository.PurgeRawAsync(Now.AddDays(-30), 10, CancellationToken.None));
    }

    [Fact]
    public async Task ServerEventsBatch_WritesEveryRow_Clipped()
    {
        var repository = await Repository();
        var longValue = new string('v', 200);
        await repository.WriteServerEventsAsync(new[]
        {
            new ServerEvent(Now, ServerEventTypes.Chat, null, "say", 4),
            new ServerEvent(Now, ServerEventTypes.Command, new string('m', 100), longValue),
            new ServerEvent(Now, new string('t', 40), null, null),
        }, CancellationToken.None);
        Assert.Equal(3L, await Scalar("SELECT COUNT(*) FROM stats_server_events"));
        Assert.Equal(4L, await Scalar("SELECT occurrences FROM stats_server_events WHERE type = 'chat'"));
        Assert.Equal(longValue[..128], await Scalar("SELECT value FROM stats_server_events WHERE type = 'command'"));
        Assert.Equal(new string('m', 64), await Scalar("SELECT module FROM stats_server_events WHERE type = 'command'"));
        Assert.Equal(new string('t', 24), await Scalar("SELECT type FROM stats_server_events WHERE value IS NULL"));
    }

    [Fact]
    public async Task EmptyServerEventsBatch_WritesNothing()
    {
        var repository = await Repository();
        await repository.WriteServerEventsAsync(Array.Empty<ServerEvent>(), CancellationToken.None);
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM stats_server_events"));
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM stats_servers"));
    }

    // Servers sharing a database, or a queue replaying an older write, must never move last_seen back.
    [Fact]
    public async Task ServerLastSeen_OnlyMovesForward()
    {
        var repository = await Repository();
        await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.OpenMapAsync("de_nuke", Now.AddDays(-1), CancellationToken.None);
        Assert.Equal("2026-10-13 20:00:00.000", await Scalar("SELECT last_seen FROM stats_servers"));
        var other = await Repository();
        await other.OpenMapAsync("de_inferno", Now.AddHours(-2), CancellationToken.None);
        Assert.Equal("2026-10-13 20:00:00.000", await Scalar("SELECT last_seen FROM stats_servers"));
        await repository.OpenMapAsync("de_anubis", Now.AddHours(1), CancellationToken.None);
        Assert.Equal("2026-10-13 21:00:00.000", await Scalar("SELECT last_seen FROM stats_servers"));
        Assert.Equal(1L, await Scalar("SELECT COUNT(*) FROM stats_servers"));
    }

    // The server id is resolved once per repository: telemetry does not upsert stats_servers on every write.
    [Fact]
    public async Task Telemetry_ReusesTheResolvedServer_WithoutTouchingIt()
    {
        var repository = await Repository();
        await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WritePresenceAsync(new PresenceSnapshot(Now.AddHours(1), "de_mirage", 1, 0, 0), CancellationToken.None);
        await repository.WriteServerEventsAsync(new[] { new ServerEvent(Now.AddHours(2), ServerEventTypes.Uptime, null, "600") }, CancellationToken.None);
        Assert.Equal("2026-10-13 20:00:00.000", await Scalar("SELECT last_seen FROM stats_servers"));
        Assert.Equal(1L, await Scalar("SELECT COUNT(DISTINCT server_id) FROM stats_presence"));
        Assert.Equal(await Scalar("SELECT id FROM stats_servers"), await Scalar("SELECT server_id FROM stats_server_events"));
    }

    [Fact]
    public async Task Telemetry_BeforeAnyMap_ResolvesTheServerOnce()
    {
        var repository = await Repository("eu-3");
        await repository.WritePresenceAsync(new PresenceSnapshot(Now, "de_mirage", 1, 0, 0), CancellationToken.None);
        await repository.WritePresenceAsync(new PresenceSnapshot(Now.AddMinutes(1), "de_mirage", 1, 0, 0), CancellationToken.None);
        Assert.Equal(1L, await Scalar("SELECT COUNT(*) FROM stats_servers WHERE name = 'eu-3'"));
        Assert.Equal("2026-10-13 20:00:00.000", await Scalar("SELECT last_seen FROM stats_servers"));
    }

    [Fact]
    public async Task WriteRound_Movement_FillsTheRoundColumnsAggregatesAndBehaviour()
    {
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, RoundMovement(Now, Ct, CtMate, T), CancellationToken.None);
        Assert.Equal(29000L, await Scalar($"SELECT alive_ms FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(100.0, await Scalar($"SELECT run_pct FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(0.0, await Scalar($"SELECT in_smoke_pct FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(6.35, await Scalar($"SELECT distance_m FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(0L, await Scalar($"SELECT afk_ms FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(500L, await Scalar($"SELECT first_contact_ms FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(1L, await Scalar($"SELECT engage_order FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Null(await Scalar($"SELECT bait FROM v2_stats_round_players WHERE steam_id = {Ct}"));
        Assert.Equal(100.0, await Scalar($"SELECT still_pct FROM v2_stats_round_players WHERE steam_id = {CtMate}"));
        Assert.Equal(2L, await Scalar($"SELECT engage_order FROM v2_stats_round_players WHERE steam_id = {CtMate}"));
        Assert.Null(await Scalar($"SELECT first_contact_ms FROM v2_stats_round_players WHERE steam_id = {T}"));
        Assert.Equal(3L, await Scalar($"SELECT alive_samples FROM v2_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(635L, await Scalar($"SELECT distance_cm FROM v2_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(100.0, await Scalar($"SELECT run_pct FROM v2_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(13.138, await Scalar($"SELECT distance_m_per_min FROM v2_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(500.0, await Scalar($"SELECT avg_first_contact_ms FROM v2_stats_players WHERE steam_id = {Ct}"));
        Assert.Equal(1L, await Scalar($"SELECT first_contact_rounds FROM v2_stats_player_scopes WHERE steam_id = {Ct} AND scope = 'site:A'"));
        Assert.Equal(0.5, await Scalar($"SELECT slowness_index FROM v_stats_player_behaviour WHERE steam_id = {Ct}"));
        Assert.Equal(2.0, await Scalar($"SELECT slowness_index FROM v_stats_player_behaviour WHERE steam_id = {CtMate}"));
        Assert.Null(await Scalar($"SELECT slowness_index FROM v_stats_player_behaviour WHERE steam_id = {T}"));
    }

    // Movement off (StatsRules.Movement = null): the 13 round columns stay NULL, the 16 counters stay 0, rounds still count.
    [Fact]
    public async Task WriteRound_MovementOff_LeavesTheRoundColumnsNullAndTheCountersZero()
    {
        Assert.Equal(13, StatsSchema.MovementRoundColumns.Count);
        Assert.Equal(16, StatsSchema.MovementCounters.Count);
        var repository = await Repository();
        var mapId = await repository.OpenMapAsync("de_mirage", Now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, RoundMovement(Now, Ct, CtMate, T, Rules), CancellationToken.None);
        var ids = $"{Ct}, {CtMate}, {T}";
        Assert.Equal(3L, await Scalar($"SELECT COUNT(*) FROM stats_round_players WHERE steam_id IN ({ids})"));
        Assert.Equal(0L, await Scalar(MovementRoundColumnsSet(ids)));
        Assert.True((long)(await Scalar($"SELECT COUNT(*) FROM stats_player_totals WHERE steam_id IN ({ids})"))! >= 3);
        Assert.Equal(0L, await Scalar(MovementCountersSet(ids)));
        Assert.Equal(1L, await Scalar($"SELECT rounds FROM stats_player_totals WHERE steam_id = {Ct} AND scope = 'all'"));
    }
}
