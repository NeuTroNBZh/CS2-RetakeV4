# Migrer de CS2RetakeV3 vers RetakeV4

## Étapes
1. Arrêter le serveur et **retirer** `addons/counterstrikesharp/plugins/CS2Retake/` : V3 et V4 ne cohabitent pas.
2. Installer RetakeV4 (voir le README).
3. **Spawns** : copier vos fichiers `CS2Retake/spawns/<map>.json` dans `plugins/RetakeV4/spawns/` (les 11 maps officielles sont déjà fournies). Le format V3 est lu tel quel ; la première sauvegarde depuis l'éditeur écrit le format V4 et garde une copie `<map>.json.v3.bak`.
4. **Préférences d'armes** : avec le serveur démarré, `css_retake_import_v3 <chemin>/CS2Retake/data/CommandAllocator/cs2retake.db` (console serveur ou admin `@retakev4/root`). Base SQLite V3 uniquement.
5. **Permissions** : remplacer `@cs2retake/admin` par `@retakev4/admin` dans `admins.json` ou dans CS2-SimpleAdmin.
6. **Commandes** : `css_retakeXxx` devient `css_retake_xxx` (tableau ci-dessous).
7. Reporter vos réglages dans les nouveaux fichiers de config (tableau ci-dessous).

## Commandes
| V3 | V4 |
|---|---|
| `css_retakeinfo` | `css_retake_info` |
| `css_retakespawn <index>` | `css_retake_tpspawn <numéro>` (numérotation à partir de 1) |
| `css_retakewrite` | `css_retake_savespawns` |
| `css_retakeread` | `css_retake_reloadspawns` |
| `css_retakescramble` | `css_retake_scramble` |
| `css_retaketeleport x y z` | `css_retake_teleport x y z` |
| `css_retakeaddspawn <2\|3> <0\|1>` | `css_retake_addspawn <T\|CT> <A\|B> [plant]` (formes V3 acceptées), dans l'éditeur |
| `!guns` et alias | inchangés |

## Clés de configuration
### `CS2Retake.json`
| Clé V3 | V4 |
|---|---|
| `PlantType` | `plant.json` → `Mode` |
| `SecondsUntilBombPlantedCheck` | `plant.json` → `PlantCheckSeconds` |
| `RoundTypeMode` | `roundtypes.json` → `Mode` |
| `RoundTypeSequence` | `roundtypes.json` → `Sequence` |
| `RoundTypeSpecific` | `roundtypes.json` → `Specific` |
| `Allocator` | `allocation.json` → `Mode` (`Menu`, `NativeBuy` ou `Both`) |
| `MaxPlayers` | `teams.json` → `MaxPlayers` |
| `TeamBalanceRatio` | `teams.json` → `TeamBalanceRatio` |
| `EnableScramble`, `ScrambleAfterSubsequentTerroristRoundWins` | `teams.json` → `ScrambleAfterTWins` (0 = désactivé) |
| `EnableSwitchOnRoundWin` | `teams.json` → `SwitchTeamsOnCtWin` |
| `EnableQueue` | supprimé : la file d'attente est toujours active |
| `SpotAnnouncerEnabled` | supprimé : le site est annoncé dans le chat et le HUD (`hud.json` → `Widgets.RoundInfo`) |
| `EnableThankYouMessage` | supprimé |
| `MessageLanguage` | supprimé : langue par joueur (`!lang`), textes dans `lang/*.json` |
| `InstaDefuseEnabled` | `instadefuse.json` → `Enabled` |
| `InstaDefuseRequireNoTAlive`, `InstaDefuseBlockOnHe`, `InstaDefuseBlockOnMolotov`, `InstaDefuseBlockOnInferno`, `InstaDefuseInfernoDistance`, `InstaDefuseForceExplodeIfNoTime`, `InstaDefuseChatNotification` | `instadefuse.json` → même nom sans le préfixe `InstaDefuse` |
| `EnableDebug` | `Debug` dans chaque fichier de module |

### Allocateur V3 (`CommandAllocator`, `FullBuy`, `Mid`, `Pistol`)
| Clé V3 | V4 |
|---|---|
| `EnableRoundTypePistolMenu`, `…MidMenu`, `…FullBuyMenu` | supprimé : un type de round apparaît dans `!guns` s'il offre au moins deux armes au choix |
| `DefuseKitMode`, `DefuseKitQuota`, `DefuseKitChance` | `roundtypes.json` → `RoundTypes[].DefuseKit` (`Mode`, `Quota`, `Chance`) |
| `PistolDefuseKitChance`, `PistolDefuseKitGuaranteeMinimum` | type `Pistol` → `DefuseKit.Chance`, `DefuseKit.GuaranteeMinimum` |
| `EnableZeus`, `ZeusChance` | `roundtypes.json` → `RoundTypes[].Zeus` (`Enabled`, `Chance`) |
| `DatabaseType`, `ConnectionString` | `allocation.json` → `Database.Type` (`Sqlite`, `MySql`, `None`), `Database.MySqlConnectionString`, `Database.SqliteFile` — PostgreSQL n'est plus géré |
| `HowToMessageDelayInMinutes` | `allocation.json` → `HowToIntervalMinutes` (entier, 0 = désactivé) |
| `HowToMessage` | `lang/*.json` → `allocation.howto.menu` / `.native` / `.both` |
| listes d'armes `FullBuy`, `Mid`, `Pistol` | `roundtypes.json` → `RoundTypes[].Primaries`, `Secondaries` (`T`, `CT`, `Any`) et `Defaults` |
| AWP (chance, nombre) | `roundtypes.json` → `RoundTypes[].Awp` (`Enabled`, `MaxPerTeam`, `MinActivePlayers`, `Chance`) |
