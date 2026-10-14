# RetakeV4 stats — data reference

The `Stats` module records every Retake round: shots, damage, kills, grenades, bomb, sessions. On top of the raw data it stores additive per-player totals, a Retake Rating and an ELO. This page documents the **public views**: they are the stable contract for the web panel and for external websites. Read the views, never the `stats_*` tables (those can change between versions).

## Setup

1. In `stats.json` set `Database.Type` to `MySql`, with the same connection string and `ServerKey` as `allocation.json` (`RetakeV4.ConfigCheck` reports a mismatch).
2. Start the server once: tables and views are created automatically (the MySQL user needs `CREATE`, `CREATE VIEW`, `INDEX`, `INSERT`, `UPDATE`, `DELETE`, `SELECT`). Migrations are idempotent: tables, indexes and views can be re-applied safely, and several servers can start together on one database.
   The views are created with the default `SQL SECURITY DEFINER`, which is what lets a read-only user granted only on the views read them. The user that runs the plugin must therefore keep `CREATE VIEW` and must not be dropped or renamed later.
3. For an external website, create a read-only user limited to the views:

```sql
CREATE USER 'stats_reader'@'%' IDENTIFIED BY 'change-me';
GRANT SELECT ON retakev4.v_stats_players TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_player_scopes TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_player_weapons TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_leaderboard TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_maps TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_rounds TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_round_players TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_kills TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_shots TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_damages TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_grenades TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_sessions TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_elo_history TO 'stats_reader'@'%';
```

SQLite works too (same tables and views, file `plugins/RetakeV4/data/retakev4.db`), but only the game server can read it.

Supported databases: **MySQL 8.0.17 or newer** (the views use `CAST(... AS DOUBLE)`), **MariaDB 11.4** and SQLite. The full test suite runs against MySQL 8.0 and MariaDB 11.4.

### Player countries (optional)

The plugin does **not** bundle the database (DB-IP Lite is CC BY 4.0 and kept out of the package). Download the free **DB-IP Lite country** database in MMDB format (https://db-ip.com/db/download/ip-to-country-lite) once a month, unzip it and save it as `addons/counterstrikesharp/configs/plugins/RetakeV4/geo/dbip-country-lite.mmdb` (path set by `GeoDatabaseFile` in `stats.json`, relative to `configs/plugins/RetakeV4`). Without the file, `country` stays `NULL` and the console shows one information line at load. Only the 2-letter country code is stored. IP addresses are only read in memory for the lookup: they are never stored, logged or sent anywhere.

Attribution (required by CC BY 4.0): [IP Geolocation by DB-IP](https://db-ip.com).

## Conventions

- `steam_id`, `attacker`, `victim`, `assister`, `planter`, `defuser`: SteamID64 (`BIGINT UNSIGNED`). Read them as strings in JavaScript.
- Dates are UTC (`DATETIME(3)` on MySQL, `yyyy-MM-dd HH:mm:ss.fff` text on SQLite).
- Booleans are `0` / `1`.
- `side`, `winner`: `CT` or `T`. `site`, `plant_site`: `A` or `B`.
- `time_ms`: milliseconds since the round started.
- Positions (`*_x`, `*_y`, `*_z`) are world coordinates; `NULL` when unknown.
- Ratios are computed by the views and are `NULL` when their denominator is 0 (for example `kd` without deaths). They are floating-point (`DOUBLE` on MySQL, `REAL` on SQLite) in every view, rounded to 3 decimals.
- `*_pct`, `accuracy`, `hs_accuracy`, `survival_pct` and `win_pct` are percentages from 0 to 100. `kpr`, `kd`, `adr` and `rating` are plain ratios.
- Free-text values are truncated to their column size (name 128; map, round type and weapons 64).
- MySQL tables use `utf8mb4` with the binary collation `utf8mb4_bin`: keys and comparisons are case- and accent-sensitive.
- Bots never get stats. Kills on bots do not count as kills and damage to bots does not count as damage; it stays in the raw views (`victim_is_bot = 1`, `victim = 0`).
- Sessions are per map: a session closes at map change and reopens on the next map, so `sessions` counts map segments, not connections.
- A round is **ranked** when each side had at least `MinPlayersPerSide` humans at round start. Unranked rounds count in every total but do not change the ELO.
- Rounds that end without a T or CT winner (restart, game commencing) are not recorded.

## Definitions

| Term | Definition |
|---|---|
| Kill | Enemy killed by a human participant. Team kills (`team_kills`) and suicides are not kills; the victim still gets a death. |
| Assist | `player_death` assister, teammate of the killer. `flash_assists` when the assist came from a flash. |
| Entry | `entry_kills`: first kill of the round. `entry_deaths`: first victim. |
| Trade | A kill on A by B is traded when a teammate of A kills B within `TradeWindowSeconds` (5 s). The avenger gets `trade_kills`, A gets `traded_deaths`. One revenge trades one death. |
| KAST | Kill, Assist, Survived or Traded in the round. |
| Clutch | Last alive of their side against X ≥ 1 opponents. X is taken when the situation appears; won if the side wins the round. Buckets 1 to 5 (`clutch_5_*` = 5 or more). |
| Damage | Health damage to enemies, capped by the victim's remaining health. `utility_damage`: from HE and fire. |
| Shots / hits | A shot is a `weapon_fire` of a bullet weapon (knives, grenades, Zeus and C4 excluded). A hit is one (shooter, tick) pair, so a shotgun blast counts once. `headshot_hits`: hits on the head. |
| Flashes | `enemies_flashed`, `teammates_flashed` (self-flashes ignored), `blind_ms`: blind time inflicted on enemies. |
| `rating_milli` | Sum of the per-round Retake Rating × 1000. The `rating` column of the views is the mean per round. |

### Retake Rating (per round)

```
impact = EntryKill·0.5 + ClutchWon·(0.5·X) + Defuse·0.5 + Plant·0.3 + (3+ kills)·0.3
rating = 0.30 + 0.35·kills + 0.20·survived + 0.004·damage + 0.15·kast + 0.20·impact
```

The weights are configurable in `stats.json` → `Rating`.

### ELO (ranked rounds)

- Start 1000, floor 100, `K` = 24, `PlayerAdvantage` = 50 (configurable in `stats.json` → `Elo`).
- Side rating = mean ELO of its players + 50 × (its players − opponents).
- `E = 1 / (1 + 10^((R_opp − R_side) / 400))`; every player of the side gets `round(K × (S − E))`, where `S` is 1 for a win and 0 for a loss. The other side gets the opposite.
- Several servers can share one database: ELO changes are applied relatively.
- When the floor clamps a loss, `v_stats_round_players.elo_after - elo_before` can differ from the applied delta; `v_stats_elo_history.delta_sum` sums the raw (unclamped) deltas.
- Optional: the scoreboard (TAB) can show each player's ELO in the rank column, as a CS Rating value. It is **off by default** and needs two settings: `stats.json` → `"ScoreboardElo": true`, and `"FollowCS2ServerGuidelines": false` in CounterStrikeSharp's `configs/core.json`. Valve's server guidelines forbid showing competitive ranks on community servers, so turning that guideline off is your decision (a server breaking the guidelines risks its GSLT token). With only `ScoreboardElo` on, the plugin logs a warning and leaves the scoreboard alone. The game colours the value by its own CS Rating tiers (grey below 5 000), so typical ELO values show in grey. Nothing is shown until the player's stored ELO is loaded; bots show nothing.

## Views

### `v_stats_players` — one row per player (scope `all`)

`steam_id`, `name` (last seen), `first_seen`, `last_seen`, `elo`, play time in seconds (`seconds_ct`, `seconds_t`, `seconds_spec`, `seconds_queue`, `seconds_total`), `sessions`, every counter below, every ratio below.

**Counters** (also in `v_stats_player_scopes`): `rounds`, `rounds_won`, `ranked_rounds`, `survived_rounds`, `kills`, `deaths`, `assists`, `flash_assists`, `team_kills`, `damage`, `utility_damage`, `headshot_kills`, `shots`, `hits`, `headshot_hits`, `entry_kills`, `entry_deaths`, `trade_kills`, `traded_deaths`, `kast_rounds`, `k2`, `k3`, `k4`, `k5` (rounds with exactly 2, 3, 4, 5+ kills), `clutch_1_played` … `clutch_5_won`, `enemies_flashed`, `teammates_flashed`, `blind_ms`, `mvps`, `plants`, `defuses`, `defuses_with_kit`, `rating_milli`.

**Ratios**: `kpr` (kills per round), `kd`, `adr`, `hs_pct`, `accuracy`, `hs_accuracy` (head hits / hits), `kast_pct`, `survival_pct`, `win_pct`, `rating`, `entry_success_pct` (entry kills / entry duels), `clutch_won_pct`.

### `v_stats_player_scopes` — totals per player and scope

`steam_id`, `name`, `scope`, the counters, the ratios. `scope` is one of `all`, `map:<map>`, `site:A`, `site:B`, `roundtype:<type>`, `side:CT`, `side:T`.

### `v_stats_player_weapons`

`steam_id`, `weapon` (without `weapon_`), `shots`, `hits`, `headshot_hits`, `kills`, `headshot_kills`, `damage`, `accuracy`, `hs_pct`.

### `v_stats_leaderboard`

Players with at least one ranked round: `steam_id`, `name`, `elo`, `ranked_rounds`, `rounds`, `kills`, `deaths`, `damage`, `kpr`, `kd`, `adr`, `hs_pct`, `accuracy`, `kast_pct`, `win_pct`, `rating`. Sort it as you need.

### `v_stats_maps`, `v_stats_rounds`, `v_stats_round_players`

- `v_stats_maps`: `map_id`, `server` (`ServerKey`), `map_name`, `started_at`, `ended_at`.
- `v_stats_rounds`: `round_id`, `server`, `map_name`, `round_number`, `site`, `round_type`, `players_ct`, `players_t`, `ranked`, `winner`, `end_reason` (CS2 round end reason), `duration_ms`, `planted`, `plant_site`, `planter`, `plant_time_ms`, `defused`, `defuser`, `defused_with_kit`, `bomb_exploded`, `started_at`.
- `v_stats_round_players`: one row per player and round with the round context (`server`, `map_name`, `site`, `round_type`, `started_at`) and `side`, `primary_weapon`, `survived`, `won`, `kills`, `deaths`, `assists`, `flash_assists`, `team_kills`, `damage`, `utility_damage`, `headshot_kills`, `shots`, `hits`, `headshot_hits`, `entry_kill`, `entry_death`, `trade_kills`, `traded_death`, `kast`, `clutch_vs` (0 = no clutch), `clutch_won`, `enemies_flashed`, `teammates_flashed`, `blind_seconds_inflicted`, `mvp`, `planted`, `defused`, `defused_with_kit`, `rating`, `elo_before`, `elo_after`.

### Raw events

- `v_stats_kills`: `kill_id`, `round_id`, `map_name`, `site`, `round_type`, `started_at`, `attacker`, `victim`, `victim_is_bot`, `assister`, `flash_assist`, `weapon`, `headshot`, `penetrated`, `noscope`, `thrusmoke`, `attacker_blind`, `attacker_in_air`, `distance`, `attacker_x/y/z`, `victim_x/y/z`, `time_ms`, `is_team_kill`.
- `v_stats_shots`: `shot_id`, `round_id`, `steam_id`, `weapon`, `time_ms`, `tick`, `pos_x/y/z`, `pitch`, `yaw`.
- `v_stats_damages`: `damage_id`, `round_id`, `attacker`, `victim`, `victim_is_bot`, `weapon`, `hitgroup` (1 = head), `dmg_health`, `dmg_armor`, `health_after`, `time_ms`, `tick`.
- `v_stats_grenades`: `grenade_id`, `round_id`, `steam_id`, `type` (`He`, `Flash`, `Smoke`, `Molotov`, `Decoy`), `thrown_ms`, `detonated_ms`, `throw_x/y/z`, `det_x/y/z`, `damage` (to enemies), `enemies_blinded`, `teammates_blinded`, `blind_seconds`.

Shots, damages and player events of rounds older than `RawRetentionDays` (30 by default, 0 = keep), and presence snapshots and server events older than that, are purged at each map start; everything else is kept. The purge assumes round ids grow with time (true in normal operation).

### `v_stats_sessions`, `v_stats_elo_history`

- `v_stats_sessions`: `session_id`, `steam_id`, `server`, `map_name`, `connected_at` (`NULL` if the opening was lost), `disconnected_at`, `disconnect_reason`, `seconds_ct`, `seconds_t`, `seconds_spec`, `seconds_queue`.
- `v_stats_elo_history`: `steam_id`, `day` (`yyyy-MM-dd`, UTC), `elo` at the end of the day, `rounds`, `delta_sum`.

## Views v2 (4.9)

`contract/stats/views.v2.json` lists the views added in 4.9. The v1 views are unchanged; the `v2_stats_*` views repeat their v1 counterpart with the new columns.

- `v2_stats_players`, `v2_stats_player_scopes`: v1 columns plus `country` (players only), the counters `shots_still`, `hits_still`, `shots_moving`, `hits_moving`, `shots_scoped`, `hits_scoped`, `reloads`, `reloads_full`, `pickups`, `drops`, `jumps`, `plant_attempts`, `plant_aborts`, `defuse_attempts`, `defuse_aborts`, `clutch_defuses`, `damage_taken`, `bomb_carry_ms`, and the ratios `still_accuracy`, `moving_accuracy`, `scoped_accuracy` (hits / shots, %), `still_shot_pct` (still shots / still + moving shots, %), `dtr` (damage taken per round).
- `v2_stats_round_players`: v1 columns plus `shots_still` ... `hits_scoped`, `damage_taken`, `bomb_carry_ms`, `health_end`, `armor_end` (0 for a player who died or left during the round; `NULL` only for a living player whose state could not be read at round end).
- `v2_stats_shots`: v1 columns plus `speed` (horizontal, u/s), `crouched`, `scoped`, `airborne`, `walking`, `still` (`NULL` when the pawn could not be read).
- `v2_stats_sessions`: v1 columns plus `seconds_vip`, `ping_avg`, `ping_max` (ms, `NULL` without a sample), `country`.
- `v_stats_player_events`: one row per player event: `event_id`, `round_id`, `map_name`, `site`, `round_type`, `started_at`, `steam_id`, `type`, `time_ms`, `value`, `pos_x/y/z`. Types: `reload`, `reload_full_clip`, `pickup`, `drop`, `jump`, `plant_begin`, `plant_abort`, `defuse_begin`, `defuse_abort`, `bomb_pickup`, `bomb_drop`. `value`: the weapon (pickup, drop), `weapon:clip/max` (reloads), `kit` (defuse started with a kit), `start` (bomb carrier at the round start).
- `v_stats_player_network`: per player `country`, `sessions`, `sessions_with_ping`, `ping_avg` (average of session averages), `ping_max`, `seconds_vip`.
- `v_stats_server_health`: `event_id`, `server`, `at`, `type`, `module`, `value`, `occurrences`, `max_ms`. Types: `plugin_start` / `hot_reload` (value = version), `module_error` / `module_disabled` (module, value = stage), `slow_handler` (module, stage, `max_ms`), `long_frame` (`max_ms`), `uptime` (every 10 min, value = seconds since the plugin's Stats module loaded: reset by a plugin reload or a server restart, and nothing is written while the server hibernates), `map_vote` (value = map, `occurrences` = votes), `map_vote_result` (winning map and its votes), `command` (module, command name only, never the arguments), `chat` (`say` / `say_team`, counted by command name only, never the text). Repeated types (`module_error`, `module_disabled`, `slow_handler`, `long_frame`, `command`, `chat`) are aggregated: one row per (type, module, value) and minute, `occurrences` = count, `max_ms` = longest duration.
- `v_stats_presence`: `snapshot_id`, `server`, `at`, `map_name`, `humans`, `bots`, `queued`, every `PresenceSnapshotSeconds`.

### Definitions (v2)

- **Still shot**: on the ground and at a horizontal speed of at most 34 % of the weapon's max speed (CS2 weapon data; scoped speed for scoped rifles). It is not "stopped": a slow walk can still count. A hit takes the state of the shot fired in the same tick by the same player.
- **reload_full_clip**: a reload started with at least half of the magazine left (CS2 never reloads a full one). Each reload is one event, `reload` or `reload_full_clip`; `reloads` counts both.
- **pickups / drops**: grenades and the bomb are never pickups or drops (neither stored nor counted); pickups before the freeze ends (spawn equipment; when the freeze end is unknown, pickups at 0 ms) and drops from the player's death on are stored but not counted.
- **bomb_carry_ms**: from `bomb_pickup` until the carrier's `bomb_drop`, the plant, their death, another pickup or the round end. Freeze time counts: the carrier given the bomb at the round start carries it from 0 ms.
- **clutch_defuses**: defuses with less than 1 s left on the bomb.
- **damage_taken**: health lost from any source (enemies, teammates, world, bots), capped by the health left.
- **seconds_vip**: time queued with a priority above 0 (part of `seconds_queue`).
- **ping**: sampled every `PingSampleSeconds`, capped at 999 ms. Packet loss is not recorded.
- **Long frame**: a server frame of at least `LongFrameMs` (gaps of 10 s or more are pauses, not frames).

New `stats.json` keys: `PresenceSnapshotSeconds` (60, 0 = off), `PingSampleSeconds` (30, 0 = off), `LongFrameMs` (50), `GeoDatabaseFile` (`geo/dbip-country-lite.mmdb`), `TrackServerHealth` (true), `TrackUsage` (true).

## Reliability

Nothing is written on the game thread. Writes go through a bounded queue (`QueueCapacity`): when it is full, the oldest pending write is dropped.

- Telemetry yields first: presence snapshots and server events (health, usage, uptime, map votes) are skipped, never queued, while half of `QueueCapacity` or more is pending. A database outage therefore never pushes rounds, sessions or maps out of the queue because of telemetry; one warning is logged per skip burst. The server events of one flush (one minute of aggregated rows, or one map vote) are one write, in one transaction.

- Database unreachable at startup: table creation is retried with the same backoff (1 s, doubling up to 60 s) until it succeeds; one error is logged, then warnings. Rounds keep queuing meanwhile and are written once the database is up.
- During a database outage each pending write is retried for about 4 minutes (10 attempts, 1 s doubling up to 60 s), then dropped with an error log.
- A round write retried after a lost `COMMIT` acknowledgment can, rarely, be counted twice.
- On shutdown the queue drains for `ShutdownFlushSeconds`, then gives up.

## Example queries

```sql
-- Top 10 by ELO
SELECT name, elo, rating, adr, kd FROM v_stats_leaderboard ORDER BY elo DESC LIMIT 10;

-- A player's retake win rate per site
SELECT scope, rounds, win_pct FROM v_stats_player_scopes
WHERE steam_id = 76561198000000001 AND scope LIKE 'site:%';

-- Kill positions on de_mirage, site A, for a heatmap
SELECT victim_x, victim_y FROM v_stats_kills WHERE map_name = 'de_mirage' AND site = 'A';

-- Best clutchers (at least 20 clutches)
SELECT p.name, p.clutch_won_pct FROM v_stats_players p
WHERE p.clutch_1_played + p.clutch_2_played + p.clutch_3_played + p.clutch_4_played + p.clutch_5_played >= 20
ORDER BY p.clutch_won_pct DESC;

-- Hours played
SELECT name, ROUND(seconds_total / 3600.0, 1) AS hours FROM v_stats_players ORDER BY seconds_total DESC;
```

## Contract

`contract/stats/views.v1.json` and `contract/stats/views.v2.json` list every view with its columns in order; the plugin's tests compare them with the real schema on SQLite (and MySQL / MariaDB when available). Views only grow by new views: v1 never changes, 4.9 additions are in v2.
