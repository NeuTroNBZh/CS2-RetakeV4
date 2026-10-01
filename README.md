# RetakeV4

Plugin **Retake** pour Counter-Strike 2, basé sur [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

Chaque round, les terroristes défendent une bombe déjà posée sur un site et les antiterroristes doivent la reprendre. Le plugin s'occupe de tout : équipes, file d'attente, spawns, armes, pose de la bombe, désamorçage, HUD et administration.

RetakeV4 est une réécriture complète de CS2RetakeV3. Il est modulaire (chaque fonctionnalité peut être désactivée), entièrement configurable en JSON et testé automatiquement.

- **Joueurs** : armes choisies une fois puis retenues, menu `!guns` en jeu ou [panel web](#panel-web).
- **Admins** : éditeur de spawns en jeu, menu `!retake`, intégration CS2-SimpleAdmin.
- **Développeurs** : [API publique](#api-pour-les-autres-plugins) pour réagir aux rounds depuis un autre plugin.

---

## Sommaire

1. [Comment se déroule un round](#comment-se-déroule-un-round)
2. [Installation](#installation)
3. [Mise à jour et migration depuis la V3](#mise-à-jour-et-migration-depuis-la-v3)
4. [Pour les joueurs](#pour-les-joueurs)
5. [Pour les admins](#pour-les-admins)
6. [Configuration](#configuration)
7. [Préférences d'armes et base de données](#préférences-darmes-et-base-de-données)
8. [Panel web](#panel-web)
9. [API pour les autres plugins](#api-pour-les-autres-plugins)
10. [Dépannage](#dépannage)
11. [Développement](#développement)

---

## Comment se déroule un round

1. **Fin du round précédent** : les équipes sont recalculées. Les joueurs en file d'attente entrent dans la partie (priorité aux VIP), le ratio T/CT est respecté, les équipes tournent quand les CT gagnent et sont mélangées après une série de victoires T.
2. **Préparation** : le plugin choisit le **type de round** (par défaut 3 rounds *Pistol*, 3 rounds *Mid*, puis *FullBuy*), le **site** (A ou B, sans longue série sur le même) et place chaque joueur sur un **spawn** de ce site.
3. **Freeze time** : chacun reçoit ses armes selon ses préférences, plus armure, kit de désamorçage, Zeus et grenades selon le type de round. Le HUD affiche le type de round, le site et le nombre de joueurs.
4. **Bombe** : elle est posée automatiquement sur le site (AutoPlant) ou par un terroriste en un instant (FastPlant).
5. **Désamorçage** : l'InstaDefuse désamorce instantanément quand plus aucun T n'est en vie et qu'aucune grenade ni aucun feu ne menace le désamorceur ; si le temps manque, la bombe explose.

---

## Installation

**Prérequis** : un serveur CS2 avec [Metamod:Source](https://www.sourcemm.net/downloads.php?branch=dev) et [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/releases) **1.0.370 ou plus récent**.

1. Téléchargez la dernière version sur la page [Releases](https://github.com/NeuTroNBZh/CS2-RetakeV4/releases) :
   - `RetakeV4-x.y.z.zip` pour une première installation (configs par défaut incluses) ;
   - `RetakeV4-x.y.z-no-configs.zip` pour une mise à jour sans toucher vos configs.
2. Décompressez l'archive dans le dossier `game/csgo/` du serveur. Vous devez obtenir :
   ```
   game/csgo/
   ├── addons/counterstrikesharp/plugins/RetakeV4/          (plugin, spawns, textes)
   ├── addons/counterstrikesharp/shared/RetakeV4.Contracts/ (API publique)
   ├── addons/counterstrikesharp/configs/plugins/RetakeV4/  (un fichier JSON par module)
   └── cfg/RetakeV4/retake.cfg                              (cvars du mode retake)
   ```
3. Démarrez ou redémarrez le serveur sur une map compétitive (`de_dust2`, `de_mirage`…). Les fichiers de config manquants sont créés automatiquement.
4. Vérifiez en console : `css_plugins list` doit afficher `RetakeV4`, et `css_retake_info` donne la version.

**Maps fournies** : ancient, ancient_night, anubis, cache, dust2, inferno, mirage, nuke, overpass, train, vertigo. Pour une autre map, créez les spawns avec l'[éditeur en jeu](#éditeur-de-spawns).

**Hébergeurs (Dathost, etc.)** : vous n'avez rien à configurer. Le plugin réapplique `retake.cfg` au premier round de chaque map, après les configs du mode compétitif qui l'écrasaient (bots, échauffement, timings).

---

## Mise à jour et migration depuis la V3

- **Mise à jour** : remplacez le contenu de `plugins/RetakeV4/` par celui de l'archive `no-configs`, puis redémarrez. Vos configs sont conservées ; les nouvelles options prennent leur valeur par défaut.
- **Depuis CS2RetakeV3** : suivez [docs/MIGRATION-V3.md](docs/MIGRATION-V3.md). En résumé :
  - les spawns V3 sont lus tels quels ;
  - les réglages correspondent un à un (le guide donne le tableau de correspondance) ;
  - les préférences d'armes des joueurs s'importent avec `css_retake_import_v3 <chemin vers cs2retake.db>`.

---

## Pour les joueurs

| Commande | Effet |
|---|---|
| `!guns` (alias : `!gun`, `!g`, `!weapons`, `!menu`…) | Ouvre le menu d'armes |
| `!awp` | Se porter volontaire (ou non) pour l'AWP |
| Touches `1` à `9`, ou viser une ligne et tirer | Choisir dans un menu HUD |
| `!1`, `!2`… | Choisir dans un menu de chat (si le serveur utilise ce mode) |

- **Choix des armes** : pour chaque équipe et chaque type de round, vous choisissez votre arme principale et votre pistolet. Le choix est enregistré et réutilisé à chaque round du même type. Un choix fait pendant le freeze time s'applique tout de suite.
- **AWP** : à chaque round qui en distribue, une AWP par équipe est tirée au sort parmi les volontaires.
- **Achat CS2** : si le serveur l'active, ouvrir le menu d'achat de CS2 et « acheter » une arme revient à la choisir comme préférence. Rien n'est réellement acheté.
- **Langue** : les messages s'affichent en français ou en anglais selon votre langue CounterStrikeSharp.

---

## Pour les admins

Les commandes admin demandent la permission `@retakev4/admin` (et `@retakev4/root` pour l'import V3), à donner via `admins.json` de CounterStrikeSharp ou CS2-SimpleAdmin.

| Commande | Effet |
|---|---|
| `!retake` | Menu admin : éditeur de spawns, forçage du site, scramble |
| `!retake edit` ou `css_retake_edit [save\|discard\|exit]` | Entrer ou sortir de l'éditeur de spawns |
| `css_retake_forcesite <A\|B\|off> [once\|sticky]` | Forcer le prochain site, ou tous les suivants |
| `css_retake_scramble` | Mélanger les équipes à la fin du round |
| `css_retake_addspawn <T\|CT> <A\|B> [plant]` | Ajouter un spawn à votre position |
| `css_retake_delspawn` | Supprimer le spawn le plus proche |
| `css_retake_tpspawn <n>`, `css_retake_teleport <x> <y> <z>` | Se téléporter |
| `css_retake_savespawns`, `css_retake_reloadspawns` | Enregistrer ou recharger les spawns de la map |
| `css_retake_import_v3 <chemin>` | Importer les préférences d'armes de la V3 |
| `css_retake_info` | Version du plugin |

### Éditeur de spawns

1. Tapez `!retake edit`. Le noclip est activé et tous les spawns de la map apparaissent, colorés par équipe et par site.
2. Utilisez le menu de l'éditeur (ou les commandes ci-dessus) pour ajouter, supprimer ou vous téléporter. Le spawn le plus proche est mis en évidence.
3. **Enregistrer** écrit `plugins/RetakeV4/spawns/<map>.json` et garde une copie `.bak` de l'ancien fichier. **Annuler** recharge la dernière version enregistrée.

Un spawn T marqué *plant* peut porter la bombe ; il en faut au moins un par site pour l'AutoPlant.

### CS2-SimpleAdmin

Si [CS2-SimpleAdmin](https://github.com/daffyyyy/CS2-SimpleAdmin) est installé, une catégorie **Retake** apparaît dans son menu `!admin` avec les mêmes actions (`admin.json` → `SimpleAdminBridge`).

---

## Configuration

Chaque module a son fichier dans `addons/counterstrikesharp/configs/plugins/RetakeV4/`. Tous contiennent `Enabled` (désactiver le module) et `Debug` (logs détaillés). Une valeur invalide est remplacée par sa valeur par défaut, avec un avertissement dans la console. Un fichier JSON illisible est ignoré en entier (valeurs par défaut) sans être écrasé.

| Fichier | Ce qu'on y règle | Valeurs par défaut principales |
|---|---|---|
| `core.json` | cfg exécutée à chaque map, fin forcée d'un échauffement bloqué | `RetakeV4/retake.cfg` |
| `roundtypes.json` | types de round : armes proposées par équipe, armes par défaut, armure, AWP, kits, Zeus, grenades ; ordre des rounds | Pistol ×3, Mid ×3, puis FullBuy ; Zeus à 100 % |
| `teams.json` | joueurs max, ratio T/CT, scramble, rotation, priorités VIP | 9 joueurs, ratio 0,499, scramble après 5 victoires T |
| `spawns.json` | nombre max du même site d'affilée | `0` (pas de limite) |
| `allocation.json` | base de données, mode d'attribution (`Menu`, `NativeBuy`, `Both`), ouverture auto du menu, rappel `!guns` | SQLite, `Menu`, rappel toutes les 3,5 min |
| `grenades.json` | kits de grenades par pool et par équipe | — |
| `plant.json` | `AutoPlant` ou `FastPlant` | `AutoPlant` |
| `instadefuse.json` | conditions de l'InstaDefuse | activé, bloqué par HE, molotov et feu |
| `hud.json` | thème, widgets, affichage des menus | menus devant le joueur |
| `admin.json` | pont CS2-SimpleAdmin | activé |
| `links.json` | commandes communautaires (`!discord` → message) | — |
| `api.json` | API publique | activée |

### Menus : HUD ou chat

Dans `hud.json`, `Menu.Display` choisit comment les menus (armes, admin, éditeur) s'affichent :

- `WorldText` (par défaut) : le menu flotte devant le joueur. On choisit avec les touches `1`-`9` ou en visant une ligne et en tirant.
- `Chat` : liste numérotée dans le chat, comme en V3. On choisit avec `!1`, `!2`…

Ce réglage est pris en compte au redémarrage du serveur ou au rechargement du plugin.

### Textes

Tous les messages sont dans `plugins/RetakeV4/lang/en.json` et `fr.json`. Les codes couleur CounterStrikeSharp (`{green}`, `{red}`…) sont acceptés.

---

## Préférences d'armes et base de données

Les choix des joueurs sont enregistrés dans une base, réglée dans `allocation.json` → `Database` :

| `Type` | Usage |
|---|---|
| `Sqlite` (défaut) | Fichier local `plugins/RetakeV4/data/retakev4.db`, rien à installer. Nécessite Linux avec glibc 2.28 ou plus récente. |
| `MySql` | Base partagée (plusieurs serveurs, [panel web](#panel-web)). Renseignez `MySqlConnectionString`, par exemple `Server=127.0.0.1;Port=3306;Database=retakev4;User ID=retake;Password=...`. Les tables sont créées automatiquement. |
| `None` | Aucune sauvegarde (armes par défaut à chaque connexion). |

La base n'est jamais interrogée sur le thread de jeu : une base lente ou en panne ne fait pas laguer le serveur. En cas de panne, les joueurs gardent leurs choix en mémoire et le plugin réessaie automatiquement.

---

## Panel web

[CS2-RetakeV4-Panel](https://github.com/NeuTroNBZh/CS2-RetakeV4-Panel) est un site où les joueurs se connectent avec Steam et choisissent leurs armes à la souris. Il s'installe en Docker (voir son README).

Pour le relier au plugin :

1. Utilisez MySQL : `allocation.json` → `Database.Type` = `MySql`.
2. Donnez au panel l'accès à la même base.
3. Si plusieurs serveurs partagent la base, donnez à chacun un `Database.ServerKey` différent et indiquez au panel celui à afficher.

Le plugin publie la liste des armes proposées dans la table `retake_catalog`, et un choix fait sur le site s'applique au round suivant, sans reconnexion.

---

## API pour les autres plugins

Référencez `RetakeV4.Contracts.dll` sans la copier dans votre plugin (elle est déjà dans `shared/`) :

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
        return; // RetakeV4 n'est pas installé
    }
    if (retake is null) return; // module Api désactivé

    retake.RoundPrepared += e => Logger.LogInformation("Round {Round} : {Type} sur {Site}", e.RoundNumber, e.RoundType, e.Site);
    retake.LastPlayerAlive += e => Logger.LogInformation("Clutch {Team} : slot {Slot}", e.Team, e.Player.Slot);
}
```

- **Événements** : `RoundPrepared`, `BombPlanted`, `LoadoutAssigned`, `LastPlayerAlive`, `RoundEnded`, `PlayerQueued`.
- **Actions** : `ForceSite`, `RequestScramble`.
- Tout s'utilise depuis le thread de jeu, où arrivent les événements. Une exception dans votre abonné est journalisée et n'interrompt pas le retake.
- Après `css_plugins reload RetakeV4`, rappelez `Get()`.
- L'API n'évolue que par ajouts, et `RetakeApi.Version` augmente à chaque ajout.

---

## Dépannage

| Symptôme | Cause et solution |
|---|---|
| Des bots apparaissent ou l'échauffement ne finit pas | La config compétitive de l'hébergeur est passée après `retake.cfg`. Le plugin la réapplique au premier round ; vérifiez que `core.json` → `ExecConfig` pointe bien sur `RetakeV4/retake.cfg`. |
| `GLIBC_2.xx not found` au démarrage | Le système de l'hôte est trop ancien pour SQLite : passez en `MySql` dans `allocation.json`. |
| Aucun spawn sur une map | La map n'est pas fournie : créez ses spawns avec `!retake edit`. |
| Un module ne se charge pas | Regardez la console au démarrage : chaque module en erreur est désactivé seul et le reste continue. `Debug: true` dans son JSON donne plus de détails. |
| Un joueur est kické (`NETWORK_DISCONNECT_OVERFLOW`) au premier round | Le serveur a gelé plus de ~450 ms. Le plugin précharge son code au démarrage pour l'éviter. Si ça persiste, les lignes `Slow handler` de la console indiquent quel module est lent. |

Pour signaler un bug : ouvrez une [issue](https://github.com/NeuTroNBZh/CS2-RetakeV4/issues) avec la version (`css_retake_info`) et les lignes de console concernées.

---

## Développement

- Prérequis : .NET 10 SDK.
- Build : `dotnet build RetakeV4.sln -c Release` (aucun avertissement toléré).
- Tests : `dotnet test RetakeV4.sln`.
- Package de test serveur : `pwsh scripts/package-dev.ps1` (résultat dans `artifacts/dev/`).
- Release : poussez un tag `vx.y.z` ; GitHub Actions construit et publie les archives.
- Architecture et règles : [CLAUDE.md](CLAUDE.md). Toute la logique métier est dans `src/RetakeV4.Domain` (sans dépendance au jeu, testée), et `src/RetakeV4` ne contient que les adaptateurs CounterStrikeSharp.
- Tests en jeu avant une release : [docs/CHECKLIST-INGAME.md](docs/CHECKLIST-INGAME.md).

---

## Licence

Distribué sous licence [MIT](LICENSE) : vous pouvez utiliser, modifier et redistribuer le plugin librement, en conservant la mention de copyright.
