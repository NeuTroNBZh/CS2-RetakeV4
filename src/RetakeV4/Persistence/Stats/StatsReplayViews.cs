namespace RetakeV4.Persistence.Stats;

// The replay views of the 4.9-d migration step (Stats v2, sub-project 3). Portable SQL (MySQL 8, MariaDB 11.4, SQLite).
// v_stats_replays: one row per owner of a stored replay (round, map, site, type, dates, owner, reason, score);
// v_stats_replay_tracks: one row per human track with its frames. Kills, grenades, damages, shots and the bomb of the
// same round are in the v1 views (join on round_id).
internal static class StatsReplayViews
{
    // In the order the 4.9-d step creates them.
    internal static IReadOnlyList<string> Names { get; } = new[] { "v_stats_replays", "v_stats_replay_tracks" };

    internal static StatsView[] Build() => new[] { Replays(), Tracks() };

    private static StatsView Replays() => new("v_stats_replays", """
        SELECT o.round_id, s.name AS server, m.map_name, r.round_number, r.site, r.round_type, r.winner, r.duration_ms, r.started_at,
            rp.sample_hz, rp.bytes, rp.created_at, o.steam_id, p.name, o.reason, o.score, o.created_at AS owned_at
        FROM stats_replay_owners o
        JOIN stats_replays rp ON rp.round_id = o.round_id
        JOIN stats_rounds r ON r.id = o.round_id
        JOIN stats_maps m ON m.id = r.map_id
        JOIN stats_servers s ON s.id = m.server_id
        JOIN stats_players p ON p.steam_id = o.steam_id
        """);

    private static StatsView Tracks() => new("v_stats_replay_tracks", """
        SELECT t.round_id, t.steam_id, p.name, t.side, t.frames
        FROM stats_replay_tracks t
        JOIN stats_players p ON p.steam_id = t.steam_id
        """);
}
