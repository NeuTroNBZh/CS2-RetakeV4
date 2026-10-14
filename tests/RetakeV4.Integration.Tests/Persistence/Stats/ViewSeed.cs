using System.Data.Common;
using System.Globalization;
using RetakeV4.Persistence.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

// A round as the derived-view tests need it; PlantTimeMs null = no plant.
internal sealed record RoundSeed(
    DateTimeOffset StartedAt,
    string? Site = "A",
    string? RoundType = "FullBuy",
    int PlayersCt = 0,
    int PlayersT = 0,
    bool Ranked = true,
    string? Winner = "CT",
    int? PlantTimeMs = null,
    bool Defused = false,
    bool Exploded = false,
    int DurationMs = 60000);

// Writes raw rows straight into the stats tables for the derived-view tests, on SQLite or MySQL / MariaDB. Only the
// NOT NULL columns are filled; every value is a parameter.
internal sealed class ViewSeed
{
    private readonly DbConnection _connection;
    private readonly StatsDialect _dialect;
    private int _roundNumber;

    public ViewSeed(DbConnection connection, StatsDialect dialect)
    {
        _connection = connection;
        _dialect = dialect;
    }

    public static DateTimeOffset Utc(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public async Task<long> Server(string name)
    {
        var at = _dialect.DateParam(Utc("2026-10-01 00:00:00"));
        await Execute("INSERT INTO stats_servers (name, first_seen, last_seen) VALUES (@name, @at, @at)", ("@name", name), ("@at", at));
        return await LastId();
    }

    public async Task<long> Map(long serverId, string mapName, DateTimeOffset startedAt)
    {
        await Execute("INSERT INTO stats_maps (server_id, map_name, started_at) VALUES (@server, @map, @at)",
            ("@server", serverId), ("@map", mapName), ("@at", _dialect.DateParam(startedAt)));
        return await LastId();
    }

    public async Task<long> Round(long mapId, RoundSeed round)
    {
        var planted = round.PlantTimeMs is not null;
        await Execute("""
            INSERT INTO stats_rounds (map_id, round_number, site, round_type, players_ct, players_t, ranked, winner, end_reason, duration_ms,
                planted, plant_site, plant_time_ms, defused, defused_with_kit, bomb_exploded, started_at)
            VALUES (@map, @number, @site, @type, @ct, @t, @ranked, @winner, 0, @duration,
                @planted, @plant_site, @plant_time, @defused, 0, @exploded, @at)
            """,
            ("@map", mapId), ("@number", ++_roundNumber), ("@site", round.Site), ("@type", round.RoundType), ("@ct", round.PlayersCt),
            ("@t", round.PlayersT), ("@ranked", B(round.Ranked)), ("@winner", round.Winner), ("@duration", round.DurationMs),
            ("@planted", B(planted)), ("@plant_site", planted ? round.Site : null), ("@plant_time", round.PlantTimeMs),
            ("@defused", B(round.Defused)), ("@exploded", B(round.Exploded)), ("@at", _dialect.DateParam(round.StartedAt)));
        return await LastId();
    }

    // Name = "p" + SteamID, so that tests can check the joined names.
    public Task Player(ulong steamId) => Execute(
        "INSERT INTO stats_players (steam_id, name, first_seen, last_seen, elo) VALUES (@steam, @name, @at, @at, 1000)",
        ("@steam", _dialect.SteamParam(steamId)), ("@name", "p" + steamId.ToString(CultureInfo.InvariantCulture)),
        ("@at", _dialect.DateParam(Utc("2026-10-01 00:00:00"))));

    // Every NOT NULL column of stats_round_players: zeros except side, won, rating and the ELO.
    public Task RoundPlayer(long roundId, ulong steamId, string side, bool won, string? primaryWeapon = null) => Execute("""
        INSERT INTO stats_round_players (round_id, steam_id, side, primary_weapon, survived, won,
            kills, deaths, assists, flash_assists, team_kills,
            damage, utility_damage, headshot_kills, shots, hits, headshot_hits,
            entry_kill, entry_death, trade_kills, traded_death,
            kast, clutch_vs, clutch_won, enemies_flashed, teammates_flashed, blind_seconds_inflicted, mvp, planted, defused,
            defused_with_kit, rating, elo_before, elo_after)
        VALUES (@round, @steam, @side, @weapon, 0, @won,
            0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 1.0, 1000, 1000)
        """,
        ("@round", roundId), ("@steam", _dialect.SteamParam(steamId)), ("@side", side), ("@weapon", primaryWeapon), ("@won", B(won)));

    public Task Kill(long roundId, ulong? attacker, ulong victim, int timeMs, bool headshot = false, bool victimIsBot = false, bool teamKill = false) => Execute("""
        INSERT INTO stats_kills (round_id, attacker, victim, victim_is_bot, flash_assist, weapon, headshot, penetrated, noscope, thrusmoke,
            attacker_blind, attacker_in_air, distance, time_ms, is_team_kill)
        VALUES (@round, @attacker, @victim, @bot, 0, 'ak47', @headshot, 0, 0, 0, 0, 0, 10.0, @time, @team)
        """,
        ("@round", roundId), ("@attacker", attacker is { } id ? _dialect.SteamParam(id) : null), ("@victim", _dialect.SteamParam(victim)),
        ("@bot", B(victimIsBot)), ("@headshot", B(headshot)), ("@time", timeMs), ("@team", B(teamKill)));

    public Task PlayerWeapon(ulong steamId, string weapon, int shots, int hits, int headshotHits, int kills, int headshotKills, int damage) => Execute("""
        INSERT INTO stats_player_weapons (steam_id, weapon, shots, hits, headshot_hits, kills, headshot_kills, damage)
        VALUES (@steam, @weapon, @shots, @hits, @hs_hits, @kills, @hs_kills, @damage)
        """,
        ("@steam", _dialect.SteamParam(steamId)), ("@weapon", weapon), ("@shots", shots), ("@hits", hits), ("@hs_hits", headshotHits),
        ("@kills", kills), ("@hs_kills", headshotKills), ("@damage", damage));

    public Task Presence(long serverId, DateTimeOffset at, int humans, int bots, int queued) => Execute(
        "INSERT INTO stats_presence (server_id, at, humans, bots, queued, map_name) VALUES (@server, @at, @humans, @bots, @queued, 'de_mirage')",
        ("@server", serverId), ("@at", _dialect.DateParam(at)), ("@humans", humans), ("@bots", bots), ("@queued", queued));

    public Task Session(ulong steamId, long mapId, DateTimeOffset? connectedAt) => Execute(
        "INSERT INTO stats_sessions (steam_id, map_id, connected_at) VALUES (@steam, @map, @at)",
        ("@steam", _dialect.SteamParam(steamId)), ("@map", mapId), ("@at", connectedAt is { } at ? _dialect.DateParam(at) : null));

    public async Task<object?> Scalar(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static int B(bool value) => value ? 1 : 0;

    private async Task Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> LastId() => Convert.ToInt64(await Scalar(_dialect.LastInsertId), CultureInfo.InvariantCulture);
}
