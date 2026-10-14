using System.Globalization;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Persistence.Stats;

public sealed record StatsView(string Name, string Select);

// The public read contract (contract/stats/views.v1.json). Portable SQL only: it runs unchanged on MySQL 8 and SQLite.
// Ratios are computed here, never stored; a ratio over zero is NULL.
public static class StatsViews
{
    private static readonly (string Name, string Expression)[] Ratios = BuildRatios();

    private static readonly string[] LeaderboardRatios = { "kpr", "kd", "adr", "hs_pct", "accuracy", "kast_pct", "win_pct", "rating" };

    private static readonly StatsView[] MySqlViews = BuildViews(StatsDialect.MySql);

    private static readonly StatsView[] SqliteViews = BuildViews(StatsDialect.Sqlite);

    public static IReadOnlyList<string> Names { get; } = SqliteViews.Select(v => v.Name).ToArray();

    public static IReadOnlyList<StatsView> For(StatsDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        return dialect.IsMySql ? MySqlViews : SqliteViews;
    }

    private static StatsView[] BuildViews(StatsDialect d) => new[]
    {
        new StatsView("v_stats_players", $"""
            SELECT p.steam_id, p.name, p.first_seen, p.last_seen, p.elo,
                COALESCE(pt.seconds_ct, 0) AS seconds_ct, COALESCE(pt.seconds_t, 0) AS seconds_t,
                COALESCE(pt.seconds_spec, 0) AS seconds_spec, COALESCE(pt.seconds_queue, 0) AS seconds_queue,
                COALESCE(pt.seconds_ct + pt.seconds_t + pt.seconds_spec + pt.seconds_queue, 0) AS seconds_total,
                COALESCE(pt.sessions, 0) AS sessions,
                {CountersOrZero(StatsCounters.Columns, "t.")},
                {RatioColumns(d, "t.", null)}
            FROM stats_players p
            LEFT JOIN stats_player_totals t ON t.steam_id = p.steam_id AND t.scope = 'all'
            LEFT JOIN stats_player_playtime pt ON pt.steam_id = p.steam_id
            """),
        new StatsView("v_stats_player_scopes", $"""
            SELECT t.steam_id, p.name, t.scope,
                {Counters(StatsCounters.Columns, "t.")},
                {RatioColumns(d, "t.", null)}
            FROM stats_player_totals t
            JOIN stats_players p ON p.steam_id = t.steam_id
            """),
        new StatsView("v_stats_player_weapons", $"""
            SELECT steam_id, weapon, shots, hits, headshot_hits, kills, headshot_kills, damage,
                {d.FloatCast("ROUND(100.0 * hits / NULLIF(shots, 0), 3)")} AS accuracy,
                {d.FloatCast("ROUND(100.0 * headshot_kills / NULLIF(kills, 0), 3)")} AS hs_pct
            FROM stats_player_weapons
            """),
        new StatsView("v_stats_leaderboard", $"""
            SELECT p.steam_id, p.name, p.elo, t.ranked_rounds, t.rounds, t.kills, t.deaths, t.damage,
                {RatioColumns(d, "t.", LeaderboardRatios)}
            FROM stats_players p
            JOIN stats_player_totals t ON t.steam_id = p.steam_id AND t.scope = 'all'
            WHERE t.ranked_rounds > 0
            """),
        new StatsView("v_stats_maps", """
            SELECT m.id AS map_id, s.name AS server, m.map_name, m.started_at, m.ended_at
            FROM stats_maps m
            JOIN stats_servers s ON s.id = m.server_id
            """),
        new StatsView("v_stats_rounds", """
            SELECT r.id AS round_id, s.name AS server, m.map_name, r.round_number, r.site, r.round_type, r.players_ct, r.players_t,
                r.ranked, r.winner, r.end_reason, r.duration_ms, r.planted, r.plant_site, r.planter, r.plant_time_ms,
                r.defused, r.defuser, r.defused_with_kit, r.bomb_exploded, r.started_at
            FROM stats_rounds r
            JOIN stats_maps m ON m.id = r.map_id
            JOIN stats_servers s ON s.id = m.server_id
            """),
        new StatsView("v_stats_round_players", """
            SELECT rp.round_id, s.name AS server, m.map_name, r.site, r.round_type, r.started_at,
                rp.steam_id, rp.side, rp.primary_weapon, rp.survived, rp.won, rp.kills, rp.deaths, rp.assists, rp.flash_assists,
                rp.team_kills, rp.damage, rp.utility_damage, rp.headshot_kills, rp.shots, rp.hits, rp.headshot_hits,
                rp.entry_kill, rp.entry_death, rp.trade_kills, rp.traded_death, rp.kast, rp.clutch_vs, rp.clutch_won,
                rp.enemies_flashed, rp.teammates_flashed, rp.blind_seconds_inflicted, rp.mvp, rp.planted, rp.defused,
                rp.defused_with_kit, rp.rating, rp.elo_before, rp.elo_after
            FROM stats_round_players rp
            JOIN stats_rounds r ON r.id = rp.round_id
            JOIN stats_maps m ON m.id = r.map_id
            JOIN stats_servers s ON s.id = m.server_id
            """),
        new StatsView("v_stats_kills", """
            SELECT k.id AS kill_id, k.round_id, m.map_name, r.site, r.round_type, r.started_at,
                k.attacker, k.victim, k.victim_is_bot, k.assister, k.flash_assist, k.weapon, k.headshot, k.penetrated,
                k.noscope, k.thrusmoke, k.attacker_blind, k.attacker_in_air, k.distance,
                k.attacker_x, k.attacker_y, k.attacker_z, k.victim_x, k.victim_y, k.victim_z, k.time_ms, k.is_team_kill
            FROM stats_kills k
            JOIN stats_rounds r ON r.id = k.round_id
            JOIN stats_maps m ON m.id = r.map_id
            """),
        new StatsView("v_stats_shots", """
            SELECT id AS shot_id, round_id, steam_id, weapon, time_ms, tick, pos_x, pos_y, pos_z, pitch, yaw
            FROM stats_shots
            """),
        new StatsView("v_stats_damages", """
            SELECT id AS damage_id, round_id, attacker, victim, victim_is_bot, weapon, hitgroup, dmg_health, dmg_armor, health_after, time_ms, tick
            FROM stats_damages
            """),
        new StatsView("v_stats_grenades", """
            SELECT id AS grenade_id, round_id, steam_id, type, thrown_ms, detonated_ms, throw_x, throw_y, throw_z, det_x, det_y, det_z,
                damage, enemies_blinded, teammates_blinded, blind_seconds
            FROM stats_grenades
            """),
        new StatsView("v_stats_sessions", """
            SELECT ss.id AS session_id, ss.steam_id, s.name AS server, m.map_name, ss.connected_at, ss.disconnected_at,
                ss.disconnect_reason, ss.seconds_ct, ss.seconds_t, ss.seconds_spec, ss.seconds_queue
            FROM stats_sessions ss
            JOIN stats_maps m ON m.id = ss.map_id
            JOIN stats_servers s ON s.id = m.server_id
            """),
        new StatsView("v_stats_elo_history", """
            SELECT steam_id, day, elo, rounds, delta_sum
            FROM stats_elo_history
            """),
    };

    internal static string Counters(IEnumerable<string> columns, string alias) => string.Join(", ", columns.Select(c => $"{alias}{c}"));

    internal static string CountersOrZero(IEnumerable<string> columns, string alias) =>
        string.Join(", ", columns.Select(c => $"COALESCE({alias}{c}, 0) AS {c}"));

    internal static string RatioColumns(StatsDialect d, string alias, IReadOnlyCollection<string>? only) => string.Join(", ",
        Ratios.Where(r => only is null || only.Contains(r.Name))
            .Select(r => $"{d.FloatCast(string.Format(CultureInfo.InvariantCulture, r.Expression, alias))} AS {r.Name}"));

    private static (string Name, string Expression)[] BuildRatios()
    {
        var clutchWon = string.Join(" + ", Enumerable.Range(1, 5).Select(x => $"{{0}}clutch_{x}_won"));
        var clutchPlayed = string.Join(" + ", Enumerable.Range(1, 5).Select(x => $"{{0}}clutch_{x}_played"));
        return new[]
        {
            ("kpr", "ROUND(1.0 * {0}kills / NULLIF({0}rounds, 0), 3)"),
            ("kd", "ROUND(1.0 * {0}kills / NULLIF({0}deaths, 0), 3)"),
            ("adr", "ROUND(1.0 * {0}damage / NULLIF({0}rounds, 0), 3)"),
            ("hs_pct", "ROUND(100.0 * {0}headshot_kills / NULLIF({0}kills, 0), 3)"),
            ("accuracy", "ROUND(100.0 * {0}hits / NULLIF({0}shots, 0), 3)"),
            ("hs_accuracy", "ROUND(100.0 * {0}headshot_hits / NULLIF({0}hits, 0), 3)"),
            ("kast_pct", "ROUND(100.0 * {0}kast_rounds / NULLIF({0}rounds, 0), 3)"),
            ("survival_pct", "ROUND(100.0 * {0}survived_rounds / NULLIF({0}rounds, 0), 3)"),
            ("win_pct", "ROUND(100.0 * {0}rounds_won / NULLIF({0}rounds, 0), 3)"),
            ("rating", "ROUND({0}rating_milli / 1000.0 / NULLIF({0}rounds, 0), 3)"),
            ("entry_success_pct", "ROUND(100.0 * {0}entry_kills / NULLIF({0}entry_kills + {0}entry_deaths, 0), 3)"),
            ("clutch_won_pct", $"ROUND(100.0 * ({clutchWon}) / NULLIF({clutchPlayed}, 0), 3)"),
        };
    }
}
