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

## Conventions

- `steam_id`, `attacker`, `victim`, `assister`, `planter`, `defuser`: SteamID64 (`BIGINT UNSIGNED`). Read them as strings in JavaScript.
- Dates are UTC (`DATETIME(3)` on MySQL, `yyyy-MM-dd HH:mm:ss.fff` text on SQLite).
- Booleans are `0` / `1`.
- `side`, `winner`: `CT` or `T`. `site`, `plant_site`: `A` or `B`.
- `time_ms`: milliseconds since the round started.
- Positions (`*_x`, `*_y`, `*_z`) are world coordinates; `NULL` when unknown.
- Ratios are computed by the views and are `NULL` when their denominator is 0 (for example `kd` without deaths). They are floating-point (`DOUBLE` on MySQL, `REAL` on SQLite) in every view.
- Free-text values are truncated to their column size (name 128; map, round type and weapons 64).
- MySQL tables use `utf8mb4` with the binary collation `utf8mb4_bin`: keys and comparisons are case- and accent-sensitive.
- Bots never get stats. Damage dealt to bots stays in `v_stats_damages` (`victim_is_bot = 1`, `victim = 0`).
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

Shots and damages older than `RawRetentionDays` (30 by default, 0 = keep) are purged at each map start; everything else is kept. The purge assumes round ids grow with time (true in normal operation).

### `v_stats_sessions`, `v_stats_elo_history`

- `v_stats_sessions`: `session_id`, `steam_id`, `server`, `map_name`, `connected_at` (`NULL` if the opening was lost), `disconnected_at`, `disconnect_reason`, `seconds_ct`, `seconds_t`, `seconds_spec`, `seconds_queue`.
- `v_stats_elo_history`: `steam_id`, `day` (`yyyy-MM-dd`, UTC), `elo` at the end of the day, `rounds`, `delta_sum`.

## Reliability

Nothing is written on the game thread. Writes go through a bounded queue (`QueueCapacity`): when it is full, the oldest round is dropped. A failing write is retried with backoff (1 s, doubling up to 60 s) and dropped after 10 attempts with an error log. On shutdown the queue drains for `ShutdownFlushSeconds`, then gives up.

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

`contract/stats/views.v1.json` lists every view with its columns in order; the plugin's tests compare it with the real schema on SQLite (and MySQL when available). Views only grow by new views: an incompatible change creates `views.v2.json` and new view names.
