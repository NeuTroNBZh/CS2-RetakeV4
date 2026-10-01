# RetakeV4

Plugin de retake pour Counter-Strike 2 (CounterStrikeSharp), réécriture complète de CS2RetakeV3 : modulaire, configurable en JSON, avec un HUD en jeu.

## Installation
1. Prérequis : [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) **1.0.370** ou plus récent (API 370).
2. Télécharger `RetakeV4-x.y.z.zip` (configs par défaut incluses) ou `RetakeV4-x.y.z-no-configs.zip` (pour une mise à jour sans toucher vos configs) depuis les releases.
3. Décompresser à la racine du serveur (`game/csgo/`) : `addons/counterstrikesharp/plugins/RetakeV4/`, `addons/counterstrikesharp/shared/RetakeV4.Contracts/`, `addons/counterstrikesharp/configs/plugins/RetakeV4/`, `cfg/RetakeV4/retake.cfg`.
4. Redémarrer le serveur. Les configs manquantes sont créées au premier démarrage.

Depuis V3 : voir [docs/MIGRATION-V3.md](docs/MIGRATION-V3.md).

## Fonctionnalités
- Rounds : types de round (Pistol, Mid, FullBuy…) définis dans `roundtypes.json`, en séquence, aléatoires ou fixes ; site choisi au hasard sans longue série, forçage admin.
- Équipes : file d'attente avec priorités VIP, ratio T/CT, rotation après une victoire CT, scramble après une série de victoires T, blocage du changement d'équipe.
- Armes : menu HUD `!guns` (au viseur ou au clavier) ou menu d'achat CS2, préférences sauvegardées (SQLite ou MySQL), AWP pour les volontaires, kits, Zeus, kits de grenades par camp.
- Plant : AutoPlant ou FastPlant. InstaDefuse avec blocages (HE, molotov, feu) et explosion forcée.
- HUD : bloc d'informations (round, file d'attente, alertes) et menus `point_worldtext` configurables (`hud.json`) ; ou menus de chat numérotés comme en V3 (`Menu.Display = Chat`).
- Administration : menu `!retake`, éditeur de spawns en jeu, forçage du site, scramble, intégration CS2-SimpleAdmin, commandes communautaires (`links.json`).
- API publique pour les autres plugins (`RetakeV4.Contracts`).

## Configuration
Un fichier par module dans `addons/counterstrikesharp/configs/plugins/RetakeV4/` (chacun a `Enabled` et `Debug`) :

| Fichier | Contenu |
|---|---|
| `core.json` | cfg exécutée, correctif du warmup infini |
| `roundtypes.json` | types de round, armes, AWP, kits, Zeus, grenades, séquence |
| `teams.json` | joueurs max, ratio, scramble, rotation, priorités VIP |
| `spawns.json` | `MaxSameSiteInRow` |
| `allocation.json` | base de données, mode (`Menu`/`NativeBuy`/`Both`), rappel, ouverture auto du menu |
| `grenades.json` | kits de grenades par pool |
| `plant.json` | `AutoPlant`/`FastPlant` |
| `instadefuse.json` | règles de l'InstaDefuse |
| `hud.json` | thème, widgets, menus : `Menu.Display` = `WorldText` (menus devant le joueur, défaut) ou `Chat` (menus de chat, choix avec `!1`, `!2`…), orientation, distance, entrée |
| `admin.json` | pont CS2-SimpleAdmin |
| `links.json` | commandes communautaires |
| `api.json` | API publique |

Une valeur invalide est remplacée par sa valeur par défaut avec un avertissement dans les logs. SQLite (par défaut) fonctionne sur les hôtes Linux avec glibc 2.28 ou plus récente ; sinon utiliser MySQL (`Database.Type`). Textes : `plugins/RetakeV4/lang/en.json` et `fr.json`.

## Commandes
| Commande | Permission | Rôle |
|---|---|---|
| `!guns` (et alias V3 : `!gun`, `!g`, `!weapons`…) | — | Menu d'armes |
| `!awp` | — | Volontaire AWP oui/non |
| `css_retake_info` | — | Version |
| `!retake`, `!retake edit` | `@retakev4/admin` | Menu admin, éditeur de spawns |
| `css_retake_edit [save\|discard\|exit]` | `@retakev4/admin` | Éditeur de spawns |
| `css_retake_forcesite <A\|B\|off> [once\|sticky]` | `@retakev4/admin` | Forçage du site |
| `css_retake_scramble` | `@retakev4/admin` | Scramble à la fin du round |
| `css_retake_addspawn`, `_delspawn`, `_tpspawn`, `_teleport`, `_savespawns`, `_reloadspawns` | `@retakev4/admin` | Spawns en console |
| `css_retake_import_v3 <chemin>` | `@retakev4/root` | Import des préférences V3 |
| commandes de `links.json` | — | Liens communautaires |

## API pour les autres plugins
Référencer `RetakeV4.Contracts.dll` (sans la copier : elle est dans `shared/`) :
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
        return; // RetakeV4 is not installed on this server
    }
    if (retake is null) return; // Api module disabled
    retake.LastPlayerAlive += e => Logger.LogInformation("Clutch for {Team}: slot {Slot}", e.Team, e.Player.Slot);
    retake.RoundPrepared += e => Logger.LogInformation("Round {Round}: {Type} on {Site}", e.RoundNumber, e.RoundType, e.Site);
}
```
Événements : `RoundPrepared`, `BombPlanted`, `LoadoutAssigned`, `LastPlayerAlive`, `RoundEnded`, `PlayerQueued`. Actions : `ForceSite`, `RequestScramble`. Tous les membres s'utilisent depuis le thread de jeu, où arrivent aussi les événements ; une exception dans un abonné est journalisée et n'arrête pas le retake. Après un rechargement de RetakeV4 (`css_plugins reload RetakeV4`), rappeler `Get()` : l'ancienne instance ne déclenche plus d'événements. Évolutions de l'API : uniquement par ajouts (nouvelles propriétés, événements ou interfaces), `RetakeApi.Version` augmente à chaque ajout.

## Panel web

Le panel web [CS2-RetakeV4-Panel](https://github.com/NeuTroNBZh/CS2-RetakeV4-Panel) permet aux joueurs de régler leurs armes depuis un navigateur (connexion Steam). Il partage la base MySQL du plugin :

- `Database.Type` doit valoir `MySql` dans `allocation.json` (le panel ne lit pas SQLite) ;
- `Database.ServerKey` (défaut `default`) identifie ce serveur dans la table `retake_catalog`, où le plugin publie les armes proposées à chaque chargement des types de round ;
- un choix fait sur le panel s'applique au round suivant, sans reconnexion.

Le format partagé est figé dans `contract/` (voir `contract/README.md`).

## Développement
- Build : `dotnet build RetakeV4.sln -c Release` ; tests : `dotnet test RetakeV4.sln`.
- Package de test : `pwsh scripts/package-dev.ps1` ; release : `pwsh scripts/package-release.ps1 -Version x.y.z` (fait par GitHub Actions sur un tag `vx.y.z`).
- Tests en jeu : `docs/CHECKLIST-INGAME.md`.
