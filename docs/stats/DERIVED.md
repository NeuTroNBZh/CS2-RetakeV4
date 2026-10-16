# RetakeV4 stats — derived stats

Stats that no view column holds but that you can compute from the recorded data. Each one comes with a SQL query run against a live MySQL 8 database. What every source column means is in [CATALOG.md](CATALOG.md); how to call the database from a website is in [INTEGRATION.md](INTEGRATION.md).

## At a glance

Examples are real results from a 30-round test game; yours will differ.

| Family | Stat | What it tells you | Computed from | Example |
|---|---|---|---|---|
| Duels and aim | [Time to kill](#time-to-kill) | Seconds from the first bullet that hits a player to the kill | `v_stats_kills`, `v_stats_damages` | 2.4 s |
| Duels and aim | [First-bullet accuracy](#first-bullet-accuracy) | Share of first bullets (after a 0.5 s pause) that hit | `v_stats_shots`, `v_stats_damages` | 38 % with the AK-47 |
| Duels and aim | [Bullets per kill, damage per shot and per hit](#bullets-per-kill-damage-per-shot-and-per-hit) | Shooting efficiency per weapon | `v_stats_player_weapons` | 8.3 bullets per kill |
| Duels and aim | [Kills by distance](#kills-by-distance) | Close (< 10 m), mid (10–25 m) and long (25 m+) kills | `v_stats_kills` (positions) | 3 / 12 / 7 |
| Duels and aim | [Body parts hit per weapon](#body-parts-hit-per-weapon) | Share of head, chest, stomach, arm and leg hits | `v_stats_damages` | 36 % head with the AK-47 |
| Round flow | [Time of the opening kill](#time-of-the-opening-kill) | Second of the first kill, per map and site | `v_stats_kills` | 19 s on B |
| Round flow | [Time alive when killed](#time-alive-when-killed) | Average second at which a player dies | `v_stats_kills` | 19 s |
| Round flow | [Value of the opening kill](#value-of-the-opening-kill) | How often the side with the first kill wins | `v_stats_round_players` | 91 % (CT) |
| Round flow | [Traded entry deaths](#traded-entry-deaths) | Share of entry deaths avenged by a teammate | `v_stats_round_players` | 0 % (none possible in 1 vs 1) |
| Round flow | [Bomb time left at the defuse](#bomb-time-left-at-the-defuse) | Seconds left on the bomb at the defuse, and last-second defuses | `v_stats_rounds` | 17 s |
| Round flow | [Team damage](#team-damage) | Damage done to teammates | `v_stats_damages`, `v_stats_round_players` | 0 HP (no teammate in 1 vs 1) |
| Utility | [Grenades per round, damage and flight time](#grenades-per-round-damage-and-flight-time) | Grenades thrown per round by type, damage per grenade, time in the air | `v_stats_grenades`, `v_stats_players` | 0.57 flashes per round |
| Form | [Longest win streak](#longest-win-streak) | Most rounds won in a row | `v_stats_round_players` | 6 rounds |
| Form | [Recent form](#recent-form) | Rating over the last 20 rounds next to the career rating | `v_stats_round_players`, `v_stats_players` | 1.40 vs 1.36 |
| Form | [Consistency](#consistency) | Spread of the per-round rating (lower = steadier) | `v_stats_round_players` | 0.51 |
| Form | [Highest and lowest ELO](#highest-and-lowest-elo) | Peak and lowest ELO reached | `v_stats_round_players` | 1094 / 1012 |
| Form | [Win rate per primary weapon](#win-rate-per-primary-weapon) | Rounds won with each primary weapon | `v_stats_round_players` | 75 % with the AK-47 |
| Form | [Best site and side](#best-site-and-side) | Rating and win rate on A, B, CT and T | `v_stats_player_scopes` | 1.40 on B, 1.32 on A |
| Presence | [Session length and days played](#session-length-and-days-played) | Average minutes per session, days with a session | `v_stats_sessions` | 6 min, 1 day |
| Positions | [Death heatmap](#death-heatmap) | Where players die (or kill, or throw grenades) on the map | `v_stats_kills`, `v_stats_grenades`, `v_stats_shots` | Grid of 256 × 256 units |
| Positions | [From the replay trajectories](#from-the-replay-trajectories) | Time to reach the site, route, distance to teammates, health over time | `v_stats_replay_tracks` (parsed in code) | Stored rounds only |

**Before using them**

- The queries read the public views only. Add `WHERE steam_id = CAST(? AS UNSIGNED)` (or `attacker`, `victim`) to get one player; never build the SQL by concatenation.
- Queries on `v_stats_shots`, `v2_stats_shots`, `v_stats_damages` and `v_stats_player_events` only see the last `RawRetentionDays` days (30 by default). The others cover the whole history.
- Window functions (`OVER`, `LAG`, `ROW_NUMBER`) need MySQL 8, MariaDB 10.2 or SQLite 3.25. `SQRT`, `STDDEV_POP` and `TIMESTAMPDIFF` are MySQL / MariaDB: on SQLite, use a build with math functions for `SQRT`, compute the standard deviation in code and use `julianday()` differences for durations.
- The game names the damage of an M4A1-S `m4a1` and of a USP-S `hkp2000`. Since 4.9.1 each bullet hit takes the name of the shot fired in the same tick (`m4a1_silencer`, `usp_silencer`), as its shots and kills already do. Rounds recorded with 4.9.0 keep the base names in `v_stats_damages` and in the per-weapon damage and hits of `v_stats_player_weapons`; per-player totals were never affected.
- These are heavy queries on large tables: run them in a scheduled job or cache the result (see INTEGRATION.md, "Caching").

## Duels and aim

### Time to kill

Seconds from the first bullet that hit the victim to the kill.

```sql
SELECT CAST(k.attacker AS CHAR) AS steam_id, COUNT(*) AS kills,
    ROUND(AVG(k.time_ms - d.first_hit_ms) / 1000.0, 2) AS avg_seconds_to_kill
FROM v_stats_kills k
JOIN (SELECT round_id, attacker, victim, MIN(time_ms) AS first_hit_ms
      FROM v_stats_damages WHERE victim_is_bot = 0 GROUP BY round_id, attacker, victim) d
  ON d.round_id = k.round_id AND d.attacker = k.attacker AND d.victim = k.victim
WHERE k.victim_is_bot = 0 AND k.is_team_kill = 0
GROUP BY k.attacker ORDER BY avg_seconds_to_kill;
```

### First-bullet accuracy

Share of the "first bullets" (no shot from the same player in the previous 0.5 s) that hit a player. A hit is a damage event from the shooter at the same tick.

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, weapon, COUNT(*) AS first_bullets,
    ROUND(100.0 * SUM(hit) / COUNT(*), 1) AS first_bullet_accuracy
FROM (SELECT s.steam_id, s.weapon,
          CASE WHEN EXISTS (SELECT 1 FROM v_stats_damages d
                            WHERE d.round_id = s.round_id AND d.attacker = s.steam_id AND d.tick = s.tick) THEN 1 ELSE 0 END AS hit,
          s.time_ms - LAG(s.time_ms) OVER (PARTITION BY s.round_id, s.steam_id ORDER BY s.tick) AS gap_ms
      FROM v_stats_shots s
      WHERE s.weapon NOT LIKE 'knife%' AND s.weapon NOT IN
          ('hegrenade', 'flashbang', 'smokegrenade', 'molotov', 'incgrenade', 'decoy', 'taser', 'c4')) x
WHERE gap_ms IS NULL OR gap_ms >= 500
GROUP BY steam_id, weapon HAVING COUNT(*) >= 20 ORDER BY first_bullet_accuracy DESC;
```

### Bullets per kill, damage per shot and per hit

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, weapon, shots, kills,
    ROUND(1.0 * shots / NULLIF(kills, 0), 1) AS bullets_per_kill,
    ROUND(1.0 * damage / NULLIF(shots, 0), 1) AS damage_per_shot,
    ROUND(1.0 * damage / NULLIF(hits, 0), 1) AS damage_per_hit
FROM v_stats_player_weapons ORDER BY kills DESC;
```

### Kills by distance

Close (under 10 m), mid (10 to 25 m) and long range (25 m and more) kills, from the positions (1 m = 39.37 units).

```sql
SELECT CAST(attacker AS CHAR) AS steam_id,
    SUM(CASE WHEN m < 10 THEN 1 ELSE 0 END) AS close_kills,
    SUM(CASE WHEN m >= 10 AND m < 25 THEN 1 ELSE 0 END) AS mid_kills,
    SUM(CASE WHEN m >= 25 THEN 1 ELSE 0 END) AS long_kills
FROM (SELECT attacker, SQRT((attacker_x - victim_x) * (attacker_x - victim_x) + (attacker_y - victim_y) * (attacker_y - victim_y)
          + (attacker_z - victim_z) * (attacker_z - victim_z)) / 39.37 AS m
      FROM v_stats_kills
      WHERE attacker IS NOT NULL AND victim_is_bot = 0 AND is_team_kill = 0
        AND attacker_x IS NOT NULL AND victim_x IS NOT NULL) k
GROUP BY attacker;
```

### Body parts hit per weapon

`hitgroup`: 0 generic, 1 head, 2 chest, 3 stomach, 4–5 arms, 6–7 legs.

```sql
SELECT weapon, hitgroup, COUNT(*) AS hits,
    ROUND(100.0 * COUNT(*) / SUM(COUNT(*)) OVER (PARTITION BY weapon), 1) AS pct
FROM v_stats_damages WHERE victim_is_bot = 0
GROUP BY weapon, hitgroup ORDER BY weapon, hitgroup;
```

## Round flow

### Time of the opening kill

Average second of the first kill of the round, per map and site.

```sql
SELECT map_name, site, COUNT(*) AS rounds, ROUND(AVG(first_kill_ms) / 1000.0, 1) AS avg_opening_seconds
FROM (SELECT round_id, map_name, site, MIN(time_ms) AS first_kill_ms
      FROM v_stats_kills WHERE attacker IS NOT NULL AND victim_is_bot = 0 AND is_team_kill = 0
      GROUP BY round_id, map_name, site) f
GROUP BY map_name, site ORDER BY map_name, site;
```

### Time alive when killed

Average second of the round at which a player dies (only rounds where they died).

```sql
SELECT CAST(victim AS CHAR) AS steam_id, COUNT(*) AS deaths, ROUND(AVG(time_ms) / 1000.0, 1) AS avg_death_second
FROM v_stats_kills WHERE victim_is_bot = 0
GROUP BY victim ORDER BY avg_death_second;
```

### Value of the opening kill

How often the side that gets the first kill wins the round.

```sql
SELECT side, COUNT(*) AS rounds_with_opening_kill, ROUND(100.0 * SUM(won) / COUNT(*), 1) AS win_pct_after_opening_kill
FROM v_stats_round_players WHERE entry_kill = 1 GROUP BY side;
```

### Traded entry deaths

Share of a player's entry deaths that a teammate avenged within `TradeWindowSeconds`.

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, SUM(entry_death) AS entry_deaths,
    SUM(CASE WHEN entry_death = 1 AND traded_death = 1 THEN 1 ELSE 0 END) AS traded_entry_deaths,
    ROUND(100.0 * SUM(CASE WHEN entry_death = 1 AND traded_death = 1 THEN 1 ELSE 0 END) / NULLIF(SUM(entry_death), 0), 1) AS traded_pct
FROM v_stats_round_players GROUP BY steam_id;
```

### Bomb time left at the defuse

Seconds left on the bomb when it was defused. The query assumes the default 40 s bomb timer (`mp_c4timer`): change `40000` if your server uses another value.

```sql
SELECT map_name, site, COUNT(*) AS defuses,
    ROUND(AVG(40000 - (duration_ms - plant_time_ms)) / 1000.0, 1) AS avg_seconds_left,
    SUM(CASE WHEN 40000 - (duration_ms - plant_time_ms) < 3000 THEN 1 ELSE 0 END) AS last_3_seconds
FROM v_stats_rounds WHERE defused = 1 AND plant_time_ms IS NOT NULL
GROUP BY map_name, site;
```

### Team damage

Damage a player did to teammates (`v_stats_damages` has no side: the sides come from the round players).

```sql
SELECT CAST(d.attacker AS CHAR) AS steam_id, COUNT(*) AS team_hits, SUM(d.dmg_health) AS team_damage
FROM v_stats_damages d
JOIN v_stats_round_players a ON a.round_id = d.round_id AND a.steam_id = d.attacker
JOIN v_stats_round_players v ON v.round_id = d.round_id AND v.steam_id = d.victim
WHERE d.victim_is_bot = 0 AND d.attacker <> d.victim AND a.side = v.side
GROUP BY d.attacker ORDER BY team_damage DESC;
```

## Utility

### Grenades per round, damage and flight time

```sql
SELECT CAST(g.steam_id AS CHAR) AS steam_id, g.type, COUNT(*) AS thrown, r.rounds,
    ROUND(1.0 * COUNT(*) / r.rounds, 2) AS per_round, SUM(g.damage) AS damage,
    ROUND(1.0 * SUM(g.damage) / COUNT(*), 1) AS damage_per_grenade,
    ROUND(AVG(g.detonated_ms - g.thrown_ms) / 1000.0, 2) AS avg_flight_seconds
FROM v_stats_grenades g JOIN v_stats_players r ON r.steam_id = g.steam_id
GROUP BY g.steam_id, g.type, r.rounds ORDER BY steam_id, g.type;
```

## Form and progression

### Longest win streak

Most rounds won in a row by a player (rounds in play order).

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, MAX(streak) AS longest_win_streak
FROM (SELECT steam_id, COUNT(*) AS streak
      FROM (SELECT steam_id, won,
                ROW_NUMBER() OVER (PARTITION BY steam_id ORDER BY started_at, round_id)
              - ROW_NUMBER() OVER (PARTITION BY steam_id, won ORDER BY started_at, round_id) AS grp
            FROM v_stats_round_players) r
      WHERE won = 1 GROUP BY steam_id, grp) s
GROUP BY steam_id;
```

### Recent form

Retake Rating over the last 20 rounds, next to the career rating.

```sql
SELECT CAST(r.steam_id AS CHAR) AS steam_id, ROUND(AVG(r.rating), 2) AS last_20_rating, p.rating AS career_rating
FROM (SELECT steam_id, rating, ROW_NUMBER() OVER (PARTITION BY steam_id ORDER BY started_at DESC, round_id DESC) AS n
      FROM v_stats_round_players) r
JOIN v_stats_players p ON p.steam_id = r.steam_id
WHERE r.n <= 20 GROUP BY r.steam_id, p.rating;
```

### Consistency

Standard deviation of the per-round rating: the lower, the steadier the player.

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, COUNT(*) AS rounds, ROUND(AVG(rating), 2) AS rating,
    ROUND(STDDEV_POP(rating), 2) AS rating_stddev
FROM v_stats_round_players GROUP BY steam_id HAVING COUNT(*) >= 20;
```

### Highest and lowest ELO

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, MAX(elo_after) AS peak_elo, MIN(elo_after) AS lowest_elo
FROM v_stats_round_players WHERE elo_after IS NOT NULL GROUP BY steam_id;
```

### Win rate per primary weapon

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, primary_weapon, COUNT(*) AS rounds,
    ROUND(100.0 * SUM(won) / COUNT(*), 1) AS win_pct
FROM v_stats_round_players WHERE primary_weapon IS NOT NULL
GROUP BY steam_id, primary_weapon;
```

### Best site and side

The totals are already split per site, side, map and round type (`scope`); compare the rows of a player.

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, scope, rounds, rating, win_pct
FROM v_stats_player_scopes WHERE scope IN ('site:A', 'site:B', 'side:CT', 'side:T')
ORDER BY steam_id, scope;
```

## Presence

### Session length and days played

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, COUNT(*) AS sessions,
    ROUND(AVG(TIMESTAMPDIFF(SECOND, connected_at, disconnected_at)) / 60.0, 1) AS avg_session_minutes,
    COUNT(DISTINCT DATE(connected_at)) AS days_played
FROM v_stats_sessions WHERE connected_at IS NOT NULL AND disconnected_at IS NOT NULL
GROUP BY steam_id;
```

## Positions

### Death heatmap

Deaths counted per square of 256 × 256 units; convert the squares to radar pixels as described in INTEGRATION.md ("Heatmap").

```sql
SELECT map_name, FLOOR(victim_x / 256) AS cell_x, FLOOR(victim_y / 256) AS cell_y, COUNT(*) AS deaths
FROM v_stats_kills WHERE victim_x IS NOT NULL AND victim_is_bot = 0
GROUP BY map_name, FLOOR(victim_x / 256), FLOOR(victim_y / 256) ORDER BY deaths DESC;
```

Use `attacker_x` / `attacker_y` instead for the spots players kill from, `v_stats_grenades.det_x` / `det_y` for where grenades land, and `v_stats_shots.pos_x` / `pos_y` for where players shoot from.

### From the replay trajectories

`v_stats_replay_tracks.frames` holds each human's position, view direction, state and health several times a second for every stored round (format in INTEGRATION.md, "Replays"). Parsed in code, it gives stats that SQL alone cannot:

- time to reach the bomb site, and the route taken (which entrance);
- distance kept from teammates (playing alone or together);
- where a player stands at a given second of the round, and how long they hold a spot;
- health over time (when the player was damaged, how long they survived with low health).

They cover the stored rounds only (each player's best rounds, highlights and `!save`), not every round.
