using MySqlConnector;
using RetakeV4.Domain.Stats;
using RetakeV4.Persistence.Stats;
using static RetakeV4.Integration.Tests.Persistence.Stats.StatsSamples;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

[Collection(StatsMySqlCollection.Name)]
public class MySqlStatsRepositoryTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RETAKEV4_MYSQL_TEST");

    [Fact]
    public async Task RoundTrip_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var repository = StatsRepositories.MySql(ConnectionString, new StatsWriteOptions($"test-{Guid.NewGuid():N}"[..20], 1000, 100));
        await repository.InitializeAsync(CancellationToken.None);
        await repository.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var before = (await repository.LoadEloAsync(new[] { Ct }, CancellationToken.None)).GetValueOrDefault(Ct, 1000);
        var mapId = await repository.OpenMapAsync("de_mirage", now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(now, ctElo: before), CancellationToken.None);
        await repository.WriteRoundAsync(mapId, RoundV2(now), CancellationToken.None);
        var session = await repository.OpenSessionAsync(mapId, Ct, "ct", now, CancellationToken.None);
        await repository.SetCountryAsync(session, Ct, "FR", CancellationToken.None);
        await repository.CloseSessionAsync(session, mapId, Ct, now.AddMinutes(1), 0, new Playtime(60, 0, 0, 0) { SecondsVip = 5 },
            new SessionActivity(40, 60), CancellationToken.None);
        await repository.WritePresenceAsync(new PresenceSnapshot(now, "de_mirage", 2, 0, 0), CancellationToken.None);
        await repository.WriteServerEventsAsync(new[] { new ServerEvent(now, ServerEventTypes.Chat, null, "say", 4), new ServerEvent(now, ServerEventTypes.Uptime, null, "600") }, CancellationToken.None);
        await repository.CloseMapAsync(mapId, now.AddMinutes(2), CancellationToken.None);
        var after = (await repository.LoadEloAsync(new[] { Ct }, CancellationToken.None))[Ct];
        Assert.NotEqual(before, after);
        await repository.PurgeRawAsync(now.AddDays(-3650), 500, CancellationToken.None);
    }

    // Several servers starting together on one database: the migration lock and the replayable ALTERs keep it safe.
    [Fact]
    public async Task ConcurrentInitialize_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var repositories = Enumerable.Range(0, 4)
            .Select(i => StatsRepositories.MySql(ConnectionString, new StatsWriteOptions($"conc-{i}", 1000, 100)))
            .ToList();
        await Task.WhenAll(repositories.Select(r => Task.Run(() => r.InitializeAsync(CancellationToken.None))));
    }

    [Fact]
    public async Task ServerLastSeen_OnlyMovesForward_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var key = $"seen-{Guid.NewGuid():N}"[..20];
        var repository = StatsRepositories.MySql(ConnectionString, new StatsWriteOptions(key, 1000, 100));
        await repository.InitializeAsync(CancellationToken.None);
        var now = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);
        await repository.OpenMapAsync("de_mirage", now, CancellationToken.None);
        await repository.OpenMapAsync("de_nuke", now.AddDays(-1), CancellationToken.None);
        await repository.WritePresenceAsync(new PresenceSnapshot(now.AddHours(1), "de_nuke", 1, 0, 0), CancellationToken.None);
        Assert.Equal(now.UtcDateTime, await LastSeen(key));
        await repository.OpenMapAsync("de_anubis", now.AddHours(2), CancellationToken.None);
        Assert.Equal(now.AddHours(2).UtcDateTime, await LastSeen(key));
    }

    private static async Task<DateTime> LastSeen(string key)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_seen FROM stats_servers WHERE name = @name";
        command.Parameters.AddWithValue("@name", key);
        return Convert.ToDateTime(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Movement_RoundTrip_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var repository = StatsRepositories.MySql(ConnectionString, new StatsWriteOptions($"test-{Guid.NewGuid():N}"[..20], 1000, 100));
        await repository.InitializeAsync(CancellationToken.None);
        // Fresh ids: the shared test database keeps the totals of earlier runs.
        var ids = Enumerable.Range(0, 3).Select(_ => 76561190000000000UL + (ulong)Random.Shared.NextInt64(1, 9_000_000_000)).ToArray();
        var now = DateTimeOffset.UtcNow;
        var mapId = await repository.OpenMapAsync("de_mirage", now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, RoundMovement(now, ids[0], ids[1], ids[2]), CancellationToken.None);
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        async Task<object?> Scalar(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();
            return value is DBNull ? null : value;
        }
        Assert.Equal(6.35, Convert.ToDouble(await Scalar($"SELECT distance_m FROM v2_stats_round_players WHERE steam_id = {ids[0]}"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1L, Convert.ToInt64(await Scalar($"SELECT engage_order FROM v2_stats_round_players WHERE steam_id = {ids[0]}"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(await Scalar($"SELECT bait FROM v2_stats_round_players WHERE steam_id = {ids[0]}"));
        Assert.Equal(100.0, Convert.ToDouble(await Scalar($"SELECT run_pct FROM v2_stats_players WHERE steam_id = {ids[0]}"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0.5, Convert.ToDouble(await Scalar($"SELECT slowness_index FROM v_stats_player_behaviour WHERE steam_id = {ids[0]}"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(2.0, Convert.ToDouble(await Scalar($"SELECT slowness_index FROM v_stats_player_behaviour WHERE steam_id = {ids[1]}"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(await Scalar($"SELECT slowness_index FROM v_stats_player_behaviour WHERE steam_id = {ids[2]}"));
    }

    // Movement off (StatsRules.Movement = null): the 13 round columns stay NULL, the 16 counters stay 0, rounds still count.
    [Fact]
    public async Task MovementOff_RoundTrip_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var repository = StatsRepositories.MySql(ConnectionString, new StatsWriteOptions($"test-{Guid.NewGuid():N}"[..20], 1000, 100));
        await repository.InitializeAsync(CancellationToken.None);
        // Fresh ids: the shared test database keeps the totals of earlier runs.
        var ids = Enumerable.Range(0, 3).Select(_ => 76561190000000000UL + (ulong)Random.Shared.NextInt64(1, 9_000_000_000)).ToArray();
        var now = DateTimeOffset.UtcNow;
        var mapId = await repository.OpenMapAsync("de_mirage", now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, RoundMovement(now, ids[0], ids[1], ids[2], Rules), CancellationToken.None);
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        async Task<long> Count(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }
        var list = string.Join(", ", ids);
        Assert.Equal(3L, await Count($"SELECT COUNT(*) FROM stats_round_players WHERE steam_id IN ({list})"));
        Assert.Equal(0L, await Count(MovementRoundColumnsSet(list)));
        Assert.True(await Count($"SELECT COUNT(*) FROM stats_player_totals WHERE steam_id IN ({list})") >= 3);
        Assert.Equal(0L, await Count(MovementCountersSet(list)));
        Assert.Equal(1L, await Count($"SELECT rounds FROM stats_player_totals WHERE steam_id = {ids[0]} AND scope = 'all'"));
    }

    [Fact]
    public async Task SpecialKills_RoundTrip_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var repository = StatsRepositories.MySql(ConnectionString, new StatsWriteOptions($"test-{Guid.NewGuid():N}"[..20], 1000, 100));
        await repository.InitializeAsync(CancellationToken.None);
        // Fresh ids: the shared test database keeps the totals of earlier runs.
        var ids = Enumerable.Range(0, 4).Select(_ => 76561190000000000UL + (ulong)Random.Shared.NextInt64(1, 9_000_000_000)).ToArray();
        var now = DateTimeOffset.UtcNow;
        var mapId = await repository.OpenMapAsync("de_mirage", now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, RoundSpecialKills(now, ids[0], ids[1], ids[2], ids[3]), CancellationToken.None);
        await repository.WriteRoundAsync(mapId, RoundSpecialKills(now.AddMinutes(1), ids[0], ids[1], ids[2], ids[3]), CancellationToken.None);
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        async Task<object?> Scalar(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();
            return value is DBNull ? null : value;
        }
        await AssertSpecialKills(Scalar, ids[0], ids[1], ids[2]);
    }
}
