using System.Globalization;

namespace RetakeV4.Persistence.Stats;

// The v2 read contract (contract/stats/views.v2.json): new views only, the v1 views never change. Portable SQL only:
// it runs unchanged on MySQL 8, MariaDB and SQLite. Ratios are computed here, never stored; a ratio over zero is NULL.
// Events(d) is what the 4.9-a migration step creates and Movement(d) what the 4.9-b step creates: both frozen
// (StatsSchemaTests pins their SQL), so they read the frozen counter and ratio lists below and of StatsSchema, never
// StatsCounters. Catalog(d) adds the derived views of the 4.9-c step (StatsDerivedViews). For(d) is the current
// contract, today equal to Catalog(d). A view changed after 4.9-c gets its new definition swapped into BuildViews and
// is replaced by a later step (StatsSchema: DROP VIEW + CREATE VIEW).
public static class StatsViewsV2
{
    // Frozen with the 4.9-a views (and reused unchanged by 4.9-b).
    private static readonly (string Name, string Expression)[] Ratios =
    {
        ("still_accuracy", "ROUND(100.0 * {0}hits_still / NULLIF({0}shots_still, 0), 3)"),
        ("moving_accuracy", "ROUND(100.0 * {0}hits_moving / NULLIF({0}shots_moving, 0), 3)"),
        ("scoped_accuracy", "ROUND(100.0 * {0}hits_scoped / NULLIF({0}shots_scoped, 0), 3)"),
        ("still_shot_pct", "ROUND(100.0 * {0}shots_still / NULLIF({0}shots_still + {0}shots_moving, 0), 3)"),
        ("dtr", "ROUND(1.0 * {0}damage_taken / NULLIF({0}rounds, 0), 3)"),
    };

    // Frozen with the 4.9-b views.
    private static readonly (string Name, string Expression)[] MovementRatios =
    {
        ("walk_pct", "ROUND(100.0 * {0}walk_samples / NULLIF({0}alive_samples, 0), 3)"),
        ("run_pct", "ROUND(100.0 * {0}run_samples / NULLIF({0}alive_samples, 0), 3)"),
        ("crouch_pct", "ROUND(100.0 * {0}crouch_samples / NULLIF({0}alive_samples, 0), 3)"),
        ("still_pct", "ROUND(100.0 * {0}still_samples / NULLIF({0}alive_samples, 0), 3)"),
        ("air_pct", "ROUND(100.0 * {0}air_samples / NULLIF({0}alive_samples, 0), 3)"),
        ("in_smoke_pct", "ROUND(100.0 * {0}in_smoke_samples / NULLIF({0}alive_samples, 0), 3)"),
        ("behind_smoke_pct", "ROUND(100.0 * {0}behind_smoke_samples / NULLIF({0}alive_samples, 0), 3)"),
        ("afk_pct", "ROUND(100.0 * {0}afk_ms / NULLIF({0}alive_ms, 0), 3)"),
        ("distance_m_per_min", "ROUND(600.0 * {0}distance_cm / NULLIF({0}alive_ms, 0), 3)"),
        ("avg_first_contact_ms", "ROUND(1.0 * {0}first_contact_ms_sum / NULLIF({0}first_contact_rounds, 0), 3)"),
        ("avg_engage_order", "ROUND(1.0 * {0}engage_order_sum / NULLIF({0}first_contact_rounds, 0), 3)"),
        ("bait_rate", "ROUND(1.0 * {0}bait_rounds / NULLIF({0}bait_chances, 0), 3)"),
    };

    // The stats_player_totals counters when the 4.9-a step ran.
    private static readonly IReadOnlyList<string> EventsCounters = StatsSchema.V1Counters.Concat(StatsSchema.V2Counters).ToArray();

    // The stats_player_totals counters when the 4.9-b step ran.
    private static readonly IReadOnlyList<string> MovementCounters = EventsCounters.Concat(StatsSchema.MovementCounters).ToArray();

    private static readonly StatsView[] MySqlEvents = BuildEvents(StatsDialect.MySql);

    private static readonly StatsView[] SqliteEvents = BuildEvents(StatsDialect.Sqlite);

    private static readonly StatsView[] MySqlMovement = BuildMovement(StatsDialect.MySql);

    private static readonly StatsView[] SqliteMovement = BuildMovement(StatsDialect.Sqlite);

    private static readonly StatsView[] MySqlCatalog = BuildCatalog(StatsDialect.MySql);

    private static readonly StatsView[] SqliteCatalog = BuildCatalog(StatsDialect.Sqlite);

    private static readonly StatsView[] MySqlViews = BuildViews(StatsDialect.MySql);

    private static readonly StatsView[] SqliteViews = BuildViews(StatsDialect.Sqlite);

    public static IReadOnlyList<string> Names { get; } = SqliteViews.Select(v => v.Name).ToArray();

    // Views the 4.9-b step drops and creates again (or creates), in this order.
    public static IReadOnlyList<string> MovementViews { get; } =
        new[] { "v2_stats_players", "v2_stats_player_scopes", "v2_stats_round_players", "v_stats_player_behaviour" };

    // Views the 4.9-c step drops and creates, in this order (StatsDerivedViews.Build).
    public static IReadOnlyList<string> CatalogViews { get; } = new[] { "v_stats_side_balance", "v_stats_retake_odds", "v_stats_weapon_meta", "v_stats_rivals", "v_stats_duos", "v_stats_server_activity" };

    public static IReadOnlyList<StatsView> For(StatsDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        return dialect.IsMySql ? MySqlViews : SqliteViews;
    }

    internal static IReadOnlyList<StatsView> Events(StatsDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        return dialect.IsMySql ? MySqlEvents : SqliteEvents;
    }

    internal static IReadOnlyList<StatsView> Movement(StatsDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        return dialect.IsMySql ? MySqlMovement : SqliteMovement;
    }

    internal static IReadOnlyList<StatsView> Catalog(StatsDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        return dialect.IsMySql ? MySqlCatalog : SqliteCatalog;
    }

    // The current contract: the 4.9-c views, unchanged so far.
    private static StatsView[] BuildViews(StatsDialect d) => BuildCatalog(d);

    // The 4.9-b views, then the derived views added by 4.9-c.
    private static StatsView[] BuildCatalog(StatsDialect d) => BuildMovement(d).Concat(StatsDerivedViews.Build(d)).ToArray();

    // Frozen: the 4.9-a views with those changed by 4.9-b swapped in, then the views added by 4.9-b.
    private static StatsView[] BuildMovement(StatsDialect d)
    {
        var replaced = new[] { Players(d), Scopes(d), RoundPlayers() }.ToDictionary(v => v.Name);
        return BuildEvents(d).Select(v => replaced.GetValueOrDefault(v.Name, v)).Append(Behaviour(d)).ToArray();
    }

    // Players, Scopes, RoundPlayers and Behaviour are the frozen 4.9-b definitions (read by BuildMovement).

    private static StatsView Players(StatsDialect d) => new("v2_stats_players", $"""
        SELECT p.steam_id, p.name, p.country, p.first_seen, p.last_seen, p.elo,
            COALESCE(pt.seconds_ct, 0) AS seconds_ct, COALESCE(pt.seconds_t, 0) AS seconds_t,
            COALESCE(pt.seconds_spec, 0) AS seconds_spec, COALESCE(pt.seconds_queue, 0) AS seconds_queue,
            COALESCE(pt.seconds_ct + pt.seconds_t + pt.seconds_spec + pt.seconds_queue, 0) AS seconds_total,
            COALESCE(pt.sessions, 0) AS sessions,
            {StatsViews.CountersOrZero(MovementCounters, "t.")},
            {StatsViews.RatioColumns(d, "t.", null)},
            {RatioColumns(d, "t.", Ratios)},
            {RatioColumns(d, "t.", MovementRatios)}
        FROM stats_players p
        LEFT JOIN stats_player_totals t ON t.steam_id = p.steam_id AND t.scope = 'all'
        LEFT JOIN stats_player_playtime pt ON pt.steam_id = p.steam_id
        """);

    private static StatsView Scopes(StatsDialect d) => new("v2_stats_player_scopes", $"""
        SELECT t.steam_id, p.name, t.scope,
            {StatsViews.Counters(MovementCounters, "t.")},
            {StatsViews.RatioColumns(d, "t.", null)},
            {RatioColumns(d, "t.", Ratios)},
            {RatioColumns(d, "t.", MovementRatios)}
        FROM stats_player_totals t
        JOIN stats_players p ON p.steam_id = t.steam_id
        """);

    private static StatsView RoundPlayers() => new("v2_stats_round_players", """
        SELECT rp.round_id, s.name AS server, m.map_name, r.site, r.round_type, r.started_at,
            rp.steam_id, rp.side, rp.primary_weapon, rp.survived, rp.won, rp.kills, rp.deaths, rp.assists, rp.flash_assists,
            rp.team_kills, rp.damage, rp.utility_damage, rp.headshot_kills, rp.shots, rp.hits, rp.headshot_hits,
            rp.entry_kill, rp.entry_death, rp.trade_kills, rp.traded_death, rp.kast, rp.clutch_vs, rp.clutch_won,
            rp.enemies_flashed, rp.teammates_flashed, rp.blind_seconds_inflicted, rp.mvp, rp.planted, rp.defused,
            rp.defused_with_kit, rp.rating, rp.elo_before, rp.elo_after,
            rp.shots_still, rp.hits_still, rp.shots_moving, rp.hits_moving, rp.shots_scoped, rp.hits_scoped,
            rp.damage_taken, rp.bomb_carry_ms, rp.health_end, rp.armor_end,
            rp.alive_ms, rp.walk_pct, rp.run_pct, rp.crouch_pct, rp.still_pct, rp.air_pct, rp.distance_m, rp.afk_ms,
            rp.in_smoke_pct, rp.behind_smoke_pct, rp.first_contact_ms, rp.engage_order, rp.bait
        FROM stats_round_players rp
        JOIN stats_rounds r ON r.id = rp.round_id
        JOIN stats_maps m ON m.id = r.map_id
        JOIN stats_servers s ON s.id = m.server_id
        """);

    // slowness_index: the player's average first contact over the rounds where at least one teammate also made contact,
    // divided by the average (over those rounds) of the teammates' average first contact. Above 1 = slower.
    private static StatsView Behaviour(StatsDialect d) => new("v_stats_player_behaviour", $"""
        SELECT p.steam_id, p.name,
            COALESCE(t.first_contact_rounds, 0) AS contact_rounds,
            {d.FloatCast("ROUND(1.0 * t.first_contact_ms_sum / NULLIF(t.first_contact_rounds, 0), 3)")} AS avg_first_contact_ms,
            COALESCE(sl.compared_rounds, 0) AS compared_rounds,
            {d.FloatCast("ROUND(sl.own_ms, 3)")} AS own_first_contact_ms,
            {d.FloatCast("ROUND(sl.team_ms, 3)")} AS team_first_contact_ms,
            {d.FloatCast("ROUND(sl.own_ms / NULLIF(sl.team_ms, 0), 3)")} AS slowness_index,
            {d.FloatCast("ROUND(100.0 * t.behind_smoke_samples / NULLIF(t.alive_samples, 0), 3)")} AS behind_smoke_pct,
            {d.FloatCast("ROUND(100.0 * t.in_smoke_samples / NULLIF(t.alive_samples, 0), 3)")} AS in_smoke_pct,
            {d.FloatCast("ROUND(1.0 * t.engage_order_sum / NULLIF(t.first_contact_rounds, 0), 3)")} AS avg_engage_order,
            COALESCE(t.bait_rounds, 0) AS bait_rounds,
            COALESCE(t.bait_chances, 0) AS bait_chances,
            {d.FloatCast("ROUND(1.0 * t.bait_rounds / NULLIF(t.bait_chances, 0), 3)")} AS bait_rate
        FROM stats_players p
        LEFT JOIN stats_player_totals t ON t.steam_id = p.steam_id AND t.scope = 'all'
        LEFT JOIN (
            SELECT x.steam_id, COUNT(*) AS compared_rounds, AVG(x.own_ms) AS own_ms, AVG(x.team_ms) AS team_ms
            FROM (
                SELECT a.round_id, a.steam_id, MAX(a.first_contact_ms) AS own_ms, AVG(b.first_contact_ms) AS team_ms
                FROM stats_round_players a
                JOIN stats_round_players b ON b.round_id = a.round_id AND b.side = a.side AND b.steam_id <> a.steam_id
                WHERE a.first_contact_ms IS NOT NULL AND b.first_contact_ms IS NOT NULL
                GROUP BY a.round_id, a.steam_id
            ) x
            GROUP BY x.steam_id
        ) sl ON sl.steam_id = p.steam_id
        """);

    private static StatsView[] BuildEvents(StatsDialect d) => new[]
    {
        new StatsView("v2_stats_players", $"""
            SELECT p.steam_id, p.name, p.country, p.first_seen, p.last_seen, p.elo,
                COALESCE(pt.seconds_ct, 0) AS seconds_ct, COALESCE(pt.seconds_t, 0) AS seconds_t,
                COALESCE(pt.seconds_spec, 0) AS seconds_spec, COALESCE(pt.seconds_queue, 0) AS seconds_queue,
                COALESCE(pt.seconds_ct + pt.seconds_t + pt.seconds_spec + pt.seconds_queue, 0) AS seconds_total,
                COALESCE(pt.sessions, 0) AS sessions,
                {StatsViews.CountersOrZero(EventsCounters, "t.")},
                {StatsViews.RatioColumns(d, "t.", null)},
                {RatioColumns(d, "t.", Ratios)}
            FROM stats_players p
            LEFT JOIN stats_player_totals t ON t.steam_id = p.steam_id AND t.scope = 'all'
            LEFT JOIN stats_player_playtime pt ON pt.steam_id = p.steam_id
            """),
        new StatsView("v2_stats_player_scopes", $"""
            SELECT t.steam_id, p.name, t.scope,
                {StatsViews.Counters(EventsCounters, "t.")},
                {StatsViews.RatioColumns(d, "t.", null)},
                {RatioColumns(d, "t.", Ratios)}
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

    private static string RatioColumns(StatsDialect d, string alias, (string Name, string Expression)[] ratios) => string.Join(", ",
        ratios.Select(r => $"{d.FloatCast(string.Format(CultureInfo.InvariantCulture, r.Expression, alias))} AS {r.Name}"));
}
