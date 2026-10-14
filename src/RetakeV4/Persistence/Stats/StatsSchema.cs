namespace RetakeV4.Persistence.Stats;

public static class StatsSchema
{
    public const string VersionTable = "stats_schema_version";

    // Frozen counter columns of the v1 stats_player_totals / stats_player_weapons tables. Migrations must not follow the
    // live StatsCounters lists: a new counter is added by an ALTER TABLE migration appended below.
    internal static readonly IReadOnlyList<string> V1Counters = new[]
    {
        "rounds", "rounds_won", "ranked_rounds", "survived_rounds",
        "kills", "deaths", "assists", "flash_assists", "team_kills",
        "damage", "utility_damage", "headshot_kills", "shots", "hits", "headshot_hits",
        "entry_kills", "entry_deaths", "trade_kills", "traded_deaths", "kast_rounds",
        "k2", "k3", "k4", "k5",
        "clutch_1_played", "clutch_1_won", "clutch_2_played", "clutch_2_won", "clutch_3_played", "clutch_3_won",
        "clutch_4_played", "clutch_4_won", "clutch_5_played", "clutch_5_won",
        "enemies_flashed", "teammates_flashed", "blind_ms", "mvps", "plants", "defuses", "defuses_with_kit", "rating_milli",
    };

    internal static readonly IReadOnlyList<string> V1WeaponCounters = new[] { "shots", "hits", "headshot_hits", "kills", "headshot_kills", "damage" };

    // Frozen counter columns added to stats_player_totals in 4.9 (StatsCounters.V2Columns).
    internal static readonly IReadOnlyList<string> V2Counters = new[]
    {
        "shots_still", "hits_still", "shots_moving", "hits_moving", "shots_scoped", "hits_scoped",
        "reloads", "reloads_full", "pickups", "drops", "jumps", "plant_attempts", "plant_aborts", "defuse_attempts", "defuse_aborts",
        "clutch_defuses", "damage_taken", "bomb_carry_ms",
    };

    // Frozen counter columns added to stats_player_totals by the 4.9-b step (StatsCounters.MovementColumns).
    internal static readonly IReadOnlyList<string> MovementCounters = new[]
    {
        "alive_ms", "walk_samples", "run_samples", "crouch_samples", "still_samples", "air_samples", "alive_samples",
        "distance_cm", "afk_ms", "in_smoke_samples", "behind_smoke_samples", "first_contact_ms_sum", "first_contact_rounds",
        "engage_order_sum", "bait_rounds", "bait_chances",
    };

    // Frozen per-round movement columns added to stats_round_players by the 4.9-b step, in order.
    internal static readonly IReadOnlyList<string> MovementRoundColumns = new[]
    {
        "alive_ms", "walk_pct", "run_pct", "crouch_pct", "still_pct", "air_pct", "distance_m", "afk_ms", "in_smoke_pct",
        "behind_smoke_pct", "first_contact_ms", "engage_order", "bait",
    };

    // Frozen special kill columns added to stats_player_totals and stats_round_players by the 4.9-c step, in order
    // (StatsCounters.SpecialKillColumns).
    internal static readonly IReadOnlyList<string> SpecialKillCounters = new[] { "noscope_kills", "wallbang_kills", "smoke_kills", "blind_kills", "air_kills" };

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

    // Indexes of the tables created in 4.9 (kept apart so that the v1 entries never change).
    public static IReadOnlyDictionary<string, (string Name, string Columns)[]> V2Indexes { get; } = new Dictionary<string, (string Name, string Columns)[]>
    {
        ["stats_player_events"] = [("ix_stats_player_events_round", "round_id"), ("ix_stats_player_events_steam", "steam_id, round_id")],
        ["stats_presence"] = [("ix_stats_presence_at", "at")],
        ["stats_server_events"] = [("ix_stats_server_events_at", "at"), ("ix_stats_server_events_type", "type, at")],
    };

    // Append-only: SqlMigrator applies entries by index, so an existing entry is never edited, reordered or removed.
    public static IReadOnlyList<SqlMigration> Migrations(StatsDialect d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return V1(d).Select(sql => new SqlMigration(sql)).Concat(V2(d)).Concat(V2Movement(d)).Concat(V2Catalog(d)).ToList();
    }

    // Number of 4.8 entries: a 4.8 database has this version.
    internal static int V1Count(StatsDialect d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return V1(d).Count;
    }

    // Number of entries up to the end of 4.9-a (sub-project 1): a database of that stage has this version.
    internal static int V2EventsCount(StatsDialect d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return V1(d).Count + V2(d).Count;
    }

    // Number of entries up to the end of 4.9-b (sub-project 2): a database of that stage has this version.
    internal static int V2MovementCount(StatsDialect d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return V2EventsCount(d) + V2Movement(d).Count;
    }

    // Number of entries up to the end of 4.9-c (sub-project 4): a database of that stage has this version.
    internal static int V2CatalogCount(StatsDialect d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return V2MovementCount(d) + V2Catalog(d).Count;
    }

    private static List<string> V1(StatsDialect d)
    {
        var counters = string.Join(", ", V1Counters.Select(c => $"{c} {d.BigInt} NOT NULL DEFAULT 0"));
        var weapons = string.Join(", ", V1WeaponCounters.Select(c => $"{c} {d.BigInt} NOT NULL DEFAULT 0"));
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
            $"CREATE TABLE IF NOT EXISTS stats_elo_history (steam_id {d.SteamId} NOT NULL, day {d.Text(10)} NOT NULL, elo {d.Int} NOT NULL, rounds {d.Int} NOT NULL, delta_sum {d.Int} NOT NULL, PRIMARY KEY (steam_id, day)){d.TableOptions}",
        };
        if (!d.IsMySql)
        {
            migrations.AddRange(Indexes.SelectMany(t => t.Value.Select(i => $"CREATE INDEX IF NOT EXISTS {i.Name} ON {t.Key} ({i.Columns})")));
        }
        migrations.AddRange(StatsViews.For(d).Select(v => $"{d.CreateView} {v.Name} AS {v.Select}"));
        return migrations;
    }

    // 4.9 (Stats v2, sub-project 1). Columns of existing tables are added by replayable ALTERs.
    private static List<SqlMigration> V2(StatsDialect d)
    {
        var counter = $"{d.Int} NOT NULL DEFAULT 0";
        var migrations = new List<SqlMigration>();
        migrations.AddRange(V2Counters.Select(c => SqlMigration.AddColumn("stats_player_totals", c, $"{d.BigInt} NOT NULL DEFAULT 0")));
        migrations.AddRange(new[] { "shots_still", "hits_still", "shots_moving", "hits_moving", "shots_scoped", "hits_scoped", "damage_taken", "bomb_carry_ms" }
            .Select(c => SqlMigration.AddColumn("stats_round_players", c, counter)));
        migrations.Add(SqlMigration.AddColumn("stats_round_players", "health_end", $"{d.Int} NULL"));
        migrations.Add(SqlMigration.AddColumn("stats_round_players", "armor_end", $"{d.Int} NULL"));
        migrations.Add(SqlMigration.AddColumn("stats_shots", "speed", $"{d.Real} NULL"));
        migrations.AddRange(new[] { "crouched", "scoped", "airborne", "walking", "still" }
            .Select(c => SqlMigration.AddColumn("stats_shots", c, $"{d.Bool} NULL")));
        migrations.Add(SqlMigration.AddColumn("stats_sessions", "seconds_vip", $"{d.BigInt} NOT NULL DEFAULT 0"));
        migrations.Add(SqlMigration.AddColumn("stats_sessions", "ping_avg", $"{d.Int} NULL"));
        migrations.Add(SqlMigration.AddColumn("stats_sessions", "ping_max", $"{d.Int} NULL"));
        migrations.Add(SqlMigration.AddColumn("stats_sessions", "country", $"{d.Text(2)} NULL"));
        migrations.Add(SqlMigration.AddColumn("stats_players", "country", $"{d.Text(2)} NULL"));
        migrations.Add(new SqlMigration($"""
            CREATE TABLE IF NOT EXISTS stats_player_events (id {d.IdPrimaryKey}, round_id {d.BigInt} NOT NULL, steam_id {d.SteamId} NOT NULL,
                type {d.Text(24)} NOT NULL, time_ms {d.Int} NOT NULL, value {d.Text(32)} NULL,
                pos_x {d.Real} NULL, pos_y {d.Real} NULL, pos_z {d.Real} NULL{Keys(d, "stats_player_events")}){d.TableOptions}
            """));
        migrations.Add(new SqlMigration($"""
            CREATE TABLE IF NOT EXISTS stats_presence (id {d.IdPrimaryKey}, server_id {d.BigInt} NOT NULL, at {d.DateTime} NOT NULL,
                humans {d.Int} NOT NULL, bots {d.Int} NOT NULL, queued {d.Int} NOT NULL, map_name {d.Text(64)} NOT NULL{Keys(d, "stats_presence")}){d.TableOptions}
            """));
        migrations.Add(new SqlMigration($"""
            CREATE TABLE IF NOT EXISTS stats_server_events (id {d.IdPrimaryKey}, server_id {d.BigInt} NOT NULL, at {d.DateTime} NOT NULL,
                type {d.Text(24)} NOT NULL, module {d.Text(64)} NULL, value {d.Text(128)} NULL,
                occurrences {d.Int} NOT NULL, max_ms {d.Int} NULL{Keys(d, "stats_server_events")}){d.TableOptions}
            """));
        if (!d.IsMySql)
        {
            migrations.AddRange(V2Indexes.SelectMany(t => t.Value.Select(i => new SqlMigration($"CREATE INDEX IF NOT EXISTS {i.Name} ON {t.Key} ({i.Columns})"))));
        }
        migrations.AddRange(StatsViewsV2.Events(d).Select(v => new SqlMigration($"{d.CreateView} {v.Name} AS {v.Select}")));
        return migrations;
    }

    // 4.9-b (Stats v2, sub-project 2): movement columns, then the v2 views that show them. SQLite's CREATE VIEW IF NOT
    // EXISTS never replaces a view, so each changed view is dropped first (MySQL's CREATE OR REPLACE would do; DROP keeps
    // the dialects alike and the step replayable). Built from the frozen StatsViewsV2.Movement, never from the live For:
    // a later change to these views is a new step.
    private static List<SqlMigration> V2Movement(StatsDialect d)
    {
        var migrations = MovementCounters.Select(c => SqlMigration.AddColumn("stats_player_totals", c, $"{d.BigInt} NOT NULL DEFAULT 0")).ToList();
        migrations.AddRange(MovementRoundColumns.Select(c => SqlMigration.AddColumn("stats_round_players", c, MovementRoundType(d, c))));
        foreach (var view in StatsViewsV2.Movement(d).Where(v => StatsViewsV2.MovementViews.Contains(v.Name)))
        {
            migrations.Add(new SqlMigration($"DROP VIEW IF EXISTS {view.Name}"));
            migrations.Add(new SqlMigration($"{d.CreateView} {view.Name} AS {view.Select}"));
        }
        return migrations;
    }

    private static string MovementRoundType(StatsDialect d, string column) => column switch
    {
        "alive_ms" or "afk_ms" or "first_contact_ms" or "engage_order" => $"{d.Int} NULL",
        "bait" => $"{d.Bool} NULL",
        _ => $"{d.Real} NULL",
    };

    // 4.9-c (Stats v2, sub-project 4): the special kill columns, then the player views that show them and the derived
    // views, each dropped first so that a database created by an intermediate build gets the final definition (SQLite's
    // CREATE VIEW IF NOT EXISTS never replaces a view). Built from StatsViewsV2.Catalog, never from the live For: a later
    // change to these views is a new step.
    private static List<SqlMigration> V2Catalog(StatsDialect d)
    {
        var migrations = SpecialKillCounters.Select(c => SqlMigration.AddColumn("stats_player_totals", c, $"{d.BigInt} NOT NULL DEFAULT 0")).ToList();
        migrations.AddRange(SpecialKillCounters.Select(c => SqlMigration.AddColumn("stats_round_players", c, $"{d.Int} NOT NULL DEFAULT 0")));
        foreach (var view in StatsViewsV2.Catalog(d).Where(v => StatsViewsV2.CatalogViews.Contains(v.Name)))
        {
            migrations.Add(new SqlMigration($"DROP VIEW IF EXISTS {view.Name}"));
            migrations.Add(new SqlMigration($"{d.CreateView} {view.Name} AS {view.Select}"));
        }
        return migrations;
    }

    // MySQL declares indexes inline so that CREATE TABLE IF NOT EXISTS stays re-runnable; SQLite uses CREATE INDEX IF NOT EXISTS.
    private static string Keys(StatsDialect d, string table) => d.IsMySql
        ? string.Concat((Indexes.TryGetValue(table, out var v1) ? v1 : V2Indexes[table]).Select(i => $", KEY {i.Name} ({i.Columns})"))
        : string.Empty;
}
