using RetakeV4.Domain.Stats;

namespace RetakeV4.Persistence.Stats;

public static class StatsSchema
{
    public const string VersionTable = "stats_schema_version";

    public static IReadOnlyDictionary<string, (string Name, string Columns)[]> Indexes { get; } = new Dictionary<string, (string Name, string Columns)[]>
    {
        ["stats_maps"] = [("ix_stats_maps_server", "server_id")],
        ["stats_sessions"] = [("ix_stats_sessions_steam", "steam_id"), ("ix_stats_sessions_map", "map_id")],
        ["stats_rounds"] = [("ix_stats_rounds_map", "map_id, round_number"), ("ix_stats_rounds_started", "started_at")],
        ["stats_round_players"] = [("ix_stats_round_players_steam", "steam_id, round_id")],
        ["stats_shots"] = [("ix_stats_shots_round", "round_id"), ("ix_stats_shots_steam", "steam_id, round_id")],
        ["stats_damages"] = [("ix_stats_damages_round", "round_id")],
        ["stats_kills"] = [("ix_stats_kills_round", "round_id"), ("ix_stats_kills_attacker", "attacker"), ("ix_stats_kills_victim", "victim")],
        ["stats_grenades"] = [("ix_stats_grenades_round", "round_id")],
    };

    // Append-only: SqlMigrator applies entries by index, so an existing entry is never edited, reordered or removed.
    public static IReadOnlyList<string> Migrations(StatsDialect d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var counters = string.Join(", ", StatsCounters.Columns.Select(c => $"{c} {d.BigInt} NOT NULL DEFAULT 0"));
        var weapons = string.Join(", ", StatsCounters.WeaponColumns.Select(c => $"{c} {d.BigInt} NOT NULL DEFAULT 0"));
        var migrations = new List<string>
        {
            $"CREATE TABLE IF NOT EXISTS stats_servers (id {d.IdPrimaryKey}, name {d.Text(64)} NOT NULL UNIQUE, first_seen {d.DateTime} NOT NULL, last_seen {d.DateTime} NOT NULL){d.TableOptions}",
            $"CREATE TABLE IF NOT EXISTS stats_players (steam_id {d.SteamId} NOT NULL PRIMARY KEY, name {d.Text(128)} NOT NULL, first_seen {d.DateTime} NOT NULL, last_seen {d.DateTime} NOT NULL, elo {d.Int} NOT NULL){d.TableOptions}",
            $"CREATE TABLE IF NOT EXISTS stats_maps (id {d.IdPrimaryKey}, server_id {d.BigInt} NOT NULL, map_name {d.Text(64)} NOT NULL, started_at {d.DateTime} NOT NULL, ended_at {d.DateTime} NULL{Keys(d, "stats_maps")}){d.TableOptions}",
            $"""
            CREATE TABLE IF NOT EXISTS stats_sessions (id {d.IdPrimaryKey}, steam_id {d.SteamId} NOT NULL, map_id {d.BigInt} NOT NULL,
                connected_at {d.DateTime} NULL, disconnected_at {d.DateTime} NULL, disconnect_reason {d.Int} NULL,
                seconds_ct {d.BigInt} NOT NULL DEFAULT 0, seconds_t {d.BigInt} NOT NULL DEFAULT 0,
                seconds_spec {d.BigInt} NOT NULL DEFAULT 0, seconds_queue {d.BigInt} NOT NULL DEFAULT 0{Keys(d, "stats_sessions")}){d.TableOptions}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS stats_rounds (id {d.IdPrimaryKey}, map_id {d.BigInt} NOT NULL, round_number {d.Int} NOT NULL,
                site {d.Text(1)} NULL, round_type {d.Text(64)} NULL, players_ct {d.Int} NOT NULL, players_t {d.Int} NOT NULL,
                ranked {d.Bool} NOT NULL, winner {d.Text(2)} NULL, end_reason {d.Int} NOT NULL, duration_ms {d.Int} NOT NULL,
                planted {d.Bool} NOT NULL, plant_site {d.Text(1)} NULL, planter {d.SteamId} NULL, plant_time_ms {d.Int} NULL,
                defused {d.Bool} NOT NULL, defuser {d.SteamId} NULL, defused_with_kit {d.Bool} NOT NULL, bomb_exploded {d.Bool} NOT NULL,
                started_at {d.DateTime} NOT NULL{Keys(d, "stats_rounds")}){d.TableOptions}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS stats_round_players (round_id {d.BigInt} NOT NULL, steam_id {d.SteamId} NOT NULL, side {d.Text(2)} NOT NULL,
                primary_weapon {d.Text(64)} NULL, survived {d.Bool} NOT NULL, won {d.Bool} NOT NULL,
                kills {d.Int} NOT NULL, deaths {d.Int} NOT NULL, assists {d.Int} NOT NULL, flash_assists {d.Int} NOT NULL, team_kills {d.Int} NOT NULL,
                damage {d.Int} NOT NULL, utility_damage {d.Int} NOT NULL, headshot_kills {d.Int} NOT NULL,
                shots {d.Int} NOT NULL, hits {d.Int} NOT NULL, headshot_hits {d.Int} NOT NULL,
                entry_kill {d.Bool} NOT NULL, entry_death {d.Bool} NOT NULL, trade_kills {d.Int} NOT NULL, traded_death {d.Bool} NOT NULL,
                kast {d.Bool} NOT NULL, clutch_vs {d.Int} NOT NULL, clutch_won {d.Bool} NOT NULL,
                enemies_flashed {d.Int} NOT NULL, teammates_flashed {d.Int} NOT NULL, blind_seconds_inflicted {d.Real} NOT NULL,
                mvp {d.Bool} NOT NULL, planted {d.Bool} NOT NULL, defused {d.Bool} NOT NULL, defused_with_kit {d.Bool} NOT NULL,
                rating {d.Real} NOT NULL, elo_before {d.Int} NOT NULL, elo_after {d.Int} NOT NULL,
                PRIMARY KEY (round_id, steam_id){Keys(d, "stats_round_players")}){d.TableOptions}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS stats_shots (id {d.IdPrimaryKey}, round_id {d.BigInt} NOT NULL, steam_id {d.SteamId} NOT NULL,
                weapon {d.Text(64)} NOT NULL, time_ms {d.Int} NOT NULL, tick {d.Int} NOT NULL,
                pos_x {d.Real} NULL, pos_y {d.Real} NULL, pos_z {d.Real} NULL, pitch {d.Real} NULL, yaw {d.Real} NULL{Keys(d, "stats_shots")}){d.TableOptions}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS stats_damages (id {d.IdPrimaryKey}, round_id {d.BigInt} NOT NULL, attacker {d.SteamId} NULL,
                victim {d.SteamId} NOT NULL, victim_is_bot {d.Bool} NOT NULL, weapon {d.Text(64)} NOT NULL, hitgroup {d.Int} NOT NULL,
                dmg_health {d.Int} NOT NULL, dmg_armor {d.Int} NOT NULL, health_after {d.Int} NOT NULL, time_ms {d.Int} NOT NULL, tick {d.Int} NOT NULL{Keys(d, "stats_damages")}){d.TableOptions}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS stats_kills (id {d.IdPrimaryKey}, round_id {d.BigInt} NOT NULL, attacker {d.SteamId} NULL,
                victim {d.SteamId} NOT NULL, victim_is_bot {d.Bool} NOT NULL, assister {d.SteamId} NULL, flash_assist {d.Bool} NOT NULL,
                weapon {d.Text(64)} NOT NULL, headshot {d.Bool} NOT NULL, penetrated {d.Int} NOT NULL, noscope {d.Bool} NOT NULL,
                thrusmoke {d.Bool} NOT NULL, attacker_blind {d.Bool} NOT NULL, attacker_in_air {d.Bool} NOT NULL, distance {d.Real} NOT NULL,
                attacker_x {d.Real} NULL, attacker_y {d.Real} NULL, attacker_z {d.Real} NULL,
                victim_x {d.Real} NULL, victim_y {d.Real} NULL, victim_z {d.Real} NULL,
                time_ms {d.Int} NOT NULL, is_team_kill {d.Bool} NOT NULL{Keys(d, "stats_kills")}){d.TableOptions}
            """,
            $"""
            CREATE TABLE IF NOT EXISTS stats_grenades (id {d.IdPrimaryKey}, round_id {d.BigInt} NOT NULL, steam_id {d.SteamId} NOT NULL,
                type {d.Text(16)} NOT NULL, thrown_ms {d.Int} NULL, detonated_ms {d.Int} NULL,
                throw_x {d.Real} NULL, throw_y {d.Real} NULL, throw_z {d.Real} NULL, det_x {d.Real} NULL, det_y {d.Real} NULL, det_z {d.Real} NULL,
                damage {d.Int} NOT NULL, enemies_blinded {d.Int} NOT NULL, teammates_blinded {d.Int} NOT NULL, blind_seconds {d.Real} NOT NULL{Keys(d, "stats_grenades")}){d.TableOptions}
            """,
            $"CREATE TABLE IF NOT EXISTS stats_player_totals (steam_id {d.SteamId} NOT NULL, scope {d.Text(80)} NOT NULL, {counters}, PRIMARY KEY (steam_id, scope)){d.TableOptions}",
            $"CREATE TABLE IF NOT EXISTS stats_player_weapons (steam_id {d.SteamId} NOT NULL, weapon {d.Text(64)} NOT NULL, {weapons}, PRIMARY KEY (steam_id, weapon)){d.TableOptions}",
            $"""
            CREATE TABLE IF NOT EXISTS stats_player_playtime (steam_id {d.SteamId} NOT NULL PRIMARY KEY,
                seconds_ct {d.BigInt} NOT NULL DEFAULT 0, seconds_t {d.BigInt} NOT NULL DEFAULT 0, seconds_spec {d.BigInt} NOT NULL DEFAULT 0,
                seconds_queue {d.BigInt} NOT NULL DEFAULT 0, sessions {d.BigInt} NOT NULL DEFAULT 0){d.TableOptions}
            """,
            $"CREATE TABLE IF NOT EXISTS stats_elo_history (steam_id {d.SteamId} NOT NULL, day {d.Text(10)} NOT NULL, elo {d.Int} NOT NULL, rounds {d.Int} NOT NULL, delta_sum {d.Int} NOT NULL, PRIMARY KEY (steam_id, day)){d.TableOptions}",        };
        if (!d.IsMySql)
        {
            migrations.AddRange(Indexes.SelectMany(t => t.Value.Select(i => $"CREATE INDEX IF NOT EXISTS {i.Name} ON {t.Key} ({i.Columns})")));
        }
        migrations.AddRange(StatsViews.For(d).Select(v => $"{d.CreateView} {v.Name} AS {v.Select}"));
        return migrations;
    }

    // MySQL declares indexes inline so that CREATE TABLE IF NOT EXISTS stays re-runnable; SQLite uses CREATE INDEX IF NOT EXISTS.
    private static string Keys(StatsDialect d, string table) => d.IsMySql
        ? string.Concat(Indexes[table].Select(i => $", KEY {i.Name} ({i.Columns})"))
        : string.Empty;
}
