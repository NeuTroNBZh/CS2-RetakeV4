namespace RetakeV4.Persistence.Stats;

// The derived views of the 4.9-c migration step (Stats v2, sub-project 4): aggregates a site would otherwise rebuild
// from the raw views. Portable SQL only (MySQL 8, MariaDB 11.4, SQLite): sub-queries in FROM, no CTE, no window
// function, every non-aggregated column in GROUP BY (ONLY_FULL_GROUP_BY); integer sums through IntCast (MySQL's SUM is
// DECIMAL, a string for most drivers), ratios through FloatCast + NULLIF. Bots are never actors.
internal static class StatsDerivedViews
{
    // In the order the 4.9-c step creates them (StatsViewsV2.CatalogViews).
    internal static StatsView[] Build(StatsDialect d) => new[] { SideBalance(d), RetakeOdds(d), WeaponMeta(d) };

    // CT / T wins per server, map and site, over the whole history.
    private static StatsView SideBalance(StatsDialect d) => new("v_stats_side_balance", $"""
        SELECT s.name AS server, m.map_name, r.site,
            COUNT(*) AS rounds,
            {d.IntCast("SUM(CASE WHEN r.winner = 'CT' THEN 1 ELSE 0 END)")} AS ct_wins,
            {d.IntCast("SUM(CASE WHEN r.winner = 'T' THEN 1 ELSE 0 END)")} AS t_wins,
            {d.FloatCast("ROUND(100.0 * SUM(CASE WHEN r.winner = 'CT' THEN 1 ELSE 0 END) / NULLIF(COUNT(*), 0), 3)")} AS ct_win_pct,
            {d.FloatCast("ROUND(100.0 * SUM(CASE WHEN r.winner = 'T' THEN 1 ELSE 0 END) / NULLIF(COUNT(*), 0), 3)")} AS t_win_pct,
            {d.IntCast("SUM(CASE WHEN r.ranked = 1 THEN 1 ELSE 0 END)")} AS ranked_rounds,
            {d.IntCast("SUM(CASE WHEN r.ranked = 1 AND r.winner = 'CT' THEN 1 ELSE 0 END)")} AS ranked_ct_wins,
            {d.FloatCast("ROUND(100.0 * SUM(CASE WHEN r.ranked = 1 AND r.winner = 'CT' THEN 1 ELSE 0 END) / NULLIF(SUM(CASE WHEN r.ranked = 1 THEN 1 ELSE 0 END), 0), 3)")} AS ranked_ct_win_pct,
            {d.FloatCast("ROUND(AVG(r.duration_ms), 3)")} AS avg_duration_ms,
            MIN(r.started_at) AS first_round_at, MAX(r.started_at) AS last_round_at
        FROM stats_rounds r
        JOIN stats_maps m ON m.id = r.map_id
        JOIN stats_servers s ON s.id = m.server_id
        GROUP BY s.name, m.map_name, r.site
        """);

    // Rounds grouped by start counts (humans at the round start) and by the participants not yet dead when the bomb was
    // planted (a death at the plant time counts as dead; NULL without a plant). A victim counts once per round, so a
    // duplicated death event changes nothing; whatever the cause (team kill, suicide, world), a death is a death; bots
    // are not participants. Sites add rows up with SUM(rounds), SUM(ct_wins).
    private static StatsView RetakeOdds(StatsDialect d) => new("v_stats_retake_odds", $"""
        SELECT x.server, x.map_name, x.site, x.round_type, x.ranked, x.players_ct, x.players_t, x.ct_alive_at_plant, x.t_alive_at_plant,
            COUNT(*) AS rounds,
            {d.IntCast("SUM(x.ct_win)")} AS ct_wins,
            {d.IntCast("SUM(x.t_win)")} AS t_wins,
            {d.FloatCast("ROUND(100.0 * SUM(x.ct_win) / NULLIF(COUNT(*), 0), 3)")} AS ct_win_pct,
            {d.IntCast("SUM(x.defused)")} AS defused_rounds,
            {d.IntCast("SUM(x.bomb_exploded)")} AS exploded_rounds,
            {d.FloatCast("ROUND(AVG(x.duration_ms), 3)")} AS avg_duration_ms
        FROM (
            SELECT s.name AS server, m.map_name, r.site, r.round_type, r.ranked, r.players_ct, r.players_t,
                CASE WHEN r.planted = 1 AND r.plant_time_ms IS NOT NULL THEN r.players_ct - COALESCE(pd.ct_dead, 0) END AS ct_alive_at_plant,
                CASE WHEN r.planted = 1 AND r.plant_time_ms IS NOT NULL THEN r.players_t - COALESCE(pd.t_dead, 0) END AS t_alive_at_plant,
                CASE WHEN r.winner = 'CT' THEN 1 ELSE 0 END AS ct_win,
                CASE WHEN r.winner = 'T' THEN 1 ELSE 0 END AS t_win,
                r.defused, r.bomb_exploded, r.duration_ms
            FROM stats_rounds r
            JOIN stats_maps m ON m.id = r.map_id
            JOIN stats_servers s ON s.id = m.server_id
            LEFT JOIN (
                SELECT k.round_id,
                    COUNT(DISTINCT CASE WHEN v.side = 'CT' THEN k.victim END) AS ct_dead,
                    COUNT(DISTINCT CASE WHEN v.side = 'T' THEN k.victim END) AS t_dead
                FROM stats_kills k
                JOIN stats_rounds pr ON pr.id = k.round_id
                JOIN stats_round_players v ON v.round_id = k.round_id AND v.steam_id = k.victim
                WHERE k.victim_is_bot = 0 AND pr.planted = 1 AND k.time_ms <= pr.plant_time_ms
                GROUP BY k.round_id
            ) pd ON pd.round_id = r.id
        ) x
        GROUP BY x.server, x.map_name, x.site, x.round_type, x.ranked, x.players_ct, x.players_t, x.ct_alive_at_plant, x.t_alive_at_plant
        """);

    // Per weapon over every player: the per-player weapon totals, plus the rounds where it was the primary weapon given
    // at the round start. The UNION lists weapons seen in either source (a primary never fired has zero totals).
    private static StatsView WeaponMeta(StatsDialect d) => new("v_stats_weapon_meta", $"""
        SELECT w.weapon,
            COALESCE(a.players, 0) AS players,
            COALESCE(a.shots, 0) AS shots,
            COALESCE(a.hits, 0) AS hits,
            COALESCE(a.headshot_hits, 0) AS headshot_hits,
            COALESCE(a.kills, 0) AS kills,
            COALESCE(a.headshot_kills, 0) AS headshot_kills,
            COALESCE(a.damage, 0) AS damage,
            {d.FloatCast("ROUND(100.0 * a.hits / NULLIF(a.shots, 0), 3)")} AS accuracy,
            {d.FloatCast("ROUND(100.0 * a.headshot_hits / NULLIF(a.hits, 0), 3)")} AS hs_accuracy,
            {d.FloatCast("ROUND(100.0 * a.headshot_kills / NULLIF(a.kills, 0), 3)")} AS hs_pct,
            {d.FloatCast("ROUND(100.0 * COALESCE(a.kills, 0) / NULLIF(t.total_kills, 0), 3)")} AS kill_share_pct,
            COALESCE(u.primary_rounds, 0) AS primary_rounds,
            COALESCE(u.primary_players, 0) AS primary_players,
            COALESCE(u.primary_won_rounds, 0) AS primary_won_rounds,
            {d.FloatCast("ROUND(100.0 * u.primary_won_rounds / NULLIF(u.primary_rounds, 0), 3)")} AS primary_win_pct
        FROM (
            SELECT weapon FROM stats_player_weapons
            UNION
            SELECT primary_weapon AS weapon FROM stats_round_players WHERE primary_weapon IS NOT NULL
        ) w
        LEFT JOIN (
            SELECT weapon, COUNT(*) AS players,
                {d.IntCast("SUM(shots)")} AS shots, {d.IntCast("SUM(hits)")} AS hits, {d.IntCast("SUM(headshot_hits)")} AS headshot_hits,
                {d.IntCast("SUM(kills)")} AS kills, {d.IntCast("SUM(headshot_kills)")} AS headshot_kills, {d.IntCast("SUM(damage)")} AS damage
            FROM stats_player_weapons
            GROUP BY weapon
        ) a ON a.weapon = w.weapon
        LEFT JOIN (
            SELECT primary_weapon AS weapon, COUNT(*) AS primary_rounds, COUNT(DISTINCT steam_id) AS primary_players,
                {d.IntCast("SUM(won)")} AS primary_won_rounds
            FROM stats_round_players
            WHERE primary_weapon IS NOT NULL
            GROUP BY primary_weapon
        ) u ON u.weapon = w.weapon
        CROSS JOIN (SELECT SUM(kills) AS total_kills FROM stats_player_weapons) t
        """);
}
