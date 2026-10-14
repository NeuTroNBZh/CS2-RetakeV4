# RetakeV4 stats — catalogue

Every statistic the `Stats` module stores or lets you compute, by category: what it means, where to read it (a view column written `view.column`, or a complete SQL query) and its limits. Column types, conventions and setup are in [STATS.md](STATS.md); building a website on top of the views is in [STATS-INTEGRATION.md](STATS-INTEGRATION.md).

Read the views only (`v_stats_*`, `v2_stats_*`): the `stats_*` tables can change between versions. The view columns are frozen by `contract/stats/views.v1.json` and `contract/stats/views.v2.json`; a test checks that every view and every column of both files is named on this page (a column name shared by several views must be written `view.column`).

**Limits that apply everywhere**

- Bots never get stats and are never actors: kills on bots are not kills, damage to bots is not damage, a bot's kill has no attacker. Humans who join after the round start are not participants of that round.
- Raw rows are purged after `RawRetentionDays` (30 by default, 0 = keep): `v_stats_shots`, `v2_stats_shots`, `v_stats_damages`, `v_stats_player_events`, `v_stats_presence`, `v_stats_server_health`. Kills, grenades, rounds, round players, sessions, ELO history and every total are kept.
- Totals are additive counters updated once per round. A counter added in 4.9 is 0 for the rounds played before; a ratio built on it is therefore only meaningful over recent play.
- Ratios are computed by the views, rounded to 3 decimals, `NULL` when the denominator is 0. `*_pct` and `accuracy` columns are 0–100; `kd`, `kpr`, `adr`, `rating`, `bait_rate`, `slowness_index` are plain ratios.
- SteamID64 values (`steam_id`, `attacker`, `victim`, `assister`, `planter`, `defuser`, `opponent_id`, `mate_id`) are 64-bit unsigned: read them as strings in JavaScript. Dates are UTC.
- Queries below use MySQL 8 / MariaDB syntax and also run on SQLite unless noted. Every value comes in as a `?` parameter: never build the SQL by concatenation.
- MySQL `SUM()` returns `DECIMAL` (drivers give strings): wrap your own sums in `CAST(... AS SIGNED)` or convert in code. The derived views already cast theirs.

## Views at a glance

| View | Contract | One row per | Raw (purged) |
|---|---|---|---|
| `v_stats_players` | v1 | player, scope `all` | no |
| `v_stats_player_scopes` | v1 | player and scope | no |
| `v_stats_player_weapons` | v1 | player and weapon | no |
| `v_stats_leaderboard` | v1 | player with a ranked round | no |
| `v_stats_maps` | v1 | map played on a server | no |
| `v_stats_rounds` | v1 | round | no |
| `v_stats_round_players` | v1 | player and round | no |
| `v_stats_kills` | v1 | death event | no |
| `v_stats_shots` | v1 | bullet shot | yes |
| `v_stats_damages` | v1 | damage event | yes |
| `v_stats_grenades` | v1 | grenade thrown | no |
| `v_stats_sessions` | v1 | player session (one map) | no |
| `v_stats_elo_history` | v1 | player and UTC day | no |
| `v2_stats_players` | v2 | player, scope `all` (v1 columns + 4.9 columns) | no |
| `v2_stats_player_scopes` | v2 | player and scope (v1 columns + 4.9 columns) | no |
| `v2_stats_round_players` | v2 | player and round (v1 columns + 4.9 columns) | no |
| `v2_stats_shots` | v2 | bullet shot with the shooter's motion | yes |
| `v2_stats_sessions` | v2 | session with VIP time, ping, country | no |
| `v_stats_player_events` | v2 | player event (reload, pickup, jump, bomb…) | yes |
| `v_stats_player_network` | v2 | player (ping, VIP time) | no |
| `v_stats_server_health` | v2 | server event (start, errors, votes, commands…) | yes |
| `v_stats_presence` | v2 | presence snapshot | yes |
| `v_stats_player_behaviour` | v2 | player (slow player, baiter) | no |
| `v_stats_retake_odds` | v2 | server, map, site, round type, ranked, player counts, situation at plant | no |
| `v_stats_side_balance` | v2 | server, map and site | no |
| `v_stats_weapon_meta` | v2 | weapon | no |
| `v_stats_rivals` | v2 | player and enemy | no |
| `v_stats_duos` | v2 | player and teammate | no |
| `v_stats_server_activity` | v2 | server, UTC day and hour | partly (presence part) |

## 1. Players and play time

| Stat | Definition | Source | Limits |
|---|---|---|---|
| `steam_id` | SteamID64 of the player | `v_stats_players.steam_id` and every per-player view | 64-bit, read as a string |
| `name` | Last name seen | `v_stats_players.name`, `v_stats_rivals.opponent_name`, `v_stats_duos.mate_name` | Chosen by the player: escape it before showing it; truncated to 128 characters |
| `country` | ISO 3166-1 alpha-2 code from the DB-IP Lite database at connection | `v2_stats_players.country`, `v2_stats_sessions.country`, `v_stats_player_network.country` | `NULL` without the MMDB file or for unknown addresses; IP addresses are never stored |
| `first_seen` / `last_seen` | First and last round or connection seen | `v_stats_players.first_seen`, `v_stats_players.last_seen` | UTC |
| `seconds_ct`, `seconds_t`, `seconds_spec`, `seconds_queue` | Seconds spent as CT, T, spectator, in the waiting queue | `v_stats_players.seconds_ct` … (totals), `v_stats_sessions.seconds_queue` … (per session) | Counted while connected, per map session |
| `seconds_total` | Sum of the four above | `v_stats_players.seconds_total` | |
| `seconds_vip` | Seconds queued with a priority above 0 (part of `seconds_queue`) | `v2_stats_sessions.seconds_vip`, `v_stats_player_network.seconds_vip` | 4.9+ only |
| `sessions` | Map segments played (a session closes at each map change) | `v_stats_players.sessions`, `v_stats_player_network.sessions`, `v_stats_server_activity.sessions` | Not connections: a player staying for 3 maps has 3 sessions |
| Session | One row per player and map: `session_id`, `connected_at`, `disconnected_at`, `disconnect_reason` (raw CS2 reason code) | `v_stats_sessions`, `v2_stats_sessions` | `connected_at` is `NULL` when the opening write was lost |
| Ping | Sampled every `PingSampleSeconds` (30), averaged per session: `ping_avg`, `ping_max` (ms); `sessions_with_ping` = sessions with a sample | `v2_stats_sessions.ping_avg`, `v_stats_player_network.ping_avg` (average of the session averages), `v_stats_player_network.ping_max`, `v_stats_player_network.sessions_with_ping` | Capped at 999 ms; packet loss is not exposed by the game |

Hours played (query):

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, name, ROUND(seconds_total / 3600.0, 1) AS hours
FROM v_stats_players ORDER BY seconds_total DESC LIMIT 50;
```

New players per UTC day (query):

```sql
SELECT DATE(first_seen) AS day, COUNT(*) AS new_players
FROM v_stats_players GROUP BY DATE(first_seen) ORDER BY day;
```

## 2. Rating and ELO

| Stat | Definition | Source | Limits |
|---|---|---|---|
| `rating` | Retake Rating: per-round score (kills, survival, damage, KAST, impact; formula in STATS.md), averaged over the rounds | `v_stats_players.rating`, `v_stats_player_scopes.rating`, `v_stats_leaderboard.rating`; per round `v_stats_round_players.rating` | Weights configurable in `stats.json`: compare servers with the same weights |
| `rating_milli` | Sum of the per-round ratings × 1000 (to recompute averages over any set of scopes) | `v_stats_players.rating_milli` | |
| `elo` | Current ELO (start 1000, floor 100), changed by ranked rounds only | `v_stats_players.elo`, `v_stats_leaderboard.elo`, `v_stats_elo_history.elo` | Several servers sharing a database share one ELO |
| `elo_before` / `elo_after` | ELO at the start and the end of a round | `v_stats_round_players.elo_before`, `v_stats_round_players.elo_after` | When the floor clamps a loss, `elo_after - elo_before` differs from the raw delta |
| ELO history | Per player and UTC `day`: `elo` at the end of the day, ranked `rounds`, `delta_sum` (sum of the raw deltas) | `v_stats_elo_history.day`, `v_stats_elo_history.rounds`, `v_stats_elo_history.delta_sum` | Days without a ranked round have no row |
| `ranked_rounds` | Rounds where each side had at least `MinPlayersPerSide` humans | `v_stats_players.ranked_rounds`, `v_stats_leaderboard.ranked_rounds`, `v_stats_side_balance.ranked_rounds`, `v_stats_server_activity.ranked_rounds` | Unranked rounds count in every total but leave the ELO alone |
| `ranked` | 1 when the round was ranked | `v_stats_rounds.ranked`, `v_stats_retake_odds.ranked` | |
| Leaderboard | Players with at least one ranked round, with `kpr`, `kd`, `adr`, `hs_pct`, `accuracy`, `kast_pct`, `win_pct`, `rating` | `v_stats_leaderboard` | Sort and filter (minimum rounds) on the site side |

## 3. Rounds, wins and survival

| Stat | Definition | Source | Limits |
|---|---|---|---|
| `rounds` | Rounds played as a participant | `v_stats_players.rounds`, `v_stats_player_scopes.rounds` | Rounds without a T or CT winner are not recorded |
| `rounds_won` / `win_pct` | Rounds won by the player's side, and their share | `v_stats_players.rounds_won`, `v_stats_players.win_pct`; per round `v_stats_round_players.won` | |
| `survived_rounds` / `survival_pct` | Rounds survived | `v_stats_players.survived_rounds`, `v_stats_players.survival_pct`; per round `v_stats_round_players.survived` | |
| `kast_rounds` / `kast_pct` | Rounds with a Kill, Assist, Survival or Traded death | `v_stats_players.kast_rounds`, `v_stats_players.kast_pct`; per round `v_stats_round_players.kast` | |
| `mvps` | Round MVP stars | `v_stats_players.mvps`; per round `v_stats_round_players.mvp` | As awarded by the game |
| `damage_taken` / `dtr` | Health lost from any source (enemies, teammates, world, bots), capped by the health left; `dtr` = per round | `v2_stats_players.damage_taken`, `v2_stats_players.dtr`, `v2_stats_round_players.damage_taken` | 4.9+ only |
| `health_end` / `armor_end` | Health and armour at the round end | `v2_stats_round_players.health_end`, `v2_stats_round_players.armor_end` | 0 for a player who died or left; `NULL` only when a living player could not be read |

## 4. Kills and damage

| Stat | Definition | Source | Limits |
|---|---|---|---|
| `kills` | Enemies killed (team kills and suicides excluded) | `v_stats_players.kills`, `v_stats_round_players.kills` | Kills on bots excluded |
| `deaths` | Deaths of the player (any cause) | `v_stats_players.deaths`, `v_stats_round_players.deaths` | A duplicated death event counts once |
| `assists` / `flash_assists` | Kill assists, and those given by a flash | `v_stats_players.assists`, `v_stats_players.flash_assists` | As reported by the game |
| `team_kills` | Teammates killed | `v_stats_players.team_kills` | |
| `kpr` / `kd` | Kills per round; kills per death | `v_stats_players.kpr`, `v_stats_players.kd` | `kd` is `NULL` without deaths |
| `damage` / `adr` | Health damage to enemies, capped by the victim's health; per round | `v_stats_players.damage`, `v_stats_players.adr` | Damage to bots and teammates excluded |
| `utility_damage` | Damage from HE grenades and fire | `v_stats_players.utility_damage`, `v_stats_round_players.utility_damage` | |
| `headshot_kills` / `hs_pct` | Kills with a headshot; share of kills | `v_stats_players.headshot_kills`, `v_stats_players.hs_pct` | |
| `k2`, `k3`, `k4`, `k5` | Rounds with exactly 2, 3, 4 and 5+ kills | `v_stats_players.k2` … `v_stats_players.k5` | |

Special kills per player (query: wallbang, through smoke, no-scope, while blind, in the air):

```sql
SELECT CAST(attacker AS CHAR) AS steam_id,
    SUM(CASE WHEN penetrated > 0 THEN 1 ELSE 0 END) AS wallbang_kills,
    SUM(thrusmoke) AS smoke_kills, SUM(noscope) AS noscope_kills,
    SUM(attacker_blind) AS blind_kills, SUM(attacker_in_air) AS air_kills
FROM v_stats_kills
WHERE attacker IS NOT NULL AND attacker <> victim AND victim_is_bot = 0 AND is_team_kill = 0
GROUP BY attacker;
```

Kill distance in meters from the positions (query; `v_stats_kills.distance` is the game's own value):

```sql
SELECT weapon, COUNT(*) AS kills,
    ROUND(AVG(SQRT((attacker_x - victim_x) * (attacker_x - victim_x) + (attacker_y - victim_y) * (attacker_y - victim_y)
        + (attacker_z - victim_z) * (attacker_z - victim_z))) / 39.37, 1) AS avg_meters
FROM v_stats_kills
WHERE attacker IS NOT NULL AND victim_is_bot = 0 AND is_team_kill = 0 AND attacker_x IS NOT NULL AND victim_x IS NOT NULL
GROUP BY weapon ORDER BY kills DESC;
```

(SQLite needs a build with math functions for `SQRT`.)

## 5. Opening duels and trades

| Stat | Definition | Source | Limits |
|---|---|---|---|
| `entry_kills` / `entry_deaths` | First kill of the round / first victim of the round | `v_stats_players.entry_kills`, `v_stats_players.entry_deaths`; per round `v_stats_round_players.entry_kill`, `v_stats_round_players.entry_death` | Only kills between participants |
| `entry_success_pct` | Entry kills / entry duels | `v_stats_players.entry_success_pct` | |
| `trade_kills` / `traded_deaths` | A kill on A by B is traded when a teammate of A kills B within `TradeWindowSeconds` (5); the avenger gets a trade kill, A a traded death | `v_stats_players.trade_kills`, `v_stats_players.traded_deaths`; per round `v_stats_round_players.trade_kills`, `v_stats_round_players.traded_death` | One revenge trades one death |
| Trades within a duo | Rounds in which the player avenged that teammate within 5 s | `v_stats_duos.trades` | The window is fixed at 5 s in the view, even if `TradeWindowSeconds` differs in `stats.json`. A revenge kill is credited to every teammate that killer killed within the window, so the `trades` of a player's pairs summed can exceed their `trade_kills` |

## 6. Clutches

| Stat | Definition | Source | Limits |
|---|---|---|---|
| `clutch_1_played` … `clutch_5_played` | Rounds where the player was the last alive of their side against 1, 2, 3, 4, 5+ enemies (X taken when the situation appears) | `v_stats_players.clutch_1_played`, `v_stats_players.clutch_2_played`, `v_stats_players.clutch_3_played`, `v_stats_players.clutch_4_played`, `v_stats_players.clutch_5_played` | Bots on the enemy side are not counted in X |
| `clutch_1_won` … `clutch_5_won` | Those clutches won (side won the round) | `v_stats_players.clutch_1_won`, `v_stats_players.clutch_2_won`, `v_stats_players.clutch_3_won`, `v_stats_players.clutch_4_won`, `v_stats_players.clutch_5_won` | |
| `clutch_won_pct` | Clutches won / played | `v_stats_players.clutch_won_pct` | |
| `clutch_vs` / `clutch_won` | Per round: X (0 = no clutch) and whether it was won | `v_stats_round_players.clutch_vs`, `v_stats_round_players.clutch_won` | |
| `clutch_defuses` | Defuses with less than 1 s left on the bomb | `v2_stats_players.clutch_defuses` | 4.9+ only |

Clutch win rate per site (query):

```sql
SELECT site, clutch_vs, COUNT(*) AS clutches, SUM(clutch_won) AS won,
    ROUND(100.0 * SUM(clutch_won) / COUNT(*), 1) AS won_pct
FROM v_stats_round_players
WHERE clutch_vs > 0
GROUP BY site, clutch_vs ORDER BY site, clutch_vs;
```

## 7. Shots and accuracy

| Stat | Definition | Source | Limits |
|---|---|---|---|
| `shots` / `hits` | Bullet shots (knives, grenades, Zeus, C4 excluded); hits = (shooter, tick) pairs that damaged a player, so a shotgun blast counts once | `v_stats_players.shots`, `v_stats_players.hits`, `v_stats_round_players.shots` | |
| `headshot_hits` / `hs_accuracy` | Hits on the head; share of the hits | `v_stats_players.headshot_hits`, `v_stats_players.hs_accuracy` | |
| `accuracy` | Hits / shots | `v_stats_players.accuracy`, `v_stats_player_weapons.accuracy`, `v_stats_weapon_meta.accuracy` | |
| `shots_still` / `hits_still` / `still_accuracy` | Shots on the ground at up to 34 % of the weapon's max speed; their hits and accuracy | `v2_stats_players.shots_still`, `v2_stats_players.hits_still`, `v2_stats_players.still_accuracy`, `v2_stats_round_players.shots_still` | "Still" is not "stopped": a slow walk can count; 4.9+ only |
| `shots_moving` / `hits_moving` / `moving_accuracy` | The other shots (faster, or in the air) | `v2_stats_players.shots_moving`, `v2_stats_players.hits_moving`, `v2_stats_players.moving_accuracy` | Shots with an unreadable pawn are in neither group |
| `shots_scoped` / `hits_scoped` / `scoped_accuracy` | Shots while scoped | `v2_stats_players.shots_scoped`, `v2_stats_players.hits_scoped`, `v2_stats_players.scoped_accuracy` | |
| `still_shot_pct` | Still shots / (still + moving shots) | `v2_stats_players.still_shot_pct` | |
| Shot row | `shot_id`, `round_id`, `steam_id`, `weapon`, `time_ms` (ms since round start), `tick`, position `pos_x`, `pos_y`, `pos_z`, view `pitch`, `yaw` | `v_stats_shots`, `v2_stats_shots` | Raw, purged; at most `MaxShotsPerRound` per round |
| Shot motion | `speed` (horizontal, u/s), `crouched`, `scoped`, `airborne`, `walking`, `still` | `v2_stats_shots.speed`, `v2_stats_shots.crouched`, `v2_stats_shots.scoped`, `v2_stats_shots.airborne`, `v2_stats_shots.walking`, `v2_stats_shots.still` | `NULL` when the pawn could not be read |

## 8. Utility and grenades

| Stat | Definition | Source | Limits |
|---|---|---|---|
| `enemies_flashed` / `teammates_flashed` | Players blinded by the player's flashes (self-flashes ignored) | `v_stats_players.enemies_flashed`, `v_stats_players.teammates_flashed`, `v_stats_round_players.enemies_flashed` | |
| `blind_ms` / `blind_seconds_inflicted` | Blind time inflicted on enemies (total in ms, per round in seconds) | `v_stats_players.blind_ms`, `v_stats_round_players.blind_seconds_inflicted` | |
| Grenade row | `grenade_id`, `type` (`He`, `Flash`, `Smoke`, `Molotov`, `Decoy`), `thrown_ms`, `detonated_ms`, throw position `throw_x`, `throw_y`, `throw_z`, detonation position `det_x`, `det_y`, `det_z`, `damage` to enemies, `enemies_blinded`, `teammates_blinded`, `blind_seconds` | `v_stats_grenades` | `thrown_ms` / `detonated_ms` `NULL` when that half was not seen |

Flash efficiency per player (query):

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, COUNT(*) AS flashes,
    ROUND(1.0 * SUM(enemies_blinded) / COUNT(*), 2) AS enemies_per_flash,
    ROUND(SUM(blind_seconds) / COUNT(*), 2) AS blind_seconds_per_flash
FROM v_stats_grenades WHERE type = 'Flash'
GROUP BY steam_id HAVING COUNT(*) >= 20 ORDER BY enemies_per_flash DESC;
```

## 9. Bomb and objectives

| Stat | Definition | Source | Limits |
|---|---|---|---|
| `plants` / `defuses` / `defuses_with_kit` | Bombs planted and defused by the player (with a kit) | `v_stats_players.plants`, `v_stats_players.defuses`, `v_stats_players.defuses_with_kit`; per round `v_stats_round_players.planted`, `v_stats_round_players.defused`, `v_stats_round_players.defused_with_kit` | An automatic plant (AutoPlant) has a planter only when the game names one |
| `plant_attempts` / `plant_aborts` | Plants started and abandoned | `v2_stats_players.plant_attempts`, `v2_stats_players.plant_aborts` | 4.9+ only |
| `defuse_attempts` / `defuse_aborts` | Defuses started and abandoned | `v2_stats_players.defuse_attempts`, `v2_stats_players.defuse_aborts` | 4.9+ only |
| `bomb_carry_ms` | Time carrying the bomb (freeze time included) | `v2_stats_players.bomb_carry_ms`, `v2_stats_round_players.bomb_carry_ms` | 4.9+ only |
| Round bomb facts | `planted`, `plant_site`, `planter`, `plant_time_ms`, `defused`, `defuser`, `defused_with_kit`, `bomb_exploded` | `v_stats_rounds.plant_site`, `v_stats_rounds.planter`, `v_stats_rounds.plant_time_ms`, `v_stats_rounds.defuser`, `v_stats_rounds.bomb_exploded` | `plant_time_ms` in ms since the round start |

Average plant time per map and site (query):

```sql
SELECT map_name, plant_site, COUNT(*) AS plants, ROUND(AVG(plant_time_ms) / 1000.0, 1) AS avg_plant_seconds
FROM v_stats_rounds WHERE planted = 1 AND plant_time_ms IS NOT NULL
GROUP BY map_name, plant_site ORDER BY map_name, plant_site;
```

## 10. Player events

| Stat | Definition | Source | Limits |
|---|---|---|---|
| Event row | `event_id`, `round_id`, `steam_id`, `type`, `time_ms`, `value`, position `pos_x`, `pos_y`, `pos_z` | `v_stats_player_events` | Raw, purged; at most `MaxShotsPerRound` events per round |
| Event types | `reload`, `reload_full_clip`, `pickup`, `drop`, `jump`, `plant_begin`, `plant_abort`, `defuse_begin`, `defuse_abort`, `bomb_pickup`, `bomb_drop`; `value` = weapon (pickup, drop), `weapon:clip/max` (reloads), `kit` (defuse with a kit), `start` (bomb carrier at the round start) | `v_stats_player_events.type`, `v_stats_player_events.value` | |
| `reloads` / `reloads_full` | Reloads; those started with at least half of the magazine left | `v2_stats_players.reloads`, `v2_stats_players.reloads_full` | CS2 never reloads a full magazine |
| `pickups` / `drops` | Weapons picked up and dropped during the live phase | `v2_stats_players.pickups`, `v2_stats_players.drops` | Grenades and the bomb excluded; spawn equipment and drops from death are stored but not counted |
| `jumps` | Jump key pressed on the ground | `v2_stats_players.jumps` | Read from the held buttons |

Most picked-up weapons (query):

```sql
SELECT value AS weapon, COUNT(*) AS pickups
FROM v_stats_player_events WHERE type = 'pickup'
GROUP BY value ORDER BY pickups DESC LIMIT 20;
```

Share of "early" reloads per player (query):

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, name, reloads, reloads_full,
    ROUND(100.0 * reloads_full / NULLIF(reloads, 0), 1) AS early_reload_pct
FROM v2_stats_players WHERE reloads >= 50 ORDER BY early_reload_pct DESC;
```

## 11. Movement and behaviour

Sampled in memory 8 times per second (default) from the freeze end to the round end; only the results are stored. All movement columns are `NULL` (rounds) or 0 (totals) for rounds without movement data.

| Stat | Definition | Source | Limits |
|---|---|---|---|
| `alive_ms` | Time alive in the live phase | `v2_stats_players.alive_ms`, `v2_stats_round_players.alive_ms` | |
| State shares | Exclusive states: in the air, else crouched, else still (< 10 u/s), else walking, else running: `walk_pct`, `run_pct`, `crouch_pct`, `still_pct`, `air_pct` (of the alive samples); sample counts `walk_samples`, `run_samples`, `crouch_samples`, `still_samples`, `air_samples`, `alive_samples` | `v2_stats_players.walk_pct` …, `v2_stats_round_players.walk_pct` …, `v2_stats_players.alive_samples` | The five shares add up to 100 |
| Distance | Horizontal distance between samples: `distance_cm` (total), `distance_m` (round), `distance_m_per_min` (per minute alive) | `v2_stats_players.distance_cm`, `v2_stats_round_players.distance_m`, `v2_stats_players.distance_m_per_min` | 1 m = 39.37 u |
| AFK | `afk_ms`: periods of 15 s+ within 2 m and 5° without firing; `afk_pct` of `alive_ms` | `v2_stats_players.afk_ms`, `v2_stats_players.afk_pct`, `v2_stats_round_players.afk_ms` | |
| Smokes | `in_smoke_pct`: eyes inside an active smoke; `behind_smoke_pct`: a smoke on the line to the nearest living enemy human; counts `in_smoke_samples`, `behind_smoke_samples` | `v2_stats_players.in_smoke_pct`, `v2_stats_players.behind_smoke_pct`, `v2_stats_players.in_smoke_samples`, `v2_stats_players.behind_smoke_samples`, `v_stats_player_behaviour.in_smoke_pct`, `v_stats_player_behaviour.behind_smoke_pct` | No map geometry: "behind" means a smoke is on the line, wall or not; 0 against bots |
| First contact | `first_contact_ms`: freeze end → first bullet shot or damage with an enemy human; totals `first_contact_ms_sum`, `first_contact_rounds`; `avg_first_contact_ms` | `v2_stats_round_players.first_contact_ms`, `v2_stats_players.first_contact_ms_sum`, `v2_stats_players.first_contact_rounds`, `v2_stats_players.avg_first_contact_ms`, `v_stats_player_behaviour.avg_first_contact_ms` | `NULL` without a contact |
| Engage order | `engage_order`: rank of the first contact in the side (1 = first, ties share); `engage_order_sum`, `avg_engage_order` | `v2_stats_round_players.engage_order`, `v2_stats_players.engage_order_sum`, `v2_stats_players.avg_engage_order`, `v_stats_player_behaviour.avg_engage_order` | |
| Bait | `bait` (round: `NULL` without a chance, else 0/1); `bait_chances` and `bait_rounds` (rounds with a chance / with a bait); `bait_rate` = bait_rounds / bait_chances | `v2_stats_round_players.bait`, `v2_stats_players.bait_chances`, `v2_stats_players.bait_rounds`, `v2_stats_players.bait_rate`, `v_stats_player_behaviour.bait_chances`, `v_stats_player_behaviour.bait_rounds`, `v_stats_player_behaviour.bait_rate` | Teammate killed within 10 m, killer and player alive for 5 s, no answer |
| Slow player | `contact_rounds` (rounds with a contact), `compared_rounds` (rounds where a teammate also made contact), `own_first_contact_ms`, `team_first_contact_ms`, `slowness_index` = own / team (above 1 = slower) | `v_stats_player_behaviour.contact_rounds`, `v_stats_player_behaviour.compared_rounds`, `v_stats_player_behaviour.own_first_contact_ms`, `v_stats_player_behaviour.team_first_contact_ms`, `v_stats_player_behaviour.slowness_index` | Starting thresholds: slow when ≥ 1.3 over 30+ `compared_rounds` |

Per-round average of a movement counter must only count rounds with data (query):

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, COUNT(*) AS rounds_with_data, ROUND(AVG(distance_m), 1) AS avg_distance_m
FROM v2_stats_round_players WHERE alive_ms IS NOT NULL
GROUP BY steam_id;
```

## 12. Kill and damage details

| Stat | Definition | Source | Limits |
|---|---|---|---|
| Kill row | `kill_id`, `round_id`, `attacker`, `victim`, `victim_is_bot`, `assister`, `flash_assist`, `weapon`, `headshot`, `penetrated` (walls crossed), `noscope`, `thrusmoke`, `attacker_blind`, `attacker_in_air`, `distance`, `time_ms`, `is_team_kill` | `v_stats_kills` | Bot kill: `attacker` `NULL`; bot victim: `victim` 0 and `victim_is_bot` 1; a duplicated death event appears twice |
| Kill positions | `attacker_x`, `attacker_y`, `attacker_z`, `victim_x`, `victim_y`, `victim_z` (world units) | `v_stats_kills.victim_x` … | `NULL` when unknown |
| Damage row | `damage_id`, `attacker`, `victim`, `hitgroup` (0 generic, 1 head, 2 chest, 3 stomach, 4–5 arms, 6–7 legs), `dmg_health`, `dmg_armor`, `health_after`, `tick` | `v_stats_damages` | Raw, purged |

Damage per body part for a player (query, `?` = the player's SteamID64 as a string):

```sql
SELECT hitgroup, COUNT(*) AS hits, SUM(dmg_health) AS damage
FROM v_stats_damages
WHERE attacker = CAST(? AS UNSIGNED) AND victim_is_bot = 0
GROUP BY hitgroup ORDER BY hitgroup;
```

## 13. Weapons

| Stat | Definition | Source | Limits |
|---|---|---|---|
| Per player | `weapon` (name without `weapon_`), `shots`, `hits`, `headshot_hits`, `kills`, `headshot_kills`, `damage`, `accuracy`, `hs_pct` | `v_stats_player_weapons` | |
| `primary_weapon` | Primary weapon given at the round start | `v_stats_round_players.primary_weapon` | `NULL` for pistol-only loadouts |
| Weapon meta | Per weapon over all players: `players` (players with a row for it), totals, `accuracy`, `hs_accuracy`, `hs_pct`, `kill_share_pct` (its kills / all weapon kills), `primary_rounds`, `primary_players`, `primary_won_rounds`, `primary_win_pct` (rounds where it was the primary weapon) | `v_stats_weapon_meta.players`, `v_stats_weapon_meta.kill_share_pct`, `v_stats_weapon_meta.primary_rounds`, `v_stats_weapon_meta.primary_players`, `v_stats_weapon_meta.primary_won_rounds`, `v_stats_weapon_meta.primary_win_pct` | Whole history; a weapon only given as primary and never fired has zero totals |

## 14. Rounds, maps and scopes

| Stat | Definition | Source | Limits |
|---|---|---|---|
| Map | `map_id`, `server` (`ServerKey`), `map_name`, `started_at`, `ended_at` | `v_stats_maps` | `ended_at` `NULL` for the current map or a crash |
| Round | `round_id`, `round_number`, `site` (`A`/`B`), `round_type`, `players_ct`, `players_t` (humans at the round start), `winner` (`CT`/`T`), `end_reason` (raw CS2 reason), `duration_ms`, `started_at` | `v_stats_rounds` | Bots are not in `players_ct` / `players_t` |
| `side` | Side of the player in the round (`CT`/`T`) | `v_stats_round_players.side` | |
| `scope` | Total slices: `all`, `map:<map>`, `site:A`, `site:B`, `roundtype:<type>`, `side:CT`, `side:T` | `v_stats_player_scopes.scope`, `v2_stats_player_scopes.scope` | |

## 15. Retake analysis (derived views)

The derived views aggregate whole tables at each query: cache their results on the site side. `v_stats_rivals` and `v_stats_duos` scan the whole kills table every time and are the heaviest.

| Stat | Definition | Source | Limits |
|---|---|---|---|
| Retake odds | Rounds grouped by `server`, `map_name`, `site`, `round_type`, `ranked`, start counts `players_ct` / `players_t` and the situation at the plant `ct_alive_at_plant` / `t_alive_at_plant` (participants not yet dead when the bomb was planted; `NULL` without a plant): `rounds`, `ct_wins`, `t_wins`, `ct_win_pct`, `defused_rounds`, `exploded_rounds`, `avg_duration_ms` | `v_stats_retake_odds.ct_alive_at_plant`, `v_stats_retake_odds.t_alive_at_plant`, `v_stats_retake_odds.ct_wins`, `v_stats_retake_odds.t_wins`, `v_stats_retake_odds.ct_win_pct`, `v_stats_retake_odds.defused_rounds`, `v_stats_retake_odds.exploded_rounds`, `v_stats_retake_odds.avg_duration_ms` | Humans only (bots and players who left are not subtracted); to merge rows, `SUM(rounds)` and `SUM(ct_wins)` then divide, never average `ct_win_pct`. The alive-at-plant counts can be off, even negative, when a human joins after the round start; deaths without a round-player row are ignored; team kills, suicides and world deaths before the plant count as deaths |
| Side balance | Per `server`, `map_name`, `site`: `rounds`, `ct_wins`, `t_wins`, `ct_win_pct`, `t_win_pct`, `ranked_rounds`, `ranked_ct_wins`, `ranked_ct_win_pct`, `avg_duration_ms`, `first_round_at`, `last_round_at` | `v_stats_side_balance.rounds`, `v_stats_side_balance.ct_wins`, `v_stats_side_balance.t_wins`, `v_stats_side_balance.ct_win_pct`, `v_stats_side_balance.t_win_pct`, `v_stats_side_balance.ranked_ct_wins`, `v_stats_side_balance.ranked_ct_win_pct`, `v_stats_side_balance.avg_duration_ms`, `v_stats_side_balance.first_round_at`, `v_stats_side_balance.last_round_at` | Whole history: filter on dates with `v_stats_rounds` for a period |
| Rivals | One row per player and enemy human (both directions): `opponent_id`, `opponent_name`, `kills` (player killed the enemy), `deaths` (enemy killed the player), `headshot_kills`, `headshot_deaths`, `kd` (`NULL` when `deaths` is 0) | `v_stats_rivals.steam_id`, `v_stats_rivals.name`, `v_stats_rivals.opponent_id`, `v_stats_rivals.opponent_name`, `v_stats_rivals.kills`, `v_stats_rivals.deaths`, `v_stats_rivals.headshot_kills`, `v_stats_rivals.headshot_deaths`, `v_stats_rivals.kd` | First death row per victim only (as the totals); team kills, suicides, the world and bots excluded |
| Duos | One row per player and teammate (both directions): `mate_id`, `mate_name`, `rounds` together on the same side, `rounds_won`, `win_pct`, `trades` (rounds in which the player avenged that teammate within 5 s) | `v_stats_duos.steam_id`, `v_stats_duos.name`, `v_stats_duos.mate_id`, `v_stats_duos.mate_name`, `v_stats_duos.rounds`, `v_stats_duos.rounds_won`, `v_stats_duos.win_pct`, `v_stats_duos.trades` | The reverse row gives the teammate's trades. The 5 s window is fixed in the view (not `TradeWindowSeconds`); one revenge kill is credited to every teammate that killer killed within the window, so per-pair trades summed can exceed a player's `trade_kills` |

Win rate by man advantage (query: collapse the plant situation; `?` = map, site):

```sql
SELECT players_ct, players_t, SUM(rounds) AS rounds, SUM(ct_wins) AS ct_wins,
    ROUND(100.0 * SUM(ct_wins) / SUM(rounds), 1) AS ct_win_pct
FROM v_stats_retake_odds
WHERE map_name = ? AND site = ? AND ranked = 1
GROUP BY players_ct, players_t ORDER BY players_ct, players_t;
```

A player's nemesis (query, `?` = the player's SteamID64 as a string):

```sql
SELECT CAST(opponent_id AS CHAR) AS opponent_id, opponent_name, deaths, kills
FROM v_stats_rivals WHERE steam_id = CAST(? AS UNSIGNED)
ORDER BY deaths DESC LIMIT 5;
```

## 16. Server

| Stat | Definition | Source | Limits |
|---|---|---|---|
| Presence | Snapshot every `PresenceSnapshotSeconds` (60): `snapshot_id`, `at`, `map_name`, `humans`, `bots`, `queued` | `v_stats_presence` | Raw, purged |
| Server events | `event_id`, `at`, `type`, `module`, `value`, `occurrences`, `max_ms`; types `plugin_start`, `hot_reload`, `module_error`, `module_disabled`, `slow_handler`, `long_frame`, `uptime`, `map_vote`, `map_vote_result`, `command`, `chat` | `v_stats_server_health.module`, `v_stats_server_health.occurrences`, `v_stats_server_health.max_ms` | Raw, purged; repeated types are aggregated per minute; commands and chat are counts by name only |
| Activity | Per `server`, UTC `day` (`yyyy-MM-dd`) and `hour` (0–23): `snapshots`, `humans_avg`, `humans_max`, `bots_avg`, `queued_max` (from presence; `NULL` with the averages once presence is purged), `rounds`, `ranked_rounds`, `players` (distinct participants of the rounds started that hour), `sessions` (sessions opened that hour) | `v_stats_server_activity.server`, `v_stats_server_activity.day`, `v_stats_server_activity.hour`, `v_stats_server_activity.snapshots`, `v_stats_server_activity.humans_avg`, `v_stats_server_activity.humans_max`, `v_stats_server_activity.bots_avg`, `v_stats_server_activity.queued_max`, `v_stats_server_activity.rounds`, `v_stats_server_activity.players` | UTC hours; once presence is purged, old hours keep rounds and sessions with `snapshots` 0 and `NULL` averages |

Map vote popularity (query):

```sql
SELECT value AS map_name, SUM(occurrences) AS votes
FROM v_stats_server_health WHERE type = 'map_vote'
GROUP BY value ORDER BY votes DESC;
```

Errors per module and day (query):

```sql
SELECT DATE(at) AS day, module, SUM(occurrences) AS errors
FROM v_stats_server_health WHERE type IN ('module_error', 'module_disabled')
GROUP BY DATE(at), module ORDER BY day DESC, errors DESC;
```

## Column reference

Every column of every view, in contract order (`view.column`). Definitions are in the sections above and in [STATS.md](STATS.md).

**Contract v1**

- `v_stats_players`: `v_stats_players.steam_id`, `v_stats_players.name`, `v_stats_players.first_seen`, `v_stats_players.last_seen`, `v_stats_players.elo`, `v_stats_players.seconds_ct`, `v_stats_players.seconds_t`, `v_stats_players.seconds_spec`, `v_stats_players.seconds_queue`, `v_stats_players.seconds_total`, `v_stats_players.sessions`, `v_stats_players.rounds`, `v_stats_players.rounds_won`, `v_stats_players.ranked_rounds`, `v_stats_players.survived_rounds`, `v_stats_players.kills`, `v_stats_players.deaths`, `v_stats_players.assists`, `v_stats_players.flash_assists`, `v_stats_players.team_kills`, `v_stats_players.damage`, `v_stats_players.utility_damage`, `v_stats_players.headshot_kills`, `v_stats_players.shots`, `v_stats_players.hits`, `v_stats_players.headshot_hits`, `v_stats_players.entry_kills`, `v_stats_players.entry_deaths`, `v_stats_players.trade_kills`, `v_stats_players.traded_deaths`, `v_stats_players.kast_rounds`, `v_stats_players.k2`, `v_stats_players.k3`, `v_stats_players.k4`, `v_stats_players.k5`, `v_stats_players.clutch_1_played`, `v_stats_players.clutch_1_won`, `v_stats_players.clutch_2_played`, `v_stats_players.clutch_2_won`, `v_stats_players.clutch_3_played`, `v_stats_players.clutch_3_won`, `v_stats_players.clutch_4_played`, `v_stats_players.clutch_4_won`, `v_stats_players.clutch_5_played`, `v_stats_players.clutch_5_won`, `v_stats_players.enemies_flashed`, `v_stats_players.teammates_flashed`, `v_stats_players.blind_ms`, `v_stats_players.mvps`, `v_stats_players.plants`, `v_stats_players.defuses`, `v_stats_players.defuses_with_kit`, `v_stats_players.rating_milli`, `v_stats_players.kpr`, `v_stats_players.kd`, `v_stats_players.adr`, `v_stats_players.hs_pct`, `v_stats_players.accuracy`, `v_stats_players.hs_accuracy`, `v_stats_players.kast_pct`, `v_stats_players.survival_pct`, `v_stats_players.win_pct`, `v_stats_players.rating`, `v_stats_players.entry_success_pct`, `v_stats_players.clutch_won_pct`
- `v_stats_player_scopes`: `v_stats_player_scopes.steam_id`, `v_stats_player_scopes.name`, `v_stats_player_scopes.scope`, `v_stats_player_scopes.rounds`, `v_stats_player_scopes.rounds_won`, `v_stats_player_scopes.ranked_rounds`, `v_stats_player_scopes.survived_rounds`, `v_stats_player_scopes.kills`, `v_stats_player_scopes.deaths`, `v_stats_player_scopes.assists`, `v_stats_player_scopes.flash_assists`, `v_stats_player_scopes.team_kills`, `v_stats_player_scopes.damage`, `v_stats_player_scopes.utility_damage`, `v_stats_player_scopes.headshot_kills`, `v_stats_player_scopes.shots`, `v_stats_player_scopes.hits`, `v_stats_player_scopes.headshot_hits`, `v_stats_player_scopes.entry_kills`, `v_stats_player_scopes.entry_deaths`, `v_stats_player_scopes.trade_kills`, `v_stats_player_scopes.traded_deaths`, `v_stats_player_scopes.kast_rounds`, `v_stats_player_scopes.k2`, `v_stats_player_scopes.k3`, `v_stats_player_scopes.k4`, `v_stats_player_scopes.k5`, `v_stats_player_scopes.clutch_1_played`, `v_stats_player_scopes.clutch_1_won`, `v_stats_player_scopes.clutch_2_played`, `v_stats_player_scopes.clutch_2_won`, `v_stats_player_scopes.clutch_3_played`, `v_stats_player_scopes.clutch_3_won`, `v_stats_player_scopes.clutch_4_played`, `v_stats_player_scopes.clutch_4_won`, `v_stats_player_scopes.clutch_5_played`, `v_stats_player_scopes.clutch_5_won`, `v_stats_player_scopes.enemies_flashed`, `v_stats_player_scopes.teammates_flashed`, `v_stats_player_scopes.blind_ms`, `v_stats_player_scopes.mvps`, `v_stats_player_scopes.plants`, `v_stats_player_scopes.defuses`, `v_stats_player_scopes.defuses_with_kit`, `v_stats_player_scopes.rating_milli`, `v_stats_player_scopes.kpr`, `v_stats_player_scopes.kd`, `v_stats_player_scopes.adr`, `v_stats_player_scopes.hs_pct`, `v_stats_player_scopes.accuracy`, `v_stats_player_scopes.hs_accuracy`, `v_stats_player_scopes.kast_pct`, `v_stats_player_scopes.survival_pct`, `v_stats_player_scopes.win_pct`, `v_stats_player_scopes.rating`, `v_stats_player_scopes.entry_success_pct`, `v_stats_player_scopes.clutch_won_pct`
- `v_stats_player_weapons`: `v_stats_player_weapons.steam_id`, `v_stats_player_weapons.weapon`, `v_stats_player_weapons.shots`, `v_stats_player_weapons.hits`, `v_stats_player_weapons.headshot_hits`, `v_stats_player_weapons.kills`, `v_stats_player_weapons.headshot_kills`, `v_stats_player_weapons.damage`, `v_stats_player_weapons.accuracy`, `v_stats_player_weapons.hs_pct`
- `v_stats_leaderboard`: `v_stats_leaderboard.steam_id`, `v_stats_leaderboard.name`, `v_stats_leaderboard.elo`, `v_stats_leaderboard.ranked_rounds`, `v_stats_leaderboard.rounds`, `v_stats_leaderboard.kills`, `v_stats_leaderboard.deaths`, `v_stats_leaderboard.damage`, `v_stats_leaderboard.kpr`, `v_stats_leaderboard.kd`, `v_stats_leaderboard.adr`, `v_stats_leaderboard.hs_pct`, `v_stats_leaderboard.accuracy`, `v_stats_leaderboard.kast_pct`, `v_stats_leaderboard.win_pct`, `v_stats_leaderboard.rating`
- `v_stats_maps`: `v_stats_maps.map_id`, `v_stats_maps.server`, `v_stats_maps.map_name`, `v_stats_maps.started_at`, `v_stats_maps.ended_at`
- `v_stats_rounds`: `v_stats_rounds.round_id`, `v_stats_rounds.server`, `v_stats_rounds.map_name`, `v_stats_rounds.round_number`, `v_stats_rounds.site`, `v_stats_rounds.round_type`, `v_stats_rounds.players_ct`, `v_stats_rounds.players_t`, `v_stats_rounds.ranked`, `v_stats_rounds.winner`, `v_stats_rounds.end_reason`, `v_stats_rounds.duration_ms`, `v_stats_rounds.planted`, `v_stats_rounds.plant_site`, `v_stats_rounds.planter`, `v_stats_rounds.plant_time_ms`, `v_stats_rounds.defused`, `v_stats_rounds.defuser`, `v_stats_rounds.defused_with_kit`, `v_stats_rounds.bomb_exploded`, `v_stats_rounds.started_at`
- `v_stats_round_players`: `v_stats_round_players.round_id`, `v_stats_round_players.server`, `v_stats_round_players.map_name`, `v_stats_round_players.site`, `v_stats_round_players.round_type`, `v_stats_round_players.started_at`, `v_stats_round_players.steam_id`, `v_stats_round_players.side`, `v_stats_round_players.primary_weapon`, `v_stats_round_players.survived`, `v_stats_round_players.won`, `v_stats_round_players.kills`, `v_stats_round_players.deaths`, `v_stats_round_players.assists`, `v_stats_round_players.flash_assists`, `v_stats_round_players.team_kills`, `v_stats_round_players.damage`, `v_stats_round_players.utility_damage`, `v_stats_round_players.headshot_kills`, `v_stats_round_players.shots`, `v_stats_round_players.hits`, `v_stats_round_players.headshot_hits`, `v_stats_round_players.entry_kill`, `v_stats_round_players.entry_death`, `v_stats_round_players.trade_kills`, `v_stats_round_players.traded_death`, `v_stats_round_players.kast`, `v_stats_round_players.clutch_vs`, `v_stats_round_players.clutch_won`, `v_stats_round_players.enemies_flashed`, `v_stats_round_players.teammates_flashed`, `v_stats_round_players.blind_seconds_inflicted`, `v_stats_round_players.mvp`, `v_stats_round_players.planted`, `v_stats_round_players.defused`, `v_stats_round_players.defused_with_kit`, `v_stats_round_players.rating`, `v_stats_round_players.elo_before`, `v_stats_round_players.elo_after`
- `v_stats_kills`: `v_stats_kills.kill_id`, `v_stats_kills.round_id`, `v_stats_kills.map_name`, `v_stats_kills.site`, `v_stats_kills.round_type`, `v_stats_kills.started_at`, `v_stats_kills.attacker`, `v_stats_kills.victim`, `v_stats_kills.victim_is_bot`, `v_stats_kills.assister`, `v_stats_kills.flash_assist`, `v_stats_kills.weapon`, `v_stats_kills.headshot`, `v_stats_kills.penetrated`, `v_stats_kills.noscope`, `v_stats_kills.thrusmoke`, `v_stats_kills.attacker_blind`, `v_stats_kills.attacker_in_air`, `v_stats_kills.distance`, `v_stats_kills.attacker_x`, `v_stats_kills.attacker_y`, `v_stats_kills.attacker_z`, `v_stats_kills.victim_x`, `v_stats_kills.victim_y`, `v_stats_kills.victim_z`, `v_stats_kills.time_ms`, `v_stats_kills.is_team_kill`
- `v_stats_shots`: `v_stats_shots.shot_id`, `v_stats_shots.round_id`, `v_stats_shots.steam_id`, `v_stats_shots.weapon`, `v_stats_shots.time_ms`, `v_stats_shots.tick`, `v_stats_shots.pos_x`, `v_stats_shots.pos_y`, `v_stats_shots.pos_z`, `v_stats_shots.pitch`, `v_stats_shots.yaw`
- `v_stats_damages`: `v_stats_damages.damage_id`, `v_stats_damages.round_id`, `v_stats_damages.attacker`, `v_stats_damages.victim`, `v_stats_damages.victim_is_bot`, `v_stats_damages.weapon`, `v_stats_damages.hitgroup`, `v_stats_damages.dmg_health`, `v_stats_damages.dmg_armor`, `v_stats_damages.health_after`, `v_stats_damages.time_ms`, `v_stats_damages.tick`
- `v_stats_grenades`: `v_stats_grenades.grenade_id`, `v_stats_grenades.round_id`, `v_stats_grenades.steam_id`, `v_stats_grenades.type`, `v_stats_grenades.thrown_ms`, `v_stats_grenades.detonated_ms`, `v_stats_grenades.throw_x`, `v_stats_grenades.throw_y`, `v_stats_grenades.throw_z`, `v_stats_grenades.det_x`, `v_stats_grenades.det_y`, `v_stats_grenades.det_z`, `v_stats_grenades.damage`, `v_stats_grenades.enemies_blinded`, `v_stats_grenades.teammates_blinded`, `v_stats_grenades.blind_seconds`
- `v_stats_sessions`: `v_stats_sessions.session_id`, `v_stats_sessions.steam_id`, `v_stats_sessions.server`, `v_stats_sessions.map_name`, `v_stats_sessions.connected_at`, `v_stats_sessions.disconnected_at`, `v_stats_sessions.disconnect_reason`, `v_stats_sessions.seconds_ct`, `v_stats_sessions.seconds_t`, `v_stats_sessions.seconds_spec`, `v_stats_sessions.seconds_queue`
- `v_stats_elo_history`: `v_stats_elo_history.steam_id`, `v_stats_elo_history.day`, `v_stats_elo_history.elo`, `v_stats_elo_history.rounds`, `v_stats_elo_history.delta_sum`

**Contract v2**

- `v2_stats_players`: `v2_stats_players.steam_id`, `v2_stats_players.name`, `v2_stats_players.country`, `v2_stats_players.first_seen`, `v2_stats_players.last_seen`, `v2_stats_players.elo`, `v2_stats_players.seconds_ct`, `v2_stats_players.seconds_t`, `v2_stats_players.seconds_spec`, `v2_stats_players.seconds_queue`, `v2_stats_players.seconds_total`, `v2_stats_players.sessions`, `v2_stats_players.rounds`, `v2_stats_players.rounds_won`, `v2_stats_players.ranked_rounds`, `v2_stats_players.survived_rounds`, `v2_stats_players.kills`, `v2_stats_players.deaths`, `v2_stats_players.assists`, `v2_stats_players.flash_assists`, `v2_stats_players.team_kills`, `v2_stats_players.damage`, `v2_stats_players.utility_damage`, `v2_stats_players.headshot_kills`, `v2_stats_players.shots`, `v2_stats_players.hits`, `v2_stats_players.headshot_hits`, `v2_stats_players.entry_kills`, `v2_stats_players.entry_deaths`, `v2_stats_players.trade_kills`, `v2_stats_players.traded_deaths`, `v2_stats_players.kast_rounds`, `v2_stats_players.k2`, `v2_stats_players.k3`, `v2_stats_players.k4`, `v2_stats_players.k5`, `v2_stats_players.clutch_1_played`, `v2_stats_players.clutch_1_won`, `v2_stats_players.clutch_2_played`, `v2_stats_players.clutch_2_won`, `v2_stats_players.clutch_3_played`, `v2_stats_players.clutch_3_won`, `v2_stats_players.clutch_4_played`, `v2_stats_players.clutch_4_won`, `v2_stats_players.clutch_5_played`, `v2_stats_players.clutch_5_won`, `v2_stats_players.enemies_flashed`, `v2_stats_players.teammates_flashed`, `v2_stats_players.blind_ms`, `v2_stats_players.mvps`, `v2_stats_players.plants`, `v2_stats_players.defuses`, `v2_stats_players.defuses_with_kit`, `v2_stats_players.rating_milli`, `v2_stats_players.shots_still`, `v2_stats_players.hits_still`, `v2_stats_players.shots_moving`, `v2_stats_players.hits_moving`, `v2_stats_players.shots_scoped`, `v2_stats_players.hits_scoped`, `v2_stats_players.reloads`, `v2_stats_players.reloads_full`, `v2_stats_players.pickups`, `v2_stats_players.drops`, `v2_stats_players.jumps`, `v2_stats_players.plant_attempts`, `v2_stats_players.plant_aborts`, `v2_stats_players.defuse_attempts`, `v2_stats_players.defuse_aborts`, `v2_stats_players.clutch_defuses`, `v2_stats_players.damage_taken`, `v2_stats_players.bomb_carry_ms`, `v2_stats_players.alive_ms`, `v2_stats_players.walk_samples`, `v2_stats_players.run_samples`, `v2_stats_players.crouch_samples`, `v2_stats_players.still_samples`, `v2_stats_players.air_samples`, `v2_stats_players.alive_samples`, `v2_stats_players.distance_cm`, `v2_stats_players.afk_ms`, `v2_stats_players.in_smoke_samples`, `v2_stats_players.behind_smoke_samples`, `v2_stats_players.first_contact_ms_sum`, `v2_stats_players.first_contact_rounds`, `v2_stats_players.engage_order_sum`, `v2_stats_players.bait_rounds`, `v2_stats_players.bait_chances`, `v2_stats_players.kpr`, `v2_stats_players.kd`, `v2_stats_players.adr`, `v2_stats_players.hs_pct`, `v2_stats_players.accuracy`, `v2_stats_players.hs_accuracy`, `v2_stats_players.kast_pct`, `v2_stats_players.survival_pct`, `v2_stats_players.win_pct`, `v2_stats_players.rating`, `v2_stats_players.entry_success_pct`, `v2_stats_players.clutch_won_pct`, `v2_stats_players.still_accuracy`, `v2_stats_players.moving_accuracy`, `v2_stats_players.scoped_accuracy`, `v2_stats_players.still_shot_pct`, `v2_stats_players.dtr`, `v2_stats_players.walk_pct`, `v2_stats_players.run_pct`, `v2_stats_players.crouch_pct`, `v2_stats_players.still_pct`, `v2_stats_players.air_pct`, `v2_stats_players.in_smoke_pct`, `v2_stats_players.behind_smoke_pct`, `v2_stats_players.afk_pct`, `v2_stats_players.distance_m_per_min`, `v2_stats_players.avg_first_contact_ms`, `v2_stats_players.avg_engage_order`, `v2_stats_players.bait_rate`
- `v2_stats_player_scopes`: `v2_stats_player_scopes.steam_id`, `v2_stats_player_scopes.name`, `v2_stats_player_scopes.scope`, `v2_stats_player_scopes.rounds`, `v2_stats_player_scopes.rounds_won`, `v2_stats_player_scopes.ranked_rounds`, `v2_stats_player_scopes.survived_rounds`, `v2_stats_player_scopes.kills`, `v2_stats_player_scopes.deaths`, `v2_stats_player_scopes.assists`, `v2_stats_player_scopes.flash_assists`, `v2_stats_player_scopes.team_kills`, `v2_stats_player_scopes.damage`, `v2_stats_player_scopes.utility_damage`, `v2_stats_player_scopes.headshot_kills`, `v2_stats_player_scopes.shots`, `v2_stats_player_scopes.hits`, `v2_stats_player_scopes.headshot_hits`, `v2_stats_player_scopes.entry_kills`, `v2_stats_player_scopes.entry_deaths`, `v2_stats_player_scopes.trade_kills`, `v2_stats_player_scopes.traded_deaths`, `v2_stats_player_scopes.kast_rounds`, `v2_stats_player_scopes.k2`, `v2_stats_player_scopes.k3`, `v2_stats_player_scopes.k4`, `v2_stats_player_scopes.k5`, `v2_stats_player_scopes.clutch_1_played`, `v2_stats_player_scopes.clutch_1_won`, `v2_stats_player_scopes.clutch_2_played`, `v2_stats_player_scopes.clutch_2_won`, `v2_stats_player_scopes.clutch_3_played`, `v2_stats_player_scopes.clutch_3_won`, `v2_stats_player_scopes.clutch_4_played`, `v2_stats_player_scopes.clutch_4_won`, `v2_stats_player_scopes.clutch_5_played`, `v2_stats_player_scopes.clutch_5_won`, `v2_stats_player_scopes.enemies_flashed`, `v2_stats_player_scopes.teammates_flashed`, `v2_stats_player_scopes.blind_ms`, `v2_stats_player_scopes.mvps`, `v2_stats_player_scopes.plants`, `v2_stats_player_scopes.defuses`, `v2_stats_player_scopes.defuses_with_kit`, `v2_stats_player_scopes.rating_milli`, `v2_stats_player_scopes.shots_still`, `v2_stats_player_scopes.hits_still`, `v2_stats_player_scopes.shots_moving`, `v2_stats_player_scopes.hits_moving`, `v2_stats_player_scopes.shots_scoped`, `v2_stats_player_scopes.hits_scoped`, `v2_stats_player_scopes.reloads`, `v2_stats_player_scopes.reloads_full`, `v2_stats_player_scopes.pickups`, `v2_stats_player_scopes.drops`, `v2_stats_player_scopes.jumps`, `v2_stats_player_scopes.plant_attempts`, `v2_stats_player_scopes.plant_aborts`, `v2_stats_player_scopes.defuse_attempts`, `v2_stats_player_scopes.defuse_aborts`, `v2_stats_player_scopes.clutch_defuses`, `v2_stats_player_scopes.damage_taken`, `v2_stats_player_scopes.bomb_carry_ms`, `v2_stats_player_scopes.alive_ms`, `v2_stats_player_scopes.walk_samples`, `v2_stats_player_scopes.run_samples`, `v2_stats_player_scopes.crouch_samples`, `v2_stats_player_scopes.still_samples`, `v2_stats_player_scopes.air_samples`, `v2_stats_player_scopes.alive_samples`, `v2_stats_player_scopes.distance_cm`, `v2_stats_player_scopes.afk_ms`, `v2_stats_player_scopes.in_smoke_samples`, `v2_stats_player_scopes.behind_smoke_samples`, `v2_stats_player_scopes.first_contact_ms_sum`, `v2_stats_player_scopes.first_contact_rounds`, `v2_stats_player_scopes.engage_order_sum`, `v2_stats_player_scopes.bait_rounds`, `v2_stats_player_scopes.bait_chances`, `v2_stats_player_scopes.kpr`, `v2_stats_player_scopes.kd`, `v2_stats_player_scopes.adr`, `v2_stats_player_scopes.hs_pct`, `v2_stats_player_scopes.accuracy`, `v2_stats_player_scopes.hs_accuracy`, `v2_stats_player_scopes.kast_pct`, `v2_stats_player_scopes.survival_pct`, `v2_stats_player_scopes.win_pct`, `v2_stats_player_scopes.rating`, `v2_stats_player_scopes.entry_success_pct`, `v2_stats_player_scopes.clutch_won_pct`, `v2_stats_player_scopes.still_accuracy`, `v2_stats_player_scopes.moving_accuracy`, `v2_stats_player_scopes.scoped_accuracy`, `v2_stats_player_scopes.still_shot_pct`, `v2_stats_player_scopes.dtr`, `v2_stats_player_scopes.walk_pct`, `v2_stats_player_scopes.run_pct`, `v2_stats_player_scopes.crouch_pct`, `v2_stats_player_scopes.still_pct`, `v2_stats_player_scopes.air_pct`, `v2_stats_player_scopes.in_smoke_pct`, `v2_stats_player_scopes.behind_smoke_pct`, `v2_stats_player_scopes.afk_pct`, `v2_stats_player_scopes.distance_m_per_min`, `v2_stats_player_scopes.avg_first_contact_ms`, `v2_stats_player_scopes.avg_engage_order`, `v2_stats_player_scopes.bait_rate`
- `v2_stats_round_players`: `v2_stats_round_players.round_id`, `v2_stats_round_players.server`, `v2_stats_round_players.map_name`, `v2_stats_round_players.site`, `v2_stats_round_players.round_type`, `v2_stats_round_players.started_at`, `v2_stats_round_players.steam_id`, `v2_stats_round_players.side`, `v2_stats_round_players.primary_weapon`, `v2_stats_round_players.survived`, `v2_stats_round_players.won`, `v2_stats_round_players.kills`, `v2_stats_round_players.deaths`, `v2_stats_round_players.assists`, `v2_stats_round_players.flash_assists`, `v2_stats_round_players.team_kills`, `v2_stats_round_players.damage`, `v2_stats_round_players.utility_damage`, `v2_stats_round_players.headshot_kills`, `v2_stats_round_players.shots`, `v2_stats_round_players.hits`, `v2_stats_round_players.headshot_hits`, `v2_stats_round_players.entry_kill`, `v2_stats_round_players.entry_death`, `v2_stats_round_players.trade_kills`, `v2_stats_round_players.traded_death`, `v2_stats_round_players.kast`, `v2_stats_round_players.clutch_vs`, `v2_stats_round_players.clutch_won`, `v2_stats_round_players.enemies_flashed`, `v2_stats_round_players.teammates_flashed`, `v2_stats_round_players.blind_seconds_inflicted`, `v2_stats_round_players.mvp`, `v2_stats_round_players.planted`, `v2_stats_round_players.defused`, `v2_stats_round_players.defused_with_kit`, `v2_stats_round_players.rating`, `v2_stats_round_players.elo_before`, `v2_stats_round_players.elo_after`, `v2_stats_round_players.shots_still`, `v2_stats_round_players.hits_still`, `v2_stats_round_players.shots_moving`, `v2_stats_round_players.hits_moving`, `v2_stats_round_players.shots_scoped`, `v2_stats_round_players.hits_scoped`, `v2_stats_round_players.damage_taken`, `v2_stats_round_players.bomb_carry_ms`, `v2_stats_round_players.health_end`, `v2_stats_round_players.armor_end`, `v2_stats_round_players.alive_ms`, `v2_stats_round_players.walk_pct`, `v2_stats_round_players.run_pct`, `v2_stats_round_players.crouch_pct`, `v2_stats_round_players.still_pct`, `v2_stats_round_players.air_pct`, `v2_stats_round_players.distance_m`, `v2_stats_round_players.afk_ms`, `v2_stats_round_players.in_smoke_pct`, `v2_stats_round_players.behind_smoke_pct`, `v2_stats_round_players.first_contact_ms`, `v2_stats_round_players.engage_order`, `v2_stats_round_players.bait`
- `v2_stats_shots`: `v2_stats_shots.shot_id`, `v2_stats_shots.round_id`, `v2_stats_shots.steam_id`, `v2_stats_shots.weapon`, `v2_stats_shots.time_ms`, `v2_stats_shots.tick`, `v2_stats_shots.pos_x`, `v2_stats_shots.pos_y`, `v2_stats_shots.pos_z`, `v2_stats_shots.pitch`, `v2_stats_shots.yaw`, `v2_stats_shots.speed`, `v2_stats_shots.crouched`, `v2_stats_shots.scoped`, `v2_stats_shots.airborne`, `v2_stats_shots.walking`, `v2_stats_shots.still`
- `v2_stats_sessions`: `v2_stats_sessions.session_id`, `v2_stats_sessions.steam_id`, `v2_stats_sessions.server`, `v2_stats_sessions.map_name`, `v2_stats_sessions.connected_at`, `v2_stats_sessions.disconnected_at`, `v2_stats_sessions.disconnect_reason`, `v2_stats_sessions.seconds_ct`, `v2_stats_sessions.seconds_t`, `v2_stats_sessions.seconds_spec`, `v2_stats_sessions.seconds_queue`, `v2_stats_sessions.seconds_vip`, `v2_stats_sessions.ping_avg`, `v2_stats_sessions.ping_max`, `v2_stats_sessions.country`
- `v_stats_player_events`: `v_stats_player_events.event_id`, `v_stats_player_events.round_id`, `v_stats_player_events.map_name`, `v_stats_player_events.site`, `v_stats_player_events.round_type`, `v_stats_player_events.started_at`, `v_stats_player_events.steam_id`, `v_stats_player_events.type`, `v_stats_player_events.time_ms`, `v_stats_player_events.value`, `v_stats_player_events.pos_x`, `v_stats_player_events.pos_y`, `v_stats_player_events.pos_z`
- `v_stats_player_network`: `v_stats_player_network.steam_id`, `v_stats_player_network.name`, `v_stats_player_network.country`, `v_stats_player_network.sessions`, `v_stats_player_network.sessions_with_ping`, `v_stats_player_network.ping_avg`, `v_stats_player_network.ping_max`, `v_stats_player_network.seconds_vip`
- `v_stats_server_health`: `v_stats_server_health.event_id`, `v_stats_server_health.server`, `v_stats_server_health.at`, `v_stats_server_health.type`, `v_stats_server_health.module`, `v_stats_server_health.value`, `v_stats_server_health.occurrences`, `v_stats_server_health.max_ms`
- `v_stats_presence`: `v_stats_presence.snapshot_id`, `v_stats_presence.server`, `v_stats_presence.at`, `v_stats_presence.map_name`, `v_stats_presence.humans`, `v_stats_presence.bots`, `v_stats_presence.queued`
- `v_stats_player_behaviour`: `v_stats_player_behaviour.steam_id`, `v_stats_player_behaviour.name`, `v_stats_player_behaviour.contact_rounds`, `v_stats_player_behaviour.avg_first_contact_ms`, `v_stats_player_behaviour.compared_rounds`, `v_stats_player_behaviour.own_first_contact_ms`, `v_stats_player_behaviour.team_first_contact_ms`, `v_stats_player_behaviour.slowness_index`, `v_stats_player_behaviour.behind_smoke_pct`, `v_stats_player_behaviour.in_smoke_pct`, `v_stats_player_behaviour.avg_engage_order`, `v_stats_player_behaviour.bait_rounds`, `v_stats_player_behaviour.bait_chances`, `v_stats_player_behaviour.bait_rate`
- `v_stats_side_balance`: `v_stats_side_balance.server`, `v_stats_side_balance.map_name`, `v_stats_side_balance.site`, `v_stats_side_balance.rounds`, `v_stats_side_balance.ct_wins`, `v_stats_side_balance.t_wins`, `v_stats_side_balance.ct_win_pct`, `v_stats_side_balance.t_win_pct`, `v_stats_side_balance.ranked_rounds`, `v_stats_side_balance.ranked_ct_wins`, `v_stats_side_balance.ranked_ct_win_pct`, `v_stats_side_balance.avg_duration_ms`, `v_stats_side_balance.first_round_at`, `v_stats_side_balance.last_round_at`
- `v_stats_retake_odds`: `v_stats_retake_odds.server`, `v_stats_retake_odds.map_name`, `v_stats_retake_odds.site`, `v_stats_retake_odds.round_type`, `v_stats_retake_odds.ranked`, `v_stats_retake_odds.players_ct`, `v_stats_retake_odds.players_t`, `v_stats_retake_odds.ct_alive_at_plant`, `v_stats_retake_odds.t_alive_at_plant`, `v_stats_retake_odds.rounds`, `v_stats_retake_odds.ct_wins`, `v_stats_retake_odds.t_wins`, `v_stats_retake_odds.ct_win_pct`, `v_stats_retake_odds.defused_rounds`, `v_stats_retake_odds.exploded_rounds`, `v_stats_retake_odds.avg_duration_ms`
- `v_stats_weapon_meta`: `v_stats_weapon_meta.weapon`, `v_stats_weapon_meta.players`, `v_stats_weapon_meta.shots`, `v_stats_weapon_meta.hits`, `v_stats_weapon_meta.headshot_hits`, `v_stats_weapon_meta.kills`, `v_stats_weapon_meta.headshot_kills`, `v_stats_weapon_meta.damage`, `v_stats_weapon_meta.accuracy`, `v_stats_weapon_meta.hs_accuracy`, `v_stats_weapon_meta.hs_pct`, `v_stats_weapon_meta.kill_share_pct`, `v_stats_weapon_meta.primary_rounds`, `v_stats_weapon_meta.primary_players`, `v_stats_weapon_meta.primary_won_rounds`, `v_stats_weapon_meta.primary_win_pct`
- `v_stats_rivals`: `v_stats_rivals.steam_id`, `v_stats_rivals.name`, `v_stats_rivals.opponent_id`, `v_stats_rivals.opponent_name`, `v_stats_rivals.kills`, `v_stats_rivals.deaths`, `v_stats_rivals.headshot_kills`, `v_stats_rivals.headshot_deaths`, `v_stats_rivals.kd`
- `v_stats_duos`: `v_stats_duos.steam_id`, `v_stats_duos.name`, `v_stats_duos.mate_id`, `v_stats_duos.mate_name`, `v_stats_duos.rounds`, `v_stats_duos.rounds_won`, `v_stats_duos.win_pct`, `v_stats_duos.trades`
- `v_stats_server_activity`: `v_stats_server_activity.server`, `v_stats_server_activity.day`, `v_stats_server_activity.hour`, `v_stats_server_activity.snapshots`, `v_stats_server_activity.humans_avg`, `v_stats_server_activity.humans_max`, `v_stats_server_activity.bots_avg`, `v_stats_server_activity.queued_max`, `v_stats_server_activity.rounds`, `v_stats_server_activity.ranked_rounds`, `v_stats_server_activity.players`, `v_stats_server_activity.sessions`

## Not available

Steam profile data (avatar, hours, VAC), packet loss, chat contents, client-side data (FPS, settings), voice, Valve ranks and economy are not recorded. Replays (2D trajectories) arrive with 4.9.0.
