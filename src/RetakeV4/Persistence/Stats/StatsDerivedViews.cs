namespace RetakeV4.Persistence.Stats;

// The derived views of the 4.9-c migration step (Stats v2, sub-project 4): aggregates a site would otherwise rebuild
// from the raw views. Portable SQL only (MySQL 8, MariaDB 11.4, SQLite): sub-queries in FROM, no CTE, no window
// function, every non-aggregated column in GROUP BY (ONLY_FULL_GROUP_BY); integer sums through IntCast (MySQL's SUM is
// DECIMAL, a string for most drivers), ratios through FloatCast + NULLIF. Bots are never actors.
internal static class StatsDerivedViews
{
    // In the order the 4.9-c step creates them (StatsViewsV2.CatalogViews).
    internal static StatsView[] Build(StatsDialect d) => new[] { SideBalance(d) };

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
}
