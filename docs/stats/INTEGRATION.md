# RetakeV4 stats — building a website

How to show the RetakeV4 stats on your own website: database account, back end, examples in Node.js and PHP, caching, data types and ready-made recipes. What each column means is in [CATALOG.md](CATALOG.md); the view reference and the server setup are in [REFERENCE.md](REFERENCE.md).

## 1. Architecture

```
CS2 server (RetakeV4) ──writes──▶ MySQL / MariaDB ◀──reads views── your back end (Node.js, PHP…) ◀──HTTPS/JSON── browser
```

- The plugin writes; your site only **reads the views** (`v_stats_*`, `v2_stats_*`). Never read or write the `stats_*` tables: they change between versions, the views do not.
- **A back end is mandatory.** The browser never talks to the database: database credentials in JavaScript sent to a browser are public, and MySQL must never be reachable from the internet. Your back end runs the queries, caches the results and serves JSON.
- SQLite works for the plugin but only the game server can read the file: a website needs MySQL 8.0.17+ or MariaDB 11.4.

## 2. Read-only account

Create one account for the website, allowed to `SELECT` on the views only, from the web server's address only. By default MySQL and MariaDB views run with the rights of the account that created them (`SQL SECURITY DEFINER`, the plugin's account), so the website account needs no right on the tables.

```sql
CREATE USER 'stats_reader'@'%' IDENTIFIED BY 'change-me';
-- v1 views (contract/stats/views.v1.json)
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
-- v2 views (contract/stats/views.v2.json)
GRANT SELECT ON retakev4.v2_stats_players TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v2_stats_player_scopes TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v2_stats_round_players TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v2_stats_shots TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v2_stats_sessions TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_player_events TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_player_network TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_server_health TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_presence TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_player_behaviour TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_retake_odds TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_side_balance TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_weapon_meta TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_rivals TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_duos TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_server_activity TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_replays TO 'stats_reader'@'%';
GRANT SELECT ON retakev4.v_stats_replay_tracks TO 'stats_reader'@'%';
```

Replace `retakev4` by your database name and, in production, `'%'` by the web server's address (`'stats_reader'@'10.0.0.5'`). Add `REQUIRE SSL` (`ALTER USER 'stats_reader'@'10.0.0.5' REQUIRE SSL;`) when the web server and the database are on different machines, and keep port 3306 closed to everything else. A new plugin version can add views: run the missing `GRANT` lines again after each upgrade (a view without a grant is simply invisible to the account).

## 3. Data types

- **SteamID64 is a string.** `steam_id`, `attacker`, `victim`, `assister`, `planter`, `defuser`, `opponent_id` and `mate_id` are `BIGINT UNSIGNED` values around 7.6 × 10^16, above JavaScript's safe integer (2^53): as a JS `number` they silently lose their last digits. Select them as text (`CAST(steam_id AS CHAR) AS steam_id`) and keep them as strings everywhere (URLs, JSON, caches).
- **Compare a SteamID parameter as an integer**: `WHERE steam_id = CAST(? AS UNSIGNED)`. The cast keeps the comparison an exact integer one, avoids implicit string-to-number conversion pitfalls and still uses the index. Validate the parameter first (`^\d{17}$`).
- **Dates are UTC** (`DATETIME(3)`, no time zone). Read them as strings (`dateStrings: true` in Node.js) and add the zone yourself: `'2026-10-14 20:15:00.000'` → `'2026-10-14T20:15:00.000Z'`. Convert to the visitor's time zone in the browser. `day` columns (`v_stats_elo_history`, `v_stats_server_activity`) and `hour` are UTC too.
- Booleans are `0` / `1`. Ratios are `DOUBLE` and `NULL` when undefined (no deaths for `kd`…): show "–", not 0.
- **MySQL `SUM()` returns `DECIMAL`, which drivers hand over as strings.** The derived views already cast their sums to `BIGINT`, but a few older columns are plain sums (`v_stats_player_network.seconds_vip`), and so are the sums in your own queries (the recipes below). Use `decimalNumbers: true` in Node.js, wrap them in `CAST(SUM(...) AS SIGNED)`, or convert in code.
- **Player names are user input**: escape them before inserting them in HTML (`textContent` in the browser, `htmlspecialchars` in PHP).

## 4. Node.js example (Express + mysql2)

```js
// npm install express mysql2
import fs from 'node:fs'; // only needed for the ssl option below
import express from 'express';
import mysql from 'mysql2/promise';

const pool = mysql.createPool({
  host: process.env.STATS_DB_HOST,
  user: process.env.STATS_DB_USER,          // stats_reader
  password: process.env.STATS_DB_PASSWORD,
  database: process.env.STATS_DB_NAME,      // retakev4
  connectionLimit: 5,
  supportBigNumbers: true,                  // never turn 64-bit values into lossy numbers
  dateStrings: true,                        // UTC 'YYYY-MM-DD HH:MM:SS.mmm'
  decimalNumbers: true,
  // ssl: { ca: fs.readFileSync(process.env.STATS_DB_CA) },  // TLS, when the database is on another machine
});

// Small bounded in-memory cache: at most MAX_ENTRIES keys (oldest evicted first), kept ttlMs milliseconds.
// Misses (null) are never cached, so arbitrary ids cannot fill the memory.
// 100, not more: a cached replay holds its parsed frames (about 125 KB of text, several times that in memory).
const MAX_ENTRIES = 100;
const cache = new Map();
async function cached(key, ttlMs, load) {
  const hit = cache.get(key);
  if (hit && hit.expires > Date.now()) return hit.value;
  const value = await load();
  if (value !== null && value !== undefined) {
    cache.delete(key);
    cache.set(key, { value, expires: Date.now() + ttlMs });
    if (cache.size > MAX_ENTRIES) cache.delete(cache.keys().next().value);
  }
  return value;
}

const utc = (text) => (text ? text.replace(' ', 'T') + 'Z' : null);
const app = express();

app.get('/api/players/:steamId', async (req, res) => {
  const { steamId } = req.params;
  if (!/^\d{17}$/.test(steamId)) return res.status(400).json({ error: 'invalid steam id' });
  try {
    const player = await cached(`player:${steamId}`, 60_000, async () => {
      const [rows] = await pool.execute(
        `SELECT CAST(steam_id AS CHAR) AS steam_id, name, country, elo, last_seen, rounds, kills, deaths,
                kd, adr, hs_pct, kast_pct, rating, still_accuracy, avg_first_contact_ms, bait_rate
         FROM v2_stats_players WHERE steam_id = CAST(? AS UNSIGNED)`,
        [steamId]);
      return rows[0] ? { ...rows[0], last_seen: utc(rows[0].last_seen) } : null;
    });
    if (!player) return res.status(404).json({ error: 'unknown player' });
    res.set('Cache-Control', 'public, max-age=60').json(player);
  } catch (error) {
    console.error(error);
    res.status(500).json({ error: 'stats unavailable' });
  }
});

app.get('/api/leaderboard', async (req, res) => {
  const page = Math.min(200, Math.max(0, Number.parseInt(String(req.query.page ?? '0'), 10) || 0));
  try {
    const rows = await cached(`leaderboard:${page}`, 60_000, async () => {
      // With MySQL 8.0.22+, execute() can reject numeric LIMIT / OFFSET values ("Incorrect arguments to mysqld_stmt_execute"): pass strings.
      const [result] = await pool.execute(
        `SELECT CAST(steam_id AS CHAR) AS steam_id, name, elo, ranked_rounds, rating, adr, kd
         FROM v_stats_leaderboard WHERE ranked_rounds >= ? ORDER BY elo DESC LIMIT ? OFFSET ?`,
        ['50', '50', String(page * 50)]);
      return result;
    });
    res.set('Cache-Control', 'public, max-age=60').json(rows);
  } catch (error) {
    console.error(error);
    res.status(500).json({ error: 'stats unavailable' });
  }
});

app.listen(3000);
```

## 5. PHP example (PDO)

```php
<?php
// player.php — runs on the server; the browser only receives the JSON.
$pdo = new PDO(
    'mysql:host=' . getenv('STATS_DB_HOST') . ';dbname=' . getenv('STATS_DB_NAME') . ';charset=utf8mb4',
    getenv('STATS_DB_USER'),
    getenv('STATS_DB_PASSWORD'),
    [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        PDO::ATTR_EMULATE_PREPARES => false, // real prepared statements
        // PDO::MYSQL_ATTR_SSL_CA => getenv('STATS_DB_CA'), // TLS, when the database is on another machine
    ]
);

$steamId = $_GET['steam_id'] ?? null;
if (!is_string($steamId) || !preg_match('/^\d{17}\z/', $steamId)) {
    http_response_code(400);
    exit;
}

$cacheKey = 'retake_player_' . $steamId;
$player = function_exists('apcu_fetch') ? apcu_fetch($cacheKey, $hit) : null;
if (empty($hit)) {
    $statement = $pdo->prepare(
        'SELECT CAST(steam_id AS CHAR) AS steam_id, name, country, elo, rounds, kills, deaths, kd, adr, rating
         FROM v2_stats_players WHERE steam_id = CAST(? AS UNSIGNED)'
    );
    $statement->execute([$steamId]);
    $player = $statement->fetch() ?: null;
    // Cache hits only: a miss is never cached, so arbitrary ids cannot fill the cache.
    if ($player !== null && function_exists('apcu_store')) {
        apcu_store($cacheKey, $player, 60);
    }
}

if (!$player) {
    http_response_code(404);
    exit;
}

header('Content-Type: application/json');
header('Cache-Control: public, max-age=60');
echo json_encode($player);
```

Without APCu, cache in Redis, Memcached or a file. When you print a name in HTML, use `htmlspecialchars($player['name'], ENT_QUOTES, 'UTF-8')`.

## 6. Caching

The stats change once per round (about every 30 to 90 s per server); nobody needs fresher than a minute. Cache every response, and the heavy views longer:

| Data | Suggested cache | Why |
|---|---|---|
| One player (`v2_stats_players`, `v2_stats_player_scopes`, `v_stats_player_weapons`), leaderboard | 1 min | one indexed lookup or a small sort |
| `v_stats_elo_history`, `v_stats_rounds`, `v_stats_round_players` of one player | 1–5 min | filtered on an index |
| `v_stats_player_behaviour`, `v_stats_rivals`, `v_stats_duos`, `v_stats_weapon_meta`, `v_stats_retake_odds`, `v_stats_side_balance`, `v_stats_server_activity` | 10–60 min | they aggregate whole tables at each query |
| Heatmaps (`v_stats_kills`, `v2_stats_shots`) | 1 h or nightly | thousands of rows |

On a large database, refresh the heavy views on a schedule (cron) into the site's own tables or files instead of querying them per visitor, and set a read timeout on the connection.

## 7. Recipes

### Player profile

```sql
-- Header and totals
SELECT CAST(steam_id AS CHAR) AS steam_id, name, country, elo, rounds, rounds_won, kills, deaths, kd, adr, hs_pct,
    kast_pct, rating, entry_success_pct, clutch_won_pct, seconds_total
FROM v2_stats_players WHERE steam_id = CAST(? AS UNSIGNED);

-- Per map, site and side
SELECT scope, rounds, win_pct, rating, adr, kd
FROM v2_stats_player_scopes WHERE steam_id = CAST(? AS UNSIGNED) AND rounds >= 10 ORDER BY scope;

-- Weapons
SELECT weapon, kills, accuracy, hs_pct FROM v_stats_player_weapons
WHERE steam_id = CAST(? AS UNSIGNED) ORDER BY kills DESC LIMIT 10;

-- ELO curve (last 90 days)
SELECT day, elo FROM v_stats_elo_history
WHERE steam_id = CAST(? AS UNSIGNED) AND day >= DATE_FORMAT(UTC_DATE() - INTERVAL 90 DAY, '%Y-%m-%d') ORDER BY day;
-- MySQL / MariaDB syntax; SQLite: day >= date('now', '-90 day')

-- Nemesis and favourite victims
SELECT CAST(opponent_id AS CHAR) AS opponent_id, opponent_name, kills, deaths
FROM v_stats_rivals WHERE steam_id = CAST(? AS UNSIGNED) ORDER BY deaths DESC LIMIT 5;

-- Best partners (at least 20 rounds together)
SELECT CAST(mate_id AS CHAR) AS mate_id, mate_name, rounds, win_pct, trades
FROM v_stats_duos WHERE steam_id = CAST(? AS UNSIGNED) AND rounds >= 20 ORDER BY win_pct DESC LIMIT 5;
```

### Leaderboard

```sql
SELECT CAST(steam_id AS CHAR) AS steam_id, name, elo, ranked_rounds, rating, adr, kd, hs_pct
FROM v_stats_leaderboard WHERE ranked_rounds >= ? ORDER BY elo DESC LIMIT ? OFFSET ?;
```

Sort by `rating` or `adr` the same way; always require a minimum number of rounds, or one lucky round tops the list.

### Heatmap (kill and shot positions)

```sql
-- Where players die on a site (enemy kills only)
SELECT victim_x, victim_y FROM v_stats_kills
WHERE map_name = ? AND site = ? AND victim_is_bot = 0 AND is_team_kill = 0 AND attacker IS NOT NULL AND victim_x IS NOT NULL;

-- Where a player shoots from (raw shots: last RawRetentionDays only)
SELECT s.pos_x, s.pos_y FROM v2_stats_shots s JOIN v_stats_rounds r ON r.round_id = s.round_id
WHERE s.steam_id = CAST(? AS UNSIGNED) AND r.map_name = ? AND s.pos_x IS NOT NULL;

-- Pre-binned grid (100 units per cell) for large maps
SELECT FLOOR(victim_x / 100) AS cell_x, FLOOR(victim_y / 100) AS cell_y, COUNT(*) AS deaths
FROM v_stats_kills WHERE map_name = ? AND victim_is_bot = 0 AND victim_x IS NOT NULL
GROUP BY FLOOR(victim_x / 100), FLOOR(victim_y / 100);
```

Positions are world units. To draw them on the map's radar image (1024 × 1024), read `pos_x`, `pos_y` and `scale` from the map's overview file (`game/csgo/resource/overviews/<map>.txt` in the CS2 files) and convert: `px = (x - pos_x) / scale`, `py = (pos_y - y) / scale`. The values of the Retake maps are listed in "Replays" below.

### Behaviour: slow players and baiters

```sql
-- Slowest to engage compared with their teammates
SELECT CAST(steam_id AS CHAR) AS steam_id, name, compared_rounds, slowness_index, behind_smoke_pct
FROM v_stats_player_behaviour WHERE compared_rounds >= 30 ORDER BY slowness_index DESC LIMIT 20;

-- Baiters
SELECT CAST(steam_id AS CHAR) AS steam_id, name, bait_chances, bait_rounds, bait_rate, avg_engage_order
FROM v_stats_player_behaviour WHERE bait_chances >= 10 ORDER BY bait_rate DESC LIMIT 20;

-- A player's last rounds, for the detail page
SELECT round_id, map_name, site, started_at, first_contact_ms, engage_order, bait, distance_m, afk_ms
FROM v2_stats_round_players WHERE steam_id = CAST(? AS UNSIGNED) AND alive_ms IS NOT NULL
ORDER BY round_id DESC LIMIT 20;
```

Starting thresholds (the plugin applies none): "slow" when `slowness_index` ≥ 1.3 over 30+ `compared_rounds`; "baiter" when `bait_rate` ≥ 0.5 over 10+ `bait_chances`, or `avg_engage_order` ≥ 2.5 over 30+ `contact_rounds`. Show them as tendencies, never as accusations: the server knows no walls.

### Retake odds and side balance

```sql
-- CT win rate by player counts at the round start, on one map and site
SELECT players_ct, players_t, SUM(rounds) AS rounds, ROUND(100.0 * SUM(ct_wins) / SUM(rounds), 1) AS ct_win_pct
FROM v_stats_retake_odds WHERE map_name = ? AND site = ?
GROUP BY players_ct, players_t HAVING SUM(rounds) >= 20 ORDER BY players_ct, players_t;

-- After the plant: CT win rate by players still alive. With the default automatic plant (AutoPlant) the bomb is planted at the end
-- of the freeze, so these counts are close to the starting counts and the split adds little; it becomes informative in a mode
-- where players plant during the round (FastPlant).
SELECT ct_alive_at_plant, t_alive_at_plant, SUM(rounds) AS rounds, ROUND(100.0 * SUM(ct_wins) / SUM(rounds), 1) AS ct_win_pct
FROM v_stats_retake_odds WHERE map_name = ? AND ct_alive_at_plant IS NOT NULL
GROUP BY ct_alive_at_plant, t_alive_at_plant ORDER BY ct_alive_at_plant, t_alive_at_plant;

-- Which site favours which side
SELECT map_name, site, SUM(rounds) AS rounds, ROUND(100.0 * SUM(ct_wins) / SUM(rounds), 1) AS ct_win_pct
FROM v_stats_side_balance GROUP BY map_name, site ORDER BY map_name, site;
```

Merge rows with `SUM(rounds)` and `SUM(ct_wins)`, never by averaging percentages.

### Server activity

```sql
-- Busiest hours (UTC) over the last 30 days
SELECT hour, ROUND(AVG(humans_avg), 1) AS humans, SUM(rounds) AS rounds
FROM v_stats_server_activity
WHERE server = ? AND day >= DATE_FORMAT(UTC_DATE() - INTERVAL 30 DAY, '%Y-%m-%d') AND snapshots > 0
GROUP BY hour ORDER BY hour;
-- MySQL / MariaDB syntax; SQLite: day >= date('now', '-30 day')
```

### Replays

A stored replay is a round with the trajectory of every human participant. List them from `v_stats_replays` (one row per owner), read the tracks from `v_stats_replay_tracks` and the rest of the round from the v1 views by `round_id`.

```sql
-- A player's replays: best rounds, highlights, saved rounds
SELECT round_id, map_name, site, round_type, started_at, reason, score
FROM v_stats_replays WHERE steam_id = CAST(? AS UNSIGNED)
ORDER BY reason, score DESC, owned_at DESC;

-- Latest highlights on the server
SELECT round_id, CAST(steam_id AS CHAR) AS steam_id, name, map_name, started_at
FROM v_stats_replays WHERE reason = 'highlight' ORDER BY owned_at DESC LIMIT 20;

-- One replay: tracks, kills, grenades, bomb
SELECT CAST(steam_id AS CHAR) AS steam_id, name, side, frames FROM v_stats_replay_tracks WHERE round_id = ?;
SELECT CAST(attacker AS CHAR) AS attacker, CAST(victim AS CHAR) AS victim, victim_is_bot, weapon, headshot, victim_x, victim_y, time_ms
FROM v_stats_kills WHERE round_id = ? ORDER BY time_ms;
SELECT CAST(steam_id AS CHAR) AS steam_id, type, detonated_ms, det_x, det_y
FROM v_stats_grenades WHERE round_id = ? AND detonated_ms IS NOT NULL ORDER BY detonated_ms;
SELECT CAST(planter AS CHAR) AS planter, planted, plant_time_ms, defused, bomb_exploded FROM v_stats_rounds WHERE round_id = ?;
```

**Frames.** `frames` is a JSON array of `[t_ms, x, y, z, yaw, flags, hp]`:

| Field | Meaning |
|---|---|
| `t_ms` | ms since the round start: the same clock as `v_stats_kills.time_ms`, `v_stats_grenades.detonated_ms` and `v_stats_rounds.plant_time_ms` |
| `x`, `y`, `z` | world position of the feet, game units rounded to the integer |
| `yaw` | view direction, 0–359 degrees: 0 = +X, 90 = +Y (counter-clockwise seen from above) |
| `flags` | bits: 1 crouched, 2 walking, 4 in the air, 8 in a smoke, 16 bomb carrier |
| `hp` | health |

Frames are one sample every `SampleEveryTicks` ticks (`sample_hz` per second, 8 by default) while the player is alive, from the end of the freeze time: a track stops at the death or the departure. Interpolate between two frames for a smooth animation. Bots have no track: show their deaths from `v_stats_kills` (`victim_is_bot` = 1). Replay writes only start with `SampleEveryTicks` > 0: the caps do nothing when it is 0 (no samples, so no track). The bomb position is not stored: use the planter's position at `plant_time_ms` in their track (none when the plant was automatic or made by a bot). Shots and damages of a round older than `RawRetentionDays` are purged; tracks, kills, grenades and the bomb stay.

**De_dust2 `rotate`.** The overview file of de_dust2 has `rotate 1`. The conversion below is the standard one and was not checked against a rotated image: check in game (a few kills near known landmarks) that the radar image you export matches it before publishing de_dust2 replays.

**Radar conversion.** A CS2 radar image is 1024 × 1024 pixels. With `pos_x`, `pos_y` (world position of the image's top-left corner) and `scale` (world units per pixel) of the map:

```
px = (x - pos_x) / scale
py = (pos_y - y) / scale
```

On the canvas the yaw becomes `dx = cos(yaw)`, `dy = -sin(yaw)` (the image's y axis points down). Values read from the CS2 game files (`resource/overviews/<map>.txt` inside `game/csgo/pak01_dir.vpk`) on 2026-10-14; check them again after a map update:

| Map | `pos_x` | `pos_y` | `scale` | Lower level |
|---|---|---|---|---|
| de_ancient | -2953 | 2164 | 5 | |
| de_ancient_night | -2953 | 2164 | 5 | |
| de_anubis | -2796 | 3328 | 5.22 | |
| de_cache | -2000 | 3250 | 5.5 | |
| de_dust2 | -2476 | 3239 | 4.4 | |
| de_inferno | -2087 | 3870 | 4.9 | |
| de_mirage | -3230 | 1713 | 5.00 | |
| de_nuke | -3453 | 2887 | 7 | `z` below -495 |
| de_overpass | -4831 | 1781 | 5.2 | |
| de_train | -2308 | 2078 | 4.082077 | `z` below -50 |
| de_vertigo | -3168 | 1762 | 4.0 | `z` below 11700 |

The radar images are `panorama/images/overheadmaps/<map>_radar_psd.vtex_c` in the same archive (export them to PNG with a Source 2 resource viewer); the maps with a lower level have a second image `<map>_lower_radar_psd.vtex_c`, drawn with the same `pos_x`, `pos_y` and `scale`, to use for the frames whose `z` is in the "Lower level" range. For a workshop map, read the overview file shipped with it.

**Back end route** (Node.js, added to the example of section 4; a stored replay never changes, so it can be cached long — it may only disappear when its owners replace it):

```js
app.get('/api/replays/:roundId', async (req, res) => {
  const { roundId } = req.params;
  if (!/^\d{1,18}$/.test(roundId)) return res.status(400).json({ error: 'invalid round id' });
  try {
    const replay = await cached(`replay:${roundId}`, 3_600_000, async () => {
      const [rounds] = await pool.execute(
        `SELECT round_id, map_name, site, round_type, winner, duration_ms, started_at, sample_hz
         FROM v_stats_replays WHERE round_id = ? LIMIT 1`, [roundId]);
      if (!rounds[0]) return null;
      const [tracks] = await pool.execute(
        'SELECT CAST(steam_id AS CHAR) AS steam_id, name, side, frames FROM v_stats_replay_tracks WHERE round_id = ?', [roundId]);
      const [kills] = await pool.execute(
        `SELECT CAST(attacker AS CHAR) AS attacker, CAST(victim AS CHAR) AS victim, victim_is_bot, weapon, headshot,
                victim_x, victim_y, time_ms
         FROM v_stats_kills WHERE round_id = ? ORDER BY time_ms`, [roundId]);
      const [grenades] = await pool.execute(
        `SELECT type, detonated_ms, det_x, det_y
         FROM v_stats_grenades WHERE round_id = ? AND detonated_ms IS NOT NULL ORDER BY detonated_ms`, [roundId]);
      return {
        ...rounds[0],
        started_at: utc(rounds[0].started_at),
        tracks: tracks.map((track) => ({ ...track, frames: JSON.parse(track.frames) })),
        kills,
        grenades,
      };
    });
    if (!replay) return res.status(404).json({ error: 'unknown replay' });
    res.set('Cache-Control', 'public, max-age=3600').json(replay);
  } catch (error) {
    console.error(error);
    res.status(500).json({ error: 'stats unavailable' });
  }
});
```

**Viewer** (browser, plain canvas; the radar PNGs are served by your site under `/radars/<map>.png`):

```html
<canvas id="replay" width="1024" height="1024"></canvas>
<script type="module">
// pos_x, pos_y, scale of each map (table above).
const RADARS = {
  de_ancient: [-2953, 2164, 5], de_ancient_night: [-2953, 2164, 5], de_anubis: [-2796, 3328, 5.22],
  de_cache: [-2000, 3250, 5.5], de_dust2: [-2476, 3239, 4.4], de_inferno: [-2087, 3870, 4.9],
  de_mirage: [-3230, 1713, 5], de_nuke: [-3453, 2887, 7], de_overpass: [-4831, 1781, 5.2],
  de_train: [-2308, 2078, 4.082077], de_vertigo: [-3168, 1762, 4],
};
const SIDE_COLORS = { CT: '#5b9bd5', T: '#e0a030' };
const IN_SMOKE = 8;
const BOMB_CARRIER = 16;
// How long a grenade stays drawn after its detonation (ms) and its drawn radius (game units).
const GRENADES = { Smoke: [18000, 144, 'rgba(200,200,200,0.6)'], Molotov: [7000, 120, 'rgba(255,90,0,0.5)'],
  He: [400, 60, 'rgba(255,60,60,0.7)'], Flash: [400, 40, 'rgba(255,255,255,0.8)'] };
const SPEED = 1; // 2 = twice as fast

// Position of a track at time t, interpolated between the surrounding frames; null before its first frame or after its
// last one (not sampled yet, dead or gone).
function positionAt(frames, t) {
  if (frames.length === 0 || t < frames[0][0] || t > frames[frames.length - 1][0]) return null;
  let i = 1;
  while (i < frames.length && frames[i][0] < t) i++;
  const a = frames[i - 1];
  const b = frames[Math.min(i, frames.length - 1)];
  const k = b[0] === a[0] ? 0 : (t - a[0]) / (b[0] - a[0]);
  return { x: a[1] + (b[1] - a[1]) * k, y: a[2] + (b[2] - a[2]) * k, yaw: a[4], flags: a[5], hp: a[6] };
}

async function play(roundId) {
  const response = await fetch(`/api/replays/${encodeURIComponent(roundId)}`);
  if (!response.ok) throw new Error('replay not found');
  const replay = await response.json();
  const radarInfo = RADARS[replay.map_name];
  if (!radarInfo) throw new Error(`no radar data for map ${replay.map_name}: add it to RADARS`);
  const [posX, posY, scale] = radarInfo;
  const toPixels = (x, y) => [(x - posX) / scale, (posY - y) / scale];
  const radar = new Image();
  radar.src = `/radars/${replay.map_name}.png`;
  await radar.decode();
  const ctx = document.getElementById('replay').getContext('2d');
  const firstFrame = Math.min(...replay.tracks.map((track) => track.frames[0]?.[0] ?? 0));
  const end = replay.duration_ms;
  const startedAt = performance.now();

  function draw(now) {
    const t = firstFrame + (now - startedAt) * SPEED;
    ctx.drawImage(radar, 0, 0, 1024, 1024);
    for (const grenade of replay.grenades) {
      const style = GRENADES[grenade.type];
      if (!style || t < grenade.detonated_ms || t > grenade.detonated_ms + style[0]) continue;
      const [px, py] = toPixels(grenade.det_x, grenade.det_y);
      ctx.fillStyle = style[2];
      ctx.beginPath();
      ctx.arc(px, py, style[1] / scale, 0, 2 * Math.PI);
      ctx.fill();
    }
    ctx.strokeStyle = '#d33';
    ctx.lineWidth = 2;
    for (const kill of replay.kills) {
      if (kill.time_ms > t || kill.victim_x == null) continue;
      const [px, py] = toPixels(kill.victim_x, kill.victim_y);
      ctx.beginPath();
      ctx.moveTo(px - 6, py - 6); ctx.lineTo(px + 6, py + 6);
      ctx.moveTo(px + 6, py - 6); ctx.lineTo(px - 6, py + 6);
      ctx.stroke();
    }
    for (const track of replay.tracks) {
      const p = positionAt(track.frames, t);
      if (!p) continue;
      const [px, py] = toPixels(p.x, p.y);
      const yaw = (p.yaw * Math.PI) / 180;
      ctx.strokeStyle = '#fff';
      ctx.beginPath();
      ctx.moveTo(px, py);
      ctx.lineTo(px + 14 * Math.cos(yaw), py - 14 * Math.sin(yaw));
      ctx.stroke();
      ctx.globalAlpha = p.flags & IN_SMOKE ? 0.5 : 1;
      ctx.fillStyle = SIDE_COLORS[track.side];
      ctx.beginPath();
      ctx.arc(px, py, 7, 0, 2 * Math.PI);
      ctx.fill();
      ctx.globalAlpha = 1;
      if (p.flags & BOMB_CARRIER) {
        ctx.fillStyle = '#f33';
        ctx.fillRect(px - 3, py - 3, 6, 6);
      }
      ctx.fillStyle = '#fff';
      ctx.font = '12px sans-serif';
      ctx.fillText(`${track.name} ${p.hp}`, px + 9, py - 9); // canvas text is never parsed as HTML
    }
    if (t < end) requestAnimationFrame(draw);
  }
  requestAnimationFrame(draw);
}

play(new URLSearchParams(location.search).get('round'));
</script>
```

The linear search in `positionAt` is fine for a few hundred frames per track; use a binary search, or remember the last index per track, for long rounds. For a map with a lower level, pick the image by the `z` of the frame (table above).

**Security.** Every value that reaches SQL is a bound parameter (`?`), never concatenated; `roundId` is validated as digits only before use; an error answers a generic message and is logged server-side. In the viewer the player names are drawn with `fillText`, which never parses HTML: if you show names in the page itself instead of the canvas, set them with `textContent`, never `innerHTML`.

## 8. Contract versions

- `contract/stats/views.v1.json` (version 1): the views of 4.8. Frozen: they never change.
- `contract/stats/views.v2.json` (version 2): the views added in 4.9 (`v2_stats_*` repeat the v1 views with the new columns). Frozen since 4.9.0, like v1.
- A later change creates a new contract file and new views; existing views keep their columns. Always select columns by name (never `SELECT *`), so that your site keeps working when a newer plugin adds views.
- To know whether the database has the 4.9 views, check one of them: `SELECT COUNT(*) FROM information_schema.VIEWS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'v_stats_side_balance'` (1 = present; the read-only account only sees the views it was granted).
- The panel (`CS2-RetakeV4-Panel`) reads v1 only.
