# RetakeV4 — Spécification de conception

- **Date** : 2026-09-30
- **Statut** : validée en brainstorming, en attente de relecture écrite
- **Remplace** : CS2RetakeV3 (CS2-RETAKE 3.1.0) et agora-retake 3.1.1

## 1. Contexte et objectifs

CS2RetakeV3 repose sur une base ancienne (fork de CS2Retake 2.x), avec des fichiers monolithiques (`CommandAllocator.cs` d'environ 1500 lignes dans agora), une allocation d'armes par menu chat, des types de round codés en dur et peu de tests. La V4 est une réécriture complète qui :

1. **conserve toutes les fonctionnalités de V3 et d'agora** : types de round, file d'attente, ratio, rotation, scramble, AutoPlant/FastPlant, allocation avec AWP, kits, Zeus et grenades, préférences persistées, InstaDefuse, commandes admin de spawns, correctif du warmup infini ;
2. **remplace les menus chat par un HUD modulaire** : menus en `point_worldtext`, avec sélection au viseur + clic et au clavier, et informations passives en HTML centré ;
3. **est modulaire** : une DLL, des modules internes indépendants, chacun avec sa config JSON et son interrupteur on/off ;
4. **est testable** : la logique métier pure est isolée de CounterStrikeSharp et couverte à 80 % ou plus ;
5. **apporte des nouveautés** :
   - éditeur de spawns en jeu (intégration de CS2-SpawnEditor) ;
   - file prioritaire VIP ;
   - traductions `lang/` par joueur ;
   - API publique pour les autres plugins ;
   - types de round définis en JSON ;
   - achat natif en mode configurable ;
   - base MySQL.

### Critères de succès

- Parité fonctionnelle avec V3 et agora, validée par la checklist de test manuel.
- Menu d'armes HUD utilisable pendant un freeze time de 3 s.
- Aucun crash serveur lié aux entités HUD (voir la leçon WriteEnterPVS d'Antibait).
- Couverture de `RetakeV4.Domain` ≥ 80 %, compilation à 0 warning.
- Un nouveau type de round, un widget HUD ou une langue s'ajoute sans modifier le cœur.

### Hors périmètre

- Détection AFK et limite de temps en file.
- Éjection d'un joueur en jeu par un VIP.
- Fusion de Breaker, Antibait, STATPLAY, AntiSlow ou AdminTools : ils restent séparés et peuvent se brancher sur l'API.
- Tout framework autre que CounterStrikeSharp.

## 2. Contraintes techniques

- CounterStrikeSharp **1.0.370+** (le serveur doit tourner en v1.0.369+), .NET 10, `[MinimumApiVersion(370)]`.
- Bases de données : SQLite (par défaut) et MySQL. PostgreSQL est abandonné.
- `Nullable` activé, `TreatWarningsAsErrors`.
- Fonctions de moins de 50 lignes, fichiers de moins de 800 lignes, imbrication de 4 niveaux au plus, objets de domaine immuables (records).

## 3. Architecture

### 3.1 Solution

```
CS2RetakeV4/
├─ RetakeV4.sln
├─ src/
│  ├─ RetakeV4.Contracts/   API publique (interfaces + records d'événements), aucune dépendance hormis CSSharp.API
│  ├─ RetakeV4.Domain/      logique pure, AUCUNE référence à CounterStrikeSharp
│  └─ RetakeV4/             plugin CSSharp : Bootstrap, Modules/, Hud/, Persistence/, Adapters/
├─ tools/RetakeV4.ConfigExporter/   génère les JSON par défaut pour le packaging
├─ tests/
│  ├─ RetakeV4.Domain.Tests/
│  └─ RetakeV4.Integration.Tests/
└─ docs/
```

Principe (architecture hexagonale) :
- **Domain** décide : qui joue, quel site, quelles armes, qui a l'AWP, si l'InstaDefuse est autorisé, etc.
- Les **modules** du plugin traduisent : événement CS2 → appel au Domain → action en jeu via les adaptateurs.
- Le Domain reçoit ses dépendances non déterministes par injection (`IRandom`, `IClock`).

### 3.2 Contrat de module

```csharp
public interface IRetakeModule
{
    string Name { get; }
    IReadOnlyList<string> DependsOn { get; }
    void Load(ModuleContext ctx);   // ctx : bus, config typée, localizer, logger, adaptateurs, services
    void Unload();
}
```

- Un module ne référence jamais un autre module : il passe uniquement par le bus d'événements ou par des interfaces du Domain et des services partagés.
- Le Bootstrap charge les modules activés dans l'ordre topologique de `DependsOn`. Si un module lève une exception au chargement, il est désactivé et l'erreur est journalisée ; le plugin continue.
- Chaque module a sa config `configs/plugins/RetakeV4/<module>.json` avec au minimum `Enabled` et `Debug`.

### 3.3 Modules

| Module | Responsabilités | Config |
|---|---|---|
| Core | Machine à états du round, cvars (`retake.cfg`), correctif du warmup infini (garde `WarmupEnd > 0` et timer de secours, repris d'agora), hot reload via `mp_restartgame 1` | `core.json` |
| RoundTypes | Chargement de `roundtypes.json`, séquence Sequence/Random/Specific, remise à zéro de la séquence sur changement de map ou de `mp_maxrounds` | `roundtypes.json` |
| Teams | File d'attente et priorités VIP, ratio, rotation, scramble, blocage de `jointeam`, exclusion des bots et du HLTV | `teams.json` |
| Spawns | Lecture et migration des spawns, choix du site, placement, forçage de site, éditeur en jeu | `spawns.json` |
| Plant | AutoPlant / FastPlant | `plant.json` |
| Allocation | Modes Menu/NativeBuy/Both, application des loadouts, menu d'armes, préférences | `allocation.json`, `grenades.json` |
| InstaDefuse | Défuse instantané, blocages, explosion forcée | `instadefuse.json` |
| Hud | Moteur de rendu, compositeur, widgets, thème | `hud.json` |
| Admin | Menu admin HUD, pont SimpleAdmin par réflexion (optionnel) | `admin.json` |
| Links | Commandes communautaires (`!discord`, `!site`, `!regles`…) définies en config | `links.json` |

Services partagés (qui ne sont pas des modules) : `EventBus`, `Localization`, `Persistence`, `Api`, `EntityGate`.

### 3.4 Cycle de vie du round (module Core)

```
Warmup → Preparing → FreezeTime → Live → PostRound → Preparing …
```

Ordre garanti des étapes pendant **Preparing** (round_start) ; chaque étape est un événement du bus :

1. `RoundTypeChosen` (RoundTypes)
2. `SiteChosen` (Spawns)
3. `TeamsVerified` (Teams : contrôle de cohérence ; le plan d'équipes a déjà été appliqué au PostRound précédent)
4. `PlayersPlaced` (Spawns)
5. `LoadoutsAssigned` (Allocation)
6. `RoundPrepared` (Core, événement public)

Si un module est désactivé, son étape est sautée : Core publie l'étape suivante sans attendre, avec des valeurs neutres (par exemple le spawn par défaut, ou pas d'allocation).

## 4. Moteur HUD

### 4.1 Rendu

- **`WorldTextRenderer`** (menus) :
  - Groupe d'entités `point_worldtext` par joueur : corps multiligne, ligne de surbrillance et pied de page d'aide.
  - Entités parentées au viewmodel du joueur. En secours, si le prototype invalide le parentage, elles sont repositionnées à chaque tick.
  - Visibilité réservée au propriétaire via `CheckTransmit`.
- **`CenterHtmlRenderer`** (informations passives) :
  - Un seul bloc HTML centré par joueur. Le **compositeur** fusionne les widgets actifs par priorité et rafraîchit à fréquence limitée (configurable).
  - Il se met en retrait pendant les messages centrés natifs importants.

### 4.2 Widgets

```csharp
public interface IHudWidget
{
    string Id { get; }
    HudZone Zone { get; }        // Menu | Center
    int Priority { get; }
    HudContent? Render(PlayerHudState state);   // null = rien à afficher
}
```

| Widget | Zone | Contenu |
|---|---|---|
| `RoundInfo` | Center | Site, type de round, CT contre T, série de victoires T |
| `QueueStatus` | Center | Position dans la file et priorité |
| `Alerts` | Center | InstaDefuse, raison d'un switch ou d'un scramble, AWP obtenue, avertissements admin |
| `WeaponMenu` | Menu | Construit par le module Allocation |
| `AdminMenu` | Menu | Construit par les modules Admin et Spawns |

Exemple de `hud.json` :

```json
{
  "Enabled": true,
  "Theme": { "Accent": "#4FC3F7", "Text": "#FFFFFF", "Muted": "#9E9E9E", "FontSize": 32 },
  "CenterRefreshMs": 250,
  "Widgets": {
    "RoundInfo":   { "Enabled": true, "ShowSeconds": 6 },
    "QueueStatus": { "Enabled": true },
    "Alerts":      { "Enabled": true, "ShowSeconds": 4 },
    "WeaponMenu":  { "Enabled": true, "Input": "AimAndKeys" }
  }
}
```

Tous les textes des widgets passent par le localizer.

### 4.3 Menus

- Un menu est un arbre immuable de nœuds `Action`, `Toggle`, `Submenu` et `Back`, construit par les modules. Le moteur n'a aucune connaissance du retake.
- La logique de navigation (`MenuNavigator` : curseur, pile de sous-menus, index visé) vit dans le Domain et est testée.

### 4.4 Entrées (`InputRouter`)

| Contexte | Navigation |
|---|---|
| Freeze time, mort ou spectateur | Viseur + clic gauche, W/S pour déplacer, E pour valider, touches 1 à 9 |
| Round en cours, joueur vivant | Touches 1 à 9 uniquement (menu compact) ; déplacements et tir intacts |

- **Sélection au viseur** : on mémorise l'angle de vue à l'ouverture du menu. L'écart de pitch donne l'index de la ligne visée, et au-delà d'un écart de yaw configurable on considère que le joueur ne vise pas le menu.
- **Clic** : tant que le joueur vise une ligne, le prochain tir de son arme est retardé, pour que le clic ne tire pas.
- Les touches 1 à 9 sont captées par des listeners de commandes `slot1`…`slot9`.

### 4.5 Sécurité des entités (`EntityGate`)

- Aucune création d'entité pendant les fenêtres dangereuses (nettoyage de début de round, scans d'entités). Les demandes sont mises en file et exécutées à l'ouverture de la fenêtre sûre.
- Les entités sont gérées par un **pool par joueur**, réutilisé plutôt que créé et détruit, puis recréé après le nettoyage de round de CS2.
- Un plafond configurable limite les entités HUD par joueur.

### 4.6 Risques à lever en phase 0

1. La précision de la sélection au viseur sur des lignes serrées.
2. La stabilité du parentage au viewmodel.
3. Le blocage fiable du tir pendant le clic.

Si le risque 1 échoue, la sélection au viseur est retirée : le clavier reste, et l'architecture ne change pas.

## 5. Allocation d'armes

### 5.1 Données

`roundtypes.json` contient une liste de `RoundTypeDefinition` :

```json
{
  "Name": "FullBuy",
  "Armor": "KevlarHelmet",
  "AllowPlayerChoice": true,
  "Primaries":   { "T": ["weapon_ak47", "..."], "CT": ["weapon_m4a1_silencer", "..."], "Any": ["weapon_ssg08", "..."] },
  "Secondaries": { "T": ["weapon_glock", "..."], "CT": ["weapon_usp_silencer", "..."], "Any": ["weapon_deagle", "..."] },
  "Defaults":    { "T": { "Primary": "weapon_ak47", "Secondary": "weapon_glock" },
                   "CT": { "Primary": "weapon_m4a1_silencer", "Secondary": "weapon_usp_silencer" } },
  "Awp":       { "Enabled": true, "MaxPerTeam": 1, "MinActivePlayers": 5, "Chances": [0, 30] },
  "DefuseKit": { "Mode": "All", "Quota": 1, "Chance": 100.0, "GuaranteeMinimum": false },
  "Zeus":      { "Enabled": false, "Chance": 20 },
  "GrenadePool": "FullBuyKits"
}
```

- Les types livrés par défaut sont Pistol, Mid et FullBuy, avec les pools et valeurs de V3 : Pistol en Kevlar avec un kit à 34,44 % et un minimum garanti, les autres en KevlarHelmet.
- La séquence fait référence aux types par leur nom : `[{ "RoundType": "Pistol", "Count": 3 }, { "RoundType": "Mid", "Count": 3 }, { "RoundType": "FullBuy", "Count": -1 }]`.
- `grenades.json` contient des pools de kits nommés et pondérés, avec une restriction d'équipe optionnelle. Il reprend les 16 kits de V3 par défaut.
- Le **`WeaponCatalog`** (Domain) recense chaque arme : identifiant, defindex, emplacement, équipes autorisées et clé de traduction. Il valide les configs et résout les achats natifs, en distinguant par exemple le M4A1-S du M4A4 par defindex.

### 5.2 Préférences

- Il y a une préférence par couple (SteamID, équipe, type de round) : `Primary`, `Secondary` et `AwpOptIn`.
- Une préférence invalide (arme absente du pool ou type de round supprimé) est remplacée par les `Defaults` du type de round.

### 5.3 Décision et application

- `LoadoutPlanner.Plan(RoundContext, IReadOnlyList<PlayerSlot>, PreferenceSnapshot, IRandom)` renvoie un `IReadOnlyDictionary<PlayerId, Loadout>` :
  - l'AWP est tirée parmi les volontaires, avec `MaxPerTeam` et un nombre de joueurs actifs d'au moins `MinActivePlayers` ;
  - les kits sont distribués selon le mode (`All`, `Quota`, `Chance`, et minimum garanti en pistol) ;
  - le Zeus et les grenades sont tirés au hasard selon le pool.
- `LoadoutApplier` (adaptateur) retire les armes, donne les nouvelles, l'armure et le kit, puis sélectionne l'emplacement, un appel de commande par emplacement.
- **Changement pendant le freeze time** : on recalcule le plan de ce seul joueur et on le réapplique immédiatement. L'AWP n'est accordée que si le quota de l'équipe le permet encore.
- **Changement en plein round** : il s'applique au round suivant.

### 5.4 Modes de sélection (`allocation.json` → `Mode`)

- **`Menu`** (par défaut) : menu HUD `WeaponMenu`.
  - Racine : « Round actuel (ton équipe) », « Autres configurations » (équipe × type de round où `AllowPlayerChoice`), « AWP : ON/OFF ».
  - Ouverture par `!guns` et ses alias V3 (`guns`, `gun`, `g`, `weapon(s)`, `menu`, `select`, `allocator`, `waffen` et les variantes mal orthographiées).
  - Ouverture automatique pendant le freeze time pour un nouveau joueur, ou quand le type de round change.
- **`NativeBuy`** : le composant isolé `NativeBuySelector` intercepte `buy` en Pre.
  - Une commande résolue par le `WeaponCatalog` met à jour la préférence, puis l'achat est bloqué.
  - Une commande numérique ou ambiguë laisse passer l'achat : l'arme est captée à `item_pickup` via son defindex, la préférence est mise à jour et l'objet est retiré (méthode d'agora).
  - Un achat ne donne jamais d'arme directement. Les cvars d'achat (`mp_buytime`, `mp_buy_anywhere`) sont réglées selon le mode.
- **`Both`** : les deux chemins alimentent la même préférence.
- Un message de rappel configurable (`HowToMessage`, intervalle en minutes) est conservé, avec un timer tué avant toute recréation.

## 6. Équipes et file d'attente

`TeamPlanner` (Domain) prend `TeamState` (CT, T, file ordonnée), `RoundOutcome` et `TeamRules`, et renvoie un `TeamPlan` contenant les mouvements et leurs raisons.

| Règle | Détail |
|---|---|
| Effectifs | Au plus `MaxPlayers` (9) joueurs en jeu ; nombre de T = `max(1, floor(n × TeamBalanceRatio))` (0.499), le reste en CT |
| Victoire CT | Les CT gagnants passent T, les T perdants passent CT, puis rééquilibrage |
| Victoire T | La série augmente et la file comble les places libres ; scramble après `ScrambleAfterTWins` (5) victoires |
| Scramble | À la fin du warmup, sur demande admin (appliqué en PostRound), et après la série de victoires T |
| File | Triée par priorité décroissante, puis par heure d'arrivée ; `PriorityFlags: [{ "Flag": "@css/vip", "Priority": 1 }]` |
| `jointeam` | Passage de CT à T (et l'inverse) refusé ; depuis spectateur, entrée en file ; vers spectateur, sortie du jeu ou de la file ; valeurs hors enum rejetées |
| Warmup | Équipes libres, puis scramble à la sortie |
| Exclusions | Bots et HLTV exclus de tous les comptages |

- Le plan est appliqué en **PostRound** via `SwitchTeam`, qui ne tue pas le joueur.
- Au freeze end, si une incohérence est détectée, le plan est recalculé. Si la correction est impossible, le round est terminé (`RestartOnInconsistency`, `true` par défaut).
- Raisons des mouvements : `SwitchedAfterCtWin`, `EnteredFromQueue`, `Scrambled`, `Balanced`, `QueuedServerFull`. Elles sont publiées sur le bus et affichées par `Alerts` et dans le chat, traduites.
- Les messages de changement d'équipe natifs sont masqués (`EventPlayerTeam.Silent`).

## 7. Spawns, éditeur et plant

### 7.1 Format

```json
{
  "SchemaVersion": 2,
  "Map": "de_mirage",
  "Spawns": [
    { "Id": "3f2c…", "Team": "CT", "Site": "A", "CanPlant": false,
      "Position": { "X": 0.0, "Y": 0.0, "Z": 0.0 },
      "Angle": { "Pitch": 0.0, "Yaw": 90.0, "Roll": 0.0 } }
  ]
}
```

- Le lecteur accepte aussi le **format V3 et SpawnEditor**, un tableau à plat `[{SpawnId, Team: 2|3, BombSite: 0|1, IsInBombZone, PositionX/Y/Z, QAngleX/Y/Z}]`. Il le convertit en mémoire (`IsInBombZone` devient `CanPlant`). La première sauvegarde écrit en V2 et crée une copie `<map>.json.v3.bak`.
- Les 11 maps sont livrées converties : ancient, ancient_night, anubis, cache, dust2, inferno, mirage, nuke, overpass, train et vertigo.

### 7.2 Choix du site et placement

- `SiteSelector` : tirage aléatoire, et `MaxSameSiteInRow` (0 = désactivé) évite les longues séries sur le même site. Un admin peut forcer un site pour un round (`Once`) ou jusqu'à annulation (`Sticky`).
- `SpawnSelector` :
  - un spawn distinct par joueur, dans son équipe et sur le site choisi ;
  - le planteur (un T tiré au hasard) reçoit un spawn `CanPlant` ;
  - s'il n'y a pas assez de spawns, on en réutilise un avec un décalage et un avertissement est journalisé.
- Si le fichier de la map est absent, le spawn CS2 par défaut est conservé, un avertissement est journalisé et un avertissement s'affiche dans le HUD des admins.

### 7.3 Éditeur en jeu

- Entrée par `!retake edit` ou par le menu admin (permission `@retakev4/admin`). Le round est mis en pause via `mp_warmup_start` et `mp_warmup_pausetimer 1`, puis l'état précédent est restauré à la sortie.
- **Marqueurs**, visibles uniquement par les éditeurs (`CheckTransmit`) :
  - piliers `CBeam`, T en rouge et CT en bleu, le site B se distinguant par sa transparence ;
  - étiquettes `point_worldtext` du type `[CT][A] #07` ;
  - anneau de 8 segments autour du spawn le plus proche.
  
  Tous passent par le pool et l'`EntityGate`.
- **Menu admin** : ajouter ici (équipe, site, CanPlant), supprimer ou modifier le spawn le plus proche, se téléporter au spawn n°, lister, sauvegarder, recharger, noclip. Une confirmation est demandée à la sortie s'il y a des modifications non sauvegardées.
- Les commandes console équivalentes sont conservées : `css_retake_addspawn`, `_delspawn`, `_tpspawn`, `_savespawns`, `_reloadspawns`, `_teleport`.
- La logique pure (spawn le plus proche, validation, sessions d'édition) est reprise de CS2-SpawnEditor dans le Domain.

### 7.4 Plant

- **AutoPlant** (par défaut) : au freeze end, la C4 est posée à la position du planteur. `bomb_beginplant` est bloqué.
- **FastPlant** : seul le planteur reçoit la C4. Sans plant au bout de `PlantCheckSeconds` (5 s), la bombe est posée automatiquement à sa position.
- `BombPlanted` (site, planteur) est publié sur le bus.
- Les timers existants sont tués avant d'en recréer.

## 8. InstaDefuse

- `InstaDefusePolicy.Evaluate(InstaDefuseState)` renvoie `Allow`, `Block(reason)` ou `ForceExplode`.
  - `Block` : T encore vivant (si `RequireNoTAlive`), HE en vol (`BlockOnHe`), molotov ou incendiaire en vol (`BlockOnMolotov`), ou feu actif à moins de `InfernoDistance` (250) de la bombe (`BlockOnInferno`).
  - `ForceExplode` : temps restant inférieur au temps de défuse (avec ou sans kit), si `ForceExplodeIfNoTime`.
- L'adaptateur suit `grenade_thrown`, `hegrenade_detonate`, `molotov_detonate`, `inferno_startburn/extinguish/expire`, `bomb_begindefuse`, `bomb_defused` et `bomb_exploded`. Il n'est actif qu'en état `Live` hors warmup.
- Les décisions sont publiées sur le bus, puis affichées par `Alerts` et dans le chat si `ChatNotification`.

## 9. Persistance

- `IPreferenceRepository` : `LoadAsync(steamId)`, `UpsertAsync(preference)`, `ImportAsync(IEnumerable<preference>)`.
- Implémentations : `SqlitePreferenceRepository` (par défaut, fichier sous `ModuleDirectory/data/`), `MySqlPreferenceRepository` et `NoOpPreferenceRepository`.
- Table :
  ```sql
  player_loadout(steam_id BIGINT, team TINYINT, round_type VARCHAR(64),
                 primary_weapon VARCHAR(64) NULL, secondary_weapon VARCHAR(64) NULL,
                 awp_opt_in BOOLEAN NOT NULL DEFAULT 0, updated_at DATETIME,
                 PRIMARY KEY (steam_id, team, round_type))
  ```
- Migrations versionnées via une table `schema_version`, requêtes toujours paramétrées, UPSERT natif.
- **Écritures** : un seul écrivain en arrière-plan (`Channel<T>`), jamais sur le thread de jeu. **Lectures** : asynchrones à la connexion, puis application au cache via `Server.NextFrame`, avec le joueur retrouvé par SteamID et ignoré s'il est parti.
- **Panne** : bascule sur NoOp (cache mémoire seul), avertissement journalisé, puis nouvelle tentative de connexion avec un délai croissant.
- **Import V3** : `css_retake_import_v3 <chemin sqlite>` lit les tables V3 filtrées par équipe. Les lignes avec `Team=0` (bug historique de V3) sont ignorées. Les types Pistol, Mid et FullBuy sont associés par nom.

## 10. Traductions

- Localizer natif de CSSharp : dossier `lang/`, langue par joueur, clés `module.section.key` et balises de couleur (modèle CS2-AntiSlow).
- Langues livrées : `en` et `fr`. En ajouter une consiste à déposer un fichier.
- Aucun texte visible par un joueur n'est codé en dur ; les logs serveur restent en anglais.

## 11. API publique

- `RetakeV4.Contracts`, exposé via `PluginCapability<IRetakeApi>("retakev4:api")`. La DLL est installée dans `addons/counterstrikesharp/shared/RetakeV4.Contracts/`.

```csharp
public interface IRetakeApi
{
    int ApiVersion { get; }
    RetakeState State { get; }
    BombSite? CurrentSite { get; }
    string? CurrentRoundType { get; }
    int? GetQueuePosition(ulong steamId);

    void ForceSite(BombSite site, ForceSiteMode mode);
    void RequestScramble();

    event Action<RoundPreparedEvent> RoundPrepared;
    event Action<BombPlantedEvent> BombPlanted;
    event Action<LoadoutAssignedEvent> LoadoutAssigned;
    event Action<LastPlayerAliveEvent> LastPlayerAlive;
    event Action<RoundEndedEvent> RoundEnded;
    event Action<PlayerQueuedEvent> PlayerQueued;
}
```

- Les événements sont des records immuables déclenchés sur le thread de jeu. Une exception chez un abonné est interceptée et journalisée, sans jamais interrompre le retake.
- Consommateurs visés : CS2-STATPLAY (étiquetage des rounds), CS2-Antibait (`LastPlayerAlive`), CS2-BreakerAndOpenDoor (`RoundPrepared`).

## 12. Commandes et permissions

| Commande | Permission | Rôle |
|---|---|---|
| `!guns` et alias | — | Menu d'armes |
| `css_retake_info` | — | Version |
| `!retake` | `@retakev4/admin` | Menu admin HUD |
| `!retake edit` | `@retakev4/admin` | Éditeur de spawns |
| `css_retake_scramble` | `@retakev4/admin` | Scramble à la fin du round |
| `css_retake_forcesite <A\|B\|off> [sticky]` | `@retakev4/admin` | Forçage du site |
| `css_retake_addspawn`, `_delspawn`, `_tpspawn`, `_savespawns`, `_reloadspawns`, `_teleport` | `@retakev4/admin` | Édition des spawns en console |
| `css_retake_import_v3 <chemin>` | `@retakev4/root` | Import des préférences V3 |
| Commandes définies dans `links.json` | — | Liens communautaires |

Le pont SimpleAdmin (réflexion, sans dépendance de compilation) ajoute les entrées Retake au menu SimpleAdmin s'il est présent.

## 13. Gestion des erreurs

- **Configs** : validation au chargement. Une valeur invalide est remplacée par sa valeur par défaut avec un avertissement (fichier, clé, raison). Une entrée invalide dans une liste est ignorée, le reste est conservé. Un avertissement signale une version de config plus ancienne.
- **Handlers** : chaque handler CSSharp et chaque abonné du bus est enveloppé ; une exception est journalisée avec le module et l'état du round. Au-delà de `MaxErrorsPerRound` (5) erreurs dans un round, le module est désactivé jusqu'au prochain rechargement.
- **Entités** : les vérifications de validité (`IsValid`, pawn, controller) se font uniquement dans les adaptateurs. Un joueur disparu entre la décision et l'application est ignoré.
- **Logs** : les contenus joueurs ne servent jamais de gabarit `ILogger`. Mode `Debug` par module.

## 14. Tests

- **`RetakeV4.Domain.Tests`** (xUnit, couverture ≥ 80 %) :
  - `LoadoutPlanner`, `TeamPlanner`, `RoundTypeSequence`, `SiteSelector`, `SpawnSelector`, `InstaDefusePolicy` ;
  - `WeaponCatalog` et résolution des achats natifs ;
  - validation des configs ;
  - migration des spawns (fixtures réelles V3) ;
  - `MenuNavigator` (arbre, index au viseur, entrées).
- **`RetakeV4.Integration.Tests`** :
  - dépôt SQLite en mémoire et migrations ;
  - MySQL, uniquement si `RETAKEV4_MYSQL_TEST` est défini, sinon test ignoré ;
  - bus et Bootstrap : ordre, module qui plante, module désactivé.
- **Test en jeu** : `docs/CHECKLIST-INGAME.md` couvre le HUD au viseur et au clavier, les modes d'achat, l'éditeur, le hot reload, la panne de base de données, la rotation et la file VIP, InstaDefuse, et un changement de map.

## 15. Packaging et livraison

- Les valeurs par défaut vivent **uniquement dans les classes C#**. `tools/RetakeV4.ConfigExporter` les sérialise pour le zip « avec configs », et le plugin écrit les JSON manquants au premier démarrage.
- Arborescence livrée :
  ```
  addons/counterstrikesharp/plugins/RetakeV4/          (RetakeV4.dll, dépendances, lang/, spawns/)
  addons/counterstrikesharp/shared/RetakeV4.Contracts/
  addons/counterstrikesharp/configs/plugins/RetakeV4/  (un JSON par module)
  cfg/RetakeV4/retake.cfg
  ```
- Zips : `RetakeV4-x.y.z.zip` et `RetakeV4-x.y.z-no-configs.zip`.
- GitHub Actions : build, tests et couverture à chaque push ; release sur un tag `v*`.
- Dépôt git local dans `CS2RetakeV4/`. La publication sur GitHub sera décidée plus tard.

## 16. Migration depuis V3

- V3 et V4 ne cohabitent pas : il faut retirer `plugins/CS2Retake/`.
- Les spawns sont relus tels quels, puis migrés à la première sauvegarde.
- Les préférences se récupèrent avec `css_retake_import_v3`.
- La permission `@cs2retake/admin` devient `@retakev4/admin` : il faut mettre à jour `admins.json` ou SimpleAdmin. Les commandes V3 `css_retakeXxx` deviennent `css_retake_xxx`.
- `docs/MIGRATION-V3.md` contient la table de correspondance entre chaque clé de config V3 et son équivalent V4.

## 17. Phases

| Phase | Contenu | Livrable |
|---|---|---|
| 0 | Prototype HUD jetable : `point_worldtext`, viseur + clic, viewmodel, blocage du tir | Go/no-go sur la sélection au viseur, notes techniques |
| 1 | Solution, Domain, bus, Bootstrap, configs, localisation, états du round dans Core, correctif warmup | Le plugin charge et suit les rounds |
| 2 | RoundTypes, Teams (file et VIP), Spawns (lecture et migration), Plant, Allocation par défaut, InstaDefuse | Parité avec V3, sans les menus |
| 3 | Moteur HUD, widgets, menu d'armes, SQLite/MySQL, import V3 | Expérience joueur complète |
| 4 | Menu admin, éditeur de spawns, forçage de site, pont SimpleAdmin, Links | Administration en jeu |
| 5 | NativeBuy/Both, API publique, CI et release, documentation, `MIGRATION-V3.md` | Version 4.0.0 |

Chaque phase fera l'objet de son propre plan d'implémentation, en commençant par les phases 0 et 1.
