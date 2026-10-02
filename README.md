<div align="center">

# RetakeV4

**A complete, modular Retake game mode for Counter-Strike 2, built on CounterStrikeSharp.**

[![Release](https://img.shields.io/github/v/release/NeuTroNBZh/CS2-RetakeV4?style=flat-square)](https://github.com/NeuTroNBZh/CS2-RetakeV4/releases/latest)
[![CI](https://img.shields.io/github/actions/workflow/status/NeuTroNBZh/CS2-RetakeV4/ci.yml?branch=main&style=flat-square&label=build)](https://github.com/NeuTroNBZh/CS2-RetakeV4/actions)
[![CounterStrikeSharp](https://img.shields.io/badge/CounterStrikeSharp-1.0.370%2B-orange?style=flat-square)](https://github.com/roflmuffin/CounterStrikeSharp)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)

</div>

Every round, the Terrorists defend a bomb that is already planted on a site and the Counter-Terrorists have to retake it. RetakeV4 runs the whole mode: teams and queue, spawns, weapons, bomb plant, defuse, HUD, map rotation and administration.

It is a full rewrite of CS2RetakeV3: every feature is a module that can be turned off, everything is configured in JSON, and the game logic is covered by more than a thousand automated tests.

## Highlights

- **Fair rounds**: Pistol, Mid and Full-buy rounds, balanced T/CT ratio, team rotation on CT wins, scramble after a T win streak, VIP priority in the queue.
- **Weapons players keep**: choose once with `!guns`, remembered per team and round type; optional buy-menu selection and AWP volunteering. Preferences are stored in SQLite or MySQL.
- **Clean HUD**: a center panel with the round type, site and player counts; menus in the center of the screen, floating in the world, or in the chat.
- **Map rotation**: an end-of-match vote over every map that has Retake spawns, plus `!rtv`.
- **Map cleanup**: doors opened, windows and vents broken every round, nothing else touched; per-map corrections from an in-game editor.
- **Admin tools**: in-game spawn editor, forced site, scramble, and a Retake category in the CS2-SimpleAdmin menu.
- **For server owners**: text overrides, timed announcements, community commands (`!discord`, `!rules`), and a config checker shipped with every release.
- **For developers**: a public API to react to rounds from other plugins, and a companion [web panel](https://github.com/NeuTroNBZh/CS2-RetakeV4-Panel).

## Contents

1. [How a round plays](#how-a-round-plays)
2. [Installation](#installation)
3. [Updating and migrating from V3](#updating-and-migrating-from-v3)
4. [Player commands](#player-commands)
5. [Administration](#administration)
6. [Map vote](#map-vote)
7. [Map cleanup](#map-cleanup)
8. [Configuration](#configuration)
9. [Customising your server](#customising-your-server)
10. [Weapon preferences and database](#weapon-preferences-and-database)
11. [Web panel](#web-panel)
12. [Plugin API](#plugin-api)
13. [Troubleshooting](#troubleshooting)
14. [Development](#development)
15. [License](#license)

---

## How a round plays

1. **Round end**: teams are rebuilt. Queued players join (VIPs first), the T/CT ratio is respected, teams rotate when the CTs win and are scrambled after a T win streak.
2. **Preparation**: the plugin picks the **round type** (by default 3 Pistol, 3 Mid, then Full-buy), the **site** (A or B, without long streaks on the same one) and a **spawn** on that site for every player.
3. **Freeze time**: everyone gets their preferred weapons plus armour, defuse kits, Zeus and grenades according to the round type. The HUD shows the round type, the site and the player counts.
4. **Bomb**: planted automatically on the site (AutoPlant) or instantly by a Terrorist (FastPlant).
5. **Defuse**: InstaDefuse defuses immediately once no Terrorist is alive and no grenade or fire threatens the defuser; if there is not enough time left, the bomb explodes.

---

## Installation

**Requirements**: a CS2 server with [Metamod:Source](https://www.sourcemm.net/downloads.php?branch=dev) and [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/releases) **1.0.370 or newer**.

1. Download the latest version from [Releases](https://github.com/NeuTroNBZh/CS2-RetakeV4/releases/latest):
   - `RetakeV4-x.y.z.zip` for a first install (default configs included);
   - `RetakeV4-x.y.z-no-configs.zip` to update without touching your configs.
2. Extract the archive into the server's `game/csgo/` folder:
   ```
   game/csgo/
   ├── addons/counterstrikesharp/plugins/RetakeV4/          plugin, spawns, texts
   ├── addons/counterstrikesharp/shared/RetakeV4.Contracts/ public API
   ├── addons/counterstrikesharp/configs/plugins/RetakeV4/  one JSON file per module
   └── cfg/RetakeV4/retake.cfg                              Retake cvars
   ```
3. Start or restart the server on a competitive map (`de_dust2`, `de_mirage`…). Missing config files are created automatically.
4. Check in the console: `css_plugins list` lists `RetakeV4`, and `css_retake_info` prints the version.

**Maps with bundled spawns**: ancient, ancient_night, anubis, cache, dust2, inferno, mirage, nuke, overpass, train, vertigo. Any other map works once you create its spawns with the [in-game editor](#spawn-editor).

**Game server hosts (Dathost and others)**: nothing to set up. The plugin applies `retake.cfg` again on the first round of every map, after the host's competitive configs that would otherwise override it (bots, warmup, timings).

---

## Updating and migrating from V3

- **Updating**: replace `plugins/RetakeV4/` with the content of the `no-configs` archive and restart. Your configs are kept; new options take their default value.
- **From CS2RetakeV3**: follow [docs/MIGRATION-V3.md](docs/MIGRATION-V3.md) (French). In short, V3 spawns are read as they are, settings map one to one, and players' weapon preferences are imported with `css_retake_import_v3 <path to cs2retake.db>`.

---

## Player commands

| Command | Effect |
|---|---|
| `!guns` (also `!gun`, `!g`, `!weapons`, `!menu`) | Open the weapon menu |
| `!awp` | Volunteer (or stop volunteering) for the AWP in your current team |
| Forward / back, then Use (E) | Move through a center menu and select |
| `!1`, `!2`… | Select in a chat menu (map vote, or every menu when the server uses chat menus) |
| `!rtv` | Ask for a map change (a vote opens once enough players asked) |
| `!mapvote` | Reopen the current map vote menu |
| `!nextmap` | Show the next map |

- **Weapons**: for each team and round type you pick a primary and a pistol. The choice is saved and reused every round of that type; a choice made during freeze time applies immediately.
- **AWP**: volunteering is per team, in the menu of every round type that hands out AWPs (Full-buy by default). Each such round, one AWP per team goes to a random volunteer.
- **Buy menu**: when the server enables it, "buying" a weapon in the CS2 buy menu sets it as your preference. Nothing is actually bought.
- **Language**: messages follow your CounterStrikeSharp language (English and French included).

---

## Administration

Admin commands need the `@retakev4/admin` permission (`@retakev4/root` for the V3 import). `@css/root` grants both. Give them through CounterStrikeSharp's `admins.json` or CS2-SimpleAdmin.

| Command | Effect |
|---|---|
| `!retake` | Admin menu: spawn editor, forced site, scramble, map cleanup |
| `!retake edit`, `css_retake_edit [save\|discard\|exit]` | Enter or leave the spawn editor |
| `!retake cleanup` | Open the map cleanup editor |
| `css_retake_forcesite <A\|B\|off> [once\|sticky]` | Force the next site, or every following one |
| `css_retake_scramble` | Scramble the teams at round end |
| `css_retake_addspawn <T\|CT> <A\|B> [plant]` | Add a spawn at your position |
| `css_retake_delspawn` | Delete the nearest spawn |
| `css_retake_tpspawn <n>`, `css_retake_teleport <x> <y> <z>` | Teleport |
| `css_retake_savespawns`, `css_retake_reloadspawns` | Save or reload the current map's spawns |
| `css_retake_import_v3 <path>` | Import V3 weapon preferences |
| `css_retake_info` | Plugin version |

### Spawn editor

1. Type `!retake edit`. Noclip is enabled and every spawn of the map is shown, coloured by team and site.
2. Use the editor menu (or the commands above) to add, delete or teleport. The nearest spawn is highlighted.
3. **Save** writes `plugins/RetakeV4/spawns/<map>.json` and keeps the previous file as `.bak`. **Discard** reloads the last saved version.

A T spawn marked *plant* can carry the bomb; AutoPlant needs at least one per site.

### Remote control (RCON)

Three commands are reserved for the server console and RCON (a player typing them is refused). They let remote tools such as a Stream Deck drive the server:

| Command | Effect |
|---|---|
| `css_retake_state` | Prints one line `RETAKE_STATE {json}` (version 2): map, phase, warmup, paused, round, max rounds, score, site, round type, players (`userId`, `steamId`, `name`, `team`, `alive`, `bot`, `health`), queue size, map vote (`open`, `nextMap`), open editors, forced site (`force.site`, `force.sticky`), pending scramble, `clock.timeLeft` (seconds left in a live round) and `clock.bomb` (`none`, `planted`, `defused`) |
| `css_retake_mapvote` | Opens the map vote now, like a successful `!rtv`: the voted map is played from the end of the round (refused if a vote is open or decided, or with fewer than two maps) |
| `css_retake_cleanup` | Replays the map cleanup now and prints how many entities were handled (refused while an editor is open) |

### CS2-SimpleAdmin

With [CS2-SimpleAdmin](https://github.com/daffyyyy/CS2-SimpleAdmin) installed, a **Retake** category appears in its `!admin` menu with the same actions (`admin.json` → `SimpleAdminBridge`).

---

## Map vote

The MapVote module rotates maps. The maps on offer are every map with Retake spawns (`plugins/RetakeV4/spawns/<map>.json`) that the server knows, except the current map and `ExcludedMaps`. A map added with the spawn editor joins the vote on its own.

- **End of match**: when `TriggerRoundsBeforeEnd` rounds are left before `mp_maxrounds` (3 by default), a vote opens in the chat for every player, spectators included, for `VoteSeconds`. `!mapvote` reopens the menu to change one's vote. The most voted map wins; a tie, or no vote at all, is settled by a random draw. When the match ends, the map changes after `ChangeDelaySeconds`.
- **`!rtv`**: once `RtvPercentage` % of the players typed it (at least `RtvMinPlayers` players, after `RtvMinRounds` rounds), a vote opens immediately and the map changes at the end of the round.
- The vote always uses the chat menu (`!1`, `!2`…), even when other menus are in the center of the screen: center menus are driven by the movement keys and would get in the way of a live round.

---

## Map cleanup

Every round, right after the bomb plant, the MapCleanup module opens doors and breaks windows and vents; at the end of freeze time it repeats the action on targets that are still there. Nothing else is ever touched:

- only `func_door`, `func_door_rotating`, `prop_door_rotating`, `func_breakable`, `func_shatterglass` and breakable `prop_dynamic` (with health) entities are read;
- only the `Open` (doors) and `Break` (windows, vents) inputs are sent;
- a breakable is a window only if its model contains `glass` or `window`, a vent only if it contains `vent` or `grate`; anything else is left alone;
- nothing happens during warmup or while the spawn editor is open.

**Correcting a map**: `!retake cleanup` (or **Map cleanup** in the admin menu). Look at the object and select the first line; the panel shows its model and class (if nothing is in your line of sight, the nearest object within 600 units is taken). Choose **Door**, **Window**, **Vent**, **Leave it alone** or **Automatic**, then **Save**. Corrections are stored in `configs/plugins/RetakeV4/mapcleanup/<map>.json` (previous version kept as `.bak`) and apply from the next round. One admin edits at a time and automatic cleanup is paused meanwhile; the editor closes on **Leave**, when another menu opens, on disconnect, on map change or after 3 minutes without action. Set `"Debug": true` in `mapcleanup.json` to log every pass and every selection.

---

## Configuration

Each module has its own file in `addons/counterstrikesharp/configs/plugins/RetakeV4/`. All of them have `Enabled` (turn the module off) and `Debug` (verbose logs). An invalid value falls back to its default with a console warning; an unreadable JSON file is ignored as a whole (defaults apply) and never overwritten.

| File | What it controls | Main defaults |
|---|---|---|
| `core.json` | cfg executed on every map, forced end of a stuck warmup | `RetakeV4/retake.cfg` |
| `roundtypes.json` | round types: weapons per team, default weapons, armour, AWP, kits, Zeus, grenades; round order | Pistol ×3, Mid ×3, then Full-buy; Zeus 100 % |
| `teams.json` | max players, T/CT ratio, scramble, rotation, VIP priorities | 9 players, ratio 0.499, scramble after 5 T wins |
| `spawns.json` | max consecutive rounds on the same site | `0` (no limit) |
| `allocation.json` | database, allocation mode (`Menu`, `NativeBuy`, `Both`), auto-open menu, `!guns` reminder | SQLite, `Menu`, reminder every 3.5 min |
| `grenades.json` | grenade kits per pool and team | — |
| `plant.json` | `AutoPlant` or `FastPlant` | `AutoPlant` |
| `instadefuse.json` | InstaDefuse conditions | on, blocked by HE, molotov and fire |
| `hud.json` | theme (colours, team colours), widgets, menu display | center panel |
| `admin.json` | CS2-SimpleAdmin bridge | on |
| `links.json` | community commands (`!discord` → one line, `!rules` → several lines) | — |
| `announcements.json` | timed messages (optionally per map) and welcome message | off (empty lists) |
| `mapvote.json` | `TriggerRoundsBeforeEnd` (1–10), `VoteSeconds` (10–120), `ChangeDelaySeconds` (3–30), `RtvEnabled`, `RtvPercentage` (1–100), `RtvMinPlayers`, `RtvMinRounds`, `ExcludedMaps` | vote 3 rounds before the end, `!rtv` at 60 % |
| `mapcleanup.json` | `OpenDoors`, `DoorOpenChancePercent` (0–100), `BreakWindows`, `BreakVents`, `MaxEntitiesPerRound` (1–4096), `FreezeEndCheck` | everything on, doors opened 100 % |
| `remote.json` | console commands for remote tools (`css_retake_state`…) | on |
| `api.json` | public API | on |

### Menu display

`hud.json` → `Menu.Display` sets how menus (weapons, admin, editors) are shown:

- `CenterHtml` (default): a large panel in the center of the screen (title, coloured T / CT sections, chosen weapon ticked, key hints). Forward / back to move, Use (E) to select, at any time. Long lists scroll (`Menu.CenterVisibleLines`, 6 by default). The player cannot move while the menu is open, like WeaponPaints menus (`Menu.FreezeWhileOpen`, on by default).
- `WorldText`: the menu floats in front of the player; aim at a line and shoot, or forward / back and Use outside rounds.
- `Chat`: a numbered list in the chat, as in V3; select with `!1`, `!2`…

The setting applies on server restart or plugin reload. The map vote always uses the chat, whatever this setting.

---

## Customising your server

Everything below lives in `addons/counterstrikesharp/configs/plugins/RetakeV4/` and is never overwritten by an update.

**Texts.** The shipped texts are in `plugins/RetakeV4/lang/en.json` and `fr.json` and are replaced on every update, so do not edit them. Instead create `lang/en.json` (and `lang/fr.json` for French players) next to the configs with only the keys you want to change. CounterStrikeSharp colour tags (`{green}`, `{red}`…) are supported.

```json
{
  "core.prefix": "{default}[{gold}MyServer{default}]",
  "core.prefix_alert": "{default}[{red}!{default} {gold}MyServer{default}]",
  "core.prefix_help": "{default}[{lightblue}?{default} {gold}MyServer{default}]"
}
```

`core.prefix` precedes normal messages, `core.prefix_alert` refusals and errors, `core.prefix_help` command answers. An override may keep or drop the `{0}`, `{1}`… placeholders of the original text but cannot add new ones. Unknown or invalid keys are ignored with a warning at startup.

**Announcements** (`announcements.json`): a random message every `IntervalSeconds` seconds (30 minimum), never the same one twice in a row; a map listed in `MapMessages` uses its own list. `Welcome` is sent once per connection, when the player joins a team.

```json
{
  "Version": 1,
  "IntervalSeconds": 420,
  "Messages": ["Join our Discord: !discord", "Type !guns to pick your weapons"],
  "MapMessages": { "de_mirage": ["Welcome to Mirage!"] },
  "Welcome": "Welcome! Type !guns to pick your weapons."
}
```

**Community commands** (`links.json`): `Message` for one line, `Lines` for several (shown with the help prefix).

```json
{
  "Version": 1,
  "Links": [
    { "Commands": ["discord", "dis"], "Message": "Discord: {lightblue}https://discord.gg/xxxx{default}" },
    { "Commands": ["rules", "regles"], "Lines": ["1. Respect everyone", "2. No cheating"] }
  ]
}
```

**Check your configuration** before restarting: every release ships `RetakeV4-<version>-configcheck.zip`. With .NET 10:

```bash
dotnet RetakeV4.ConfigCheck.dll <configs/plugins/RetakeV4> [<plugins/RetakeV4/spawns>]
```

It lists every problem (invalid value, unknown text key, unreadable spawn file) or prints `Configuration OK`.

---

## Weapon preferences and database

Player choices are stored in a database set in `allocation.json` → `Database`:

| `Type` | Use |
|---|---|
| `Sqlite` (default) | Local file `plugins/RetakeV4/data/retakev4.db`, nothing to install. Needs Linux with glibc 2.28 or newer. |
| `MySql` | Shared database (several servers, [web panel](#web-panel)). Set `MySqlConnectionString`, for example `Server=127.0.0.1;Port=3306;Database=retakev4;User ID=retake;Password=...`. Tables are created automatically. |
| `None` | Nothing saved (default weapons on every connection). |

The database is never queried on the game thread: a slow or unavailable database does not make the server lag. During an outage, players keep their choices in memory and the plugin retries on its own.

---

## Web panel

[CS2-RetakeV4-Panel](https://github.com/NeuTroNBZh/CS2-RetakeV4-Panel) is a website where players sign in with Steam and pick their weapons with the mouse. It runs in Docker (see its README).

To connect it:

1. Use MySQL: `allocation.json` → `Database.Type` = `MySql`.
2. Give the panel access to the same database.
3. If several servers share the database, give each one its own `Database.ServerKey` and tell the panel which one to show.

The plugin publishes the weapons on offer in the `retake_catalog` table, and a choice made on the website applies on the next round, without reconnecting.

---

## Plugin API

Reference `RetakeV4.Contracts.dll` without copying it into your plugin (it is already in `shared/`):

```csharp
using RetakeV4.Contracts;

public override void OnAllPluginsLoaded(bool hotReload)
{
    IRetakeApi? retake;
    try
    {
        retake = RetakeApi.Capability.Get();
    }
    catch (KeyNotFoundException)
    {
        return; // RetakeV4 is not installed
    }
    if (retake is null) return; // Api module disabled

    retake.RoundPrepared += e => Logger.LogInformation("Round {Round}: {Type} on {Site}", e.RoundNumber, e.RoundType, e.Site);
    retake.LastPlayerAlive += e => Logger.LogInformation("Clutch {Team}: slot {Slot}", e.Team, e.Player.Slot);
}
```

- **Events**: `RoundPrepared`, `BombPlanted`, `LoadoutAssigned`, `LastPlayerAlive`, `RoundEnded`, `PlayerQueued`.
- **Actions**: `ForceSite`, `RequestScramble`.
- Use everything from the game thread, where events are raised. An exception in your handler is logged and never stops the Retake.
- After `css_plugins reload RetakeV4`, call `Get()` again.
- The API only grows by additions, and `RetakeApi.Version` increases with each one.

---

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Bots appear or warmup never ends | The host's competitive config ran after `retake.cfg`. The plugin applies it again on the first round; check that `core.json` → `ExecConfig` points to `RetakeV4/retake.cfg`. |
| `GLIBC_2.xx not found` at startup | The host system is too old for SQLite: switch to `MySql` in `allocation.json`. |
| No spawns on a map | The map has no bundled spawns: create them with `!retake edit`. |
| No map vote at the end of the match | `mp_maxrounds` is 0, or fewer than two other maps have spawns (see the console warning). |
| A module does not load | Read the console at startup: a failing module is disabled on its own and the rest keeps running. `Debug: true` in its JSON gives more detail. |
| A player is kicked (`NETWORK_DISCONNECT_OVERFLOW`) on the first round | The server froze for more than ~450 ms. The plugin warms its code up at startup to prevent it; if it persists, the `Slow handler` console lines name the slow module. |

To report a bug, open an [issue](https://github.com/NeuTroNBZh/CS2-RetakeV4/issues) with the version (`css_retake_info`) and the relevant console lines.

---

## Development

- Requirements: .NET 10 SDK.
- Build: `dotnet build RetakeV4.sln -c Release` (no warning allowed).
- Tests: `dotnet test RetakeV4.sln`.
- Test server package: `pwsh scripts/package-dev.ps1` (output in `artifacts/dev/`).
- Release: push a `vx.y.z` tag; GitHub Actions builds and publishes the archives.
- Architecture: all game logic lives in `src/RetakeV4.Domain` (no game dependency, unit tested); `src/RetakeV4` only holds thin CounterStrikeSharp adapters, one folder per module. The shared database formats used by the web panel are pinned in [`contract/`](contract).

---

## License

Released under the [MIT License](LICENSE): you may use, modify and redistribute the plugin freely, keeping the copyright notice.

Made by [NeuTroNBZh](https://github.com/NeuTroNBZh).
