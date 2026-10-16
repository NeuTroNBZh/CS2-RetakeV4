# RetakeV4 stats

Everything the `Stats` module records during a game, how to read it from your own website or tools, and what you can compute from it.

| Page | Contents |
|---|---|
| [CATALOG.md](CATALOG.md) | Every stat the game records, by category: definition, where to read it (`view.column`) and limits |
| [DERIVED.md](DERIVED.md) | Stats that are not stored as such but can be computed from the recorded data, with a tested SQL query for each |
| [REFERENCE.md](REFERENCE.md) | Setup (`stats.json`, database, countries), conventions, Retake Rating and ELO formulas, every view |
| [INTEGRATION.md](INTEGRATION.md) | Building a website: read-only account, back end, Node.js and PHP examples, caching, recipes (profile, leaderboard, heatmap, behaviour, 2D replays) |

## What the game records

Every round between humans is recorded (bots never get stats). Rows marked *raw* are purged after `RawRetentionDays` (30 days by default); everything else is kept.

| Family | Recorded | Read it in |
|---|---|---|
| Players | SteamID64, name, country (optional, the IP is never stored), first and last seen, play time per side, spectator and queue time | `v2_stats_players`, `v_stats_player_network` |
| Sessions | One row per player and map: connection, disconnection and reason, time per side, VIP queue time, average and max ping | `v2_stats_sessions` |
| Rounds | Map, site, round type, players per side, ranked or not, winner, end reason, duration, bomb plant (site, planter, time), defuse (defuser, kit) or explosion | `v_stats_rounds`, `v_stats_maps` |
| Player in a round | Side, primary weapon, kills, deaths, assists, damage given and taken, entry and trade, clutch, flashes, plant and defuse, health and armour at the end, Retake Rating, ELO before and after, movement summary | `v2_stats_round_players` |
| Kills | Attacker, victim, assister, weapon, headshot, walls crossed, no-scope, through smoke, attacker blind or in the air, distance, both positions, time | `v_stats_kills` |
| Damage *(raw)* | Attacker, victim, weapon, body part, health and armour damage, health left, tick | `v_stats_damages` |
| Shots *(raw)* | Every bullet: weapon, position, view angles, tick, speed, crouched, scoped, in the air, walking, still | `v2_stats_shots` |
| Grenades | Type, thrower, throw and detonation positions and times, damage, players blinded and blind time | `v_stats_grenades` |
| Player events *(raw)* | Reloads, weapon pickups and drops, jumps, plant and defuse attempts, bomb pickups and drops, with positions | `v_stats_player_events` |
| Movement | Sampled 8 times a second while alive, stored as a summary: walk, run, crouch, still and air shares, distance, AFK time, time in or behind smokes, first contact, engage order, bait | `v2_stats_round_players`, `v_stats_player_behaviour` |
| Totals | Per player, for all play and per map, site, round type and side: every counter above plus rates (K/D, ADR, HS %, KAST, rating, accuracy…) | `v2_stats_players`, `v2_stats_player_scopes`, `v_stats_player_weapons`, `v_stats_leaderboard` |
| Rating and ELO | Retake Rating per round and averaged, ELO after each ranked round, ELO per day | `v_stats_round_players`, `v_stats_elo_history` |
| Analysis | Side balance per site, retake odds by man advantage, weapon meta, rivals, duos, server activity per hour | `v_stats_side_balance`, `v_stats_retake_odds`, `v_stats_weapon_meta`, `v_stats_rivals`, `v_stats_duos`, `v_stats_server_activity` |
| Replays | The best rounds, highlights and `!save` rounds of each player, with every human's trajectory (position, yaw, health…) | `v_stats_replays`, `v_stats_replay_tracks` |
| Server *(raw)* | Players online every minute, plugin starts, errors, slow frames, map votes, command and chat counts (never the contents) | `v_stats_presence`, `v_stats_server_health` |

The game does not give, so nothing records: Steam profile data (avatar, hours, VAC), packet loss, chat contents, client data (FPS, settings), voice, Valve ranks, the economy.

## Stats you can compute

Not stored as such, but computed from the data above. Each link leads to its SQL query in [DERIVED.md](DERIVED.md).

| Family | Stat | What it tells you | Computed from |
|---|---|---|---|
| Duels and aim | [Time to kill](DERIVED.md#time-to-kill) | Seconds from the first bullet that hits a player to the kill | `v_stats_kills`, `v_stats_damages` |
| Duels and aim | [First-bullet accuracy](DERIVED.md#first-bullet-accuracy) | Share of first bullets (after a 0.5 s pause) that hit | `v_stats_shots`, `v_stats_damages` |
| Duels and aim | [Bullets per kill, damage per shot and per hit](DERIVED.md#bullets-per-kill-damage-per-shot-and-per-hit) | Shooting efficiency per weapon | `v_stats_player_weapons` |
| Duels and aim | [Kills by distance](DERIVED.md#kills-by-distance) | Close (< 10 m), mid (10–25 m) and long (25 m+) kills | `v_stats_kills` (positions) |
| Duels and aim | [Body parts hit per weapon](DERIVED.md#body-parts-hit-per-weapon) | Share of head, chest, stomach, arm and leg hits | `v_stats_damages` |
| Round flow | [Time of the opening kill](DERIVED.md#time-of-the-opening-kill) | Second of the first kill, per map and site | `v_stats_kills` |
| Round flow | [Time alive when killed](DERIVED.md#time-alive-when-killed) | Average second at which a player dies | `v_stats_kills` |
| Round flow | [Value of the opening kill](DERIVED.md#value-of-the-opening-kill) | How often the side with the first kill wins | `v_stats_round_players` |
| Round flow | [Traded entry deaths](DERIVED.md#traded-entry-deaths) | Share of entry deaths avenged by a teammate | `v_stats_round_players` |
| Round flow | [Bomb time left at the defuse](DERIVED.md#bomb-time-left-at-the-defuse) | Seconds left on the bomb at the defuse, and last-second defuses | `v_stats_rounds` |
| Round flow | [Team damage](DERIVED.md#team-damage) | Damage done to teammates | `v_stats_damages`, `v_stats_round_players` |
| Utility | [Grenades per round, damage and flight time](DERIVED.md#grenades-per-round-damage-and-flight-time) | Grenades thrown per round by type, damage per grenade, time in the air | `v_stats_grenades`, `v_stats_players` |
| Form | [Longest win streak](DERIVED.md#longest-win-streak) | Most rounds won in a row | `v_stats_round_players` |
| Form | [Recent form](DERIVED.md#recent-form) | Rating over the last 20 rounds next to the career rating | `v_stats_round_players`, `v_stats_players` |
| Form | [Consistency](DERIVED.md#consistency) | Spread of the per-round rating (lower = steadier) | `v_stats_round_players` |
| Form | [Highest and lowest ELO](DERIVED.md#highest-and-lowest-elo) | Peak and lowest ELO reached | `v_stats_round_players` |
| Form | [Win rate per primary weapon](DERIVED.md#win-rate-per-primary-weapon) | Rounds won with each primary weapon | `v_stats_round_players` |
| Form | [Best site and side](DERIVED.md#best-site-and-side) | Rating and win rate on A, B, CT and T | `v_stats_player_scopes` |
| Presence | [Session length and days played](DERIVED.md#session-length-and-days-played) | Average minutes per session, days with a session | `v_stats_sessions` |
| Positions | [Death heatmap](DERIVED.md#death-heatmap) | Where players die (or kill, or throw grenades) on the map | `v_stats_kills`, `v_stats_grenades`, `v_stats_shots` |
| Positions | [From the replay trajectories](DERIVED.md#from-the-replay-trajectories) | Time to reach the site, route, distance to teammates, health over time | `v_stats_replay_tracks` (parsed in code) |

## How to get them

1. **Store the stats in MySQL or MariaDB** so that other programs can read them: in `stats.json` set `Database.Type` to `MySql` and `Database.MySqlConnectionString`. Tables and views are created at startup ([REFERENCE.md](REFERENCE.md#setup)). With the default SQLite file, the data stays in `plugins/RetakeV4/data/retakev4.db`.
2. **Create a read-only MySQL user** that can only `SELECT` the views: the `GRANT` lines are in [REFERENCE.md](REFERENCE.md) and [INTEGRATION.md](INTEGRATION.md).
3. **Query the views from a back end** (never from the browser: the database password would be public). Read the views only (`v_stats_*`, `v2_stats_*`): the `stats_*` tables can change between versions, the views are frozen by `contract/stats/views.v1.json` and `views.v2.json`.

```sql
-- Top 10 players by Retake Rating, with at least 50 ranked rounds
SELECT CAST(steam_id AS CHAR) AS steam_id, name, elo, ranked_rounds, kd, adr, hs_pct, rating
FROM v_stats_leaderboard
WHERE ranked_rounds >= 50
ORDER BY rating DESC
LIMIT 10;
```

SteamIDs are 64-bit: read them as strings in JavaScript. Dates are UTC. Ready-to-use Node.js and PHP code is in [INTEGRATION.md](INTEGRATION.md).
