using System.Globalization;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Persistence.Stats;

// The v2 read contract (contract/stats/views.v2.json): new views only, the v1 views never change. Portable SQL only:
// it runs unchanged on MySQL 8, MariaDB and SQLite. Ratios are computed here, never stored; a ratio over zero is NULL.
public static class StatsViewsV2
{
    private static readonly (string Name, string Expression)[] Ratios =
    {
        ("still_accuracy", "ROUND(100.0 * {0}hits_still / NULLIF({0}shots_still, 0), 3)"),
        ("moving_accuracy", "ROUND(100.0 * {0}hits_moving / NULLIF({0}shots_moving, 0), 3)"),
        ("scoped_accuracy", "ROUND(100.0 * {0}hits_scoped / NULLIF({0}shots_scoped, 0), 3)"),
        ("still_shot_pct", "ROUND(100.0 * {0}shots_still / NULLIF({0}shots_still + {0}shots_moving, 0), 3)"),
        ("dtr", "ROUND(1.0 * {0}damage_taken / NULLIF({0}rounds, 0), 3)"),
    };

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
        new StatsView("v2_stats_players", $"""
            SELECT p.steam_id, p.name, p.country, p.first_seen, p.last_seen, p.elo,
                COALESCE(pt.seconds_ct, 0) AS seconds_ct, COALESCE(pt.seconds_t, 0) AS seconds_t,
                COALESCE(pt.seconds_spec, 0) AS seconds_spec, COALESCE(pt.seconds_queue, 0) AS seconds_queue,
                COALESCE(pt.seconds_ct + pt.seconds_t + pt.seconds_spec + pt.seconds_queue, 0) AS seconds_total,
                COALESCE(pt.sessions, 0) AS sessions,
                {StatsViews.CountersOrZero(StatsCounters.AllColumns, "t.")},
                {StatsViews.RatioColumns(d, "t.", null)},
                {RatioColumns(d, "t.")}
            FROM stats_players p
            LEFT JOIN stats_player_totals t ON t.steam_id = p.steam_id AND t.scope = 'all'
            LEFT JOIN stats_player_playtime pt ON pt.steam_id = p.steam_id
            """),
        new StatsView("v2_stats_player_scopes", $"""
            SELECT t.steam_id, p.name, t.scope,
                {StatsViews.Counters(StatsCounters.AllColumns, "t.")},
                {StatsViews.RatioColumns(d, "t.", null)},
                {RatioColumns(d, "t.")}
            FROM stats_player_totals t
            JOIN stats_players p ON p.steam_id = t.steam_id
            """),
        new StatsView("v2_stats_round_players", """
            SELECT rp.round_id, s.name AS server, m.map_name, r.site, r.round_type, r.started_at,
                rp.steam_id, rp.side, rp.primary_weapon, rp.survived, rp.won, rp.kills, rp.deaths, rp.assists, rp.flash_assists,
                rp.team_kills, rp.damage, rp.utility_damage, rp.headshot_kills, rp.shots, rp.hits, rp.headshot_hits,
                rp.entry_kill, rp.entry_death, rp.trade_kills, rp.traded_death, rp.kast, rp.clutch_vs, rp.clutch_won,
                rp.enemies_flashed, rp.teammates_flashed, rp.blind_seconds_inflicted, rp.mvp, rp.planted, rp.defused,
                rp.defused_with_kit, rp.rating, rp.elo_before, rp.elo_after,
                rp.shots_still, rp.hits_still, rp.shots_moving, rp.hits_moving, rp.shots_scoped, rp.hits_scoped,
                rp.damage_taken, rp.bomb_carry_ms, rp.health_end, rp.armor_end
            FROM stats_round_players rp
            JOIN stats_rounds r ON r.id = rp.round_id
            JOIN stats_maps m ON m.id = r.map_id
            JOIN stats_servers s ON s.id = m.server_id
            """),
        new StatsView("v2_stats_shots", """
            SELECT id AS shot_id, round_id, steam_id, weapon, time_ms, tick, pos_x, pos_y, pos_z, pitch, yaw,
                speed, crouched, scoped, airborne, walking, still
            FROM stats_shots
            """),
        new StatsView("v2_stats_sessions", """
            SELECT ss.id AS session_id, ss.steam_id, s.name AS server, m.map_name, ss.connected_at, ss.disconnected_at,
                ss.disconnect_reason, ss.seconds_ct, ss.seconds_t, ss.seconds_spec, ss.seconds_queue, ss.seconds_vip,
                ss.ping_avg, ss.ping_max, ss.country
            FROM stats_sessions ss
            JOIN stats_maps m ON m.id = ss.map_id
            JOIN stats_servers s ON s.id = m.server_id
            """),
        new StatsView("v_stats_player_events", """
            SELECT e.id AS event_id, e.round_id, m.map_name, r.site, r.round_type, r.started_at,
                e.steam_id, e.type, e.time_ms, e.value, e.pos_x, e.pos_y, e.pos_z
            FROM stats_player_events e
            JOIN stats_rounds r ON r.id = e.round_id
            JOIN stats_maps m ON m.id = r.map_id
            """),
        new StatsView("v_stats_player_network", $"""
            SELECT p.steam_id, p.name, p.country, COUNT(ss.id) AS sessions, COUNT(ss.ping_avg) AS sessions_with_ping,
                {d.FloatCast("ROUND(AVG(ss.ping_avg), 1)")} AS ping_avg, MAX(ss.ping_max) AS ping_max,
                COALESCE(SUM(ss.seconds_vip), 0) AS seconds_vip
            FROM stats_players p
            LEFT JOIN stats_sessions ss ON ss.steam_id = p.steam_id
            GROUP BY p.steam_id, p.name, p.country
            """),
        new StatsView("v_stats_server_health", """
            SELECT e.id AS event_id, s.name AS server, e.at, e.type, e.module, e.value, e.occurrences, e.max_ms
            FROM stats_server_events e
            JOIN stats_servers s ON s.id = e.server_id
            """),
        new StatsView("v_stats_presence", """
            SELECT pr.id AS snapshot_id, s.name AS server, pr.at, pr.map_name, pr.humans, pr.bots, pr.queued
            FROM stats_presence pr
            JOIN stats_servers s ON s.id = pr.server_id
            """),
    };

    private static string RatioColumns(StatsDialect d, string alias) => string.Join(", ",
        Ratios.Select(r => $"{d.FloatCast(string.Format(CultureInfo.InvariantCulture, r.Expression, alias))} AS {r.Name}"));
}
