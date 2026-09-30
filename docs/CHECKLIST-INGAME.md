# Checklist de test en jeu — RetakeV4

Chaque phase ajoute sa section. Cocher sur un serveur de test avant de passer à la phase suivante.
Préparation : retirer `plugins/CS2Retake/` (V3), lancer `pwsh -NoProfile -File scripts/package-dev.ps1`, copier `artifacts/dev/*` à la racine `csgo/` du serveur.

## Phase 1 — Fondations
- [ ] Au démarrage : log `RetakeV4 4.0.0-alpha.1 loaded with modules: Core`, aucune erreur.
- [ ] `configs/plugins/RetakeV4/core.json` est créé avec les valeurs par défaut.
- [ ] Log `RetakeV4 cvars loaded` à chaque changement de map.
- [ ] Mode compétitif : le warmup se termine seul (~16 s) avec le message « Fin du warmup, le retake commence ! » (client en `css_lang fr`).
- [ ] Avec `"Debug": true` dans `core.json` (puis restart map) : chaque round logue `PostRound -> Preparing`, `Preparing -> FreezeTime`, `FreezeTime -> Live`, `Live -> PostRound`, avec un numéro de round croissant.
- [ ] `mp_restartgame 1` en plein round : pas d'erreur, le numéro de round continue de croître.
- [ ] `css_retake_info` (console serveur et joueur) : `RetakeV4 v4.0.0-alpha.1 par NeuTroNBZh` (fr) / `by` (en).
- [ ] `css_plugins reload RetakeV4` en plein round : `mp_restartgame 1` est exécuté, le plugin repart sans erreur.
- [ ] `core.json` volontairement cassé (`{ "Debug": `) : warning `invalid JSON`, plugin chargé avec les valeurs par défaut, fichier non modifié.
- [ ] `core.json` avec `"ExecConfig": "../../server.cfg"` : warning `ExecConfig`, `RetakeV4/retake.cfg` exécuté à la place.

## Phase 2a — Types de round, spawns, équipes
Préparation : `pwsh -NoProfile -File scripts/package-dev.ps1`, copier `artifacts/dev/*` (le dossier `plugins/RetakeV4/spawns/` doit être présent).
- [ ] Log `RetakeV4 … loaded with modules: Core, RoundTypes, Teams, Spawns` puis `Loaded 52 spawns for de_mirage (legacy format: False)`.
- [ ] `roundtypes.json`, `teams.json`, `spawns.json` créés avec les valeurs par défaut.
- [ ] Après le warmup : scramble (message « Les équipes ont été mélangées »), un `mp_restartgame 1` automatique si des joueurs ont changé d'équipe, puis round 1 avec tout le monde dans sa nouvelle équipe et ratio respecté (ex. 5 joueurs → 3 CT / 2 T).
- [ ] Chaque round : message « Round Pistol — site A » ; rounds 1-3 Pistol, 4-6 Mid, puis FullBuy (avec `"Debug": true` dans `roundtypes.json`, le log indique `rounds played`).
- [ ] Les joueurs apparaissent sur des spawns retake du site annoncé, un T est sur un spawn en zone de plant, aucun joueur empilé.
- [ ] Victoire CT : les CT passent T, les T passent CT, message correspondant.
- [ ] Victoire T : équipes inchangées, message « … round(s) d'affilée » ; 5 victoires T de suite → scramble.
- [ ] Un joueur qui rejoint en cours de partie est mis en file (position annoncée) et entre à la fin du round.
- [ ] Un joueur `@css/vip` rejoint la file après un joueur normal : il passe devant.
- [ ] Changer d'équipe via le menu CT↔T est refusé avec message.
- [ ] Serveur plein (MaxPlayers atteint) : le suivant reste en file tant que personne ne part.
- [ ] Déconnexion d'un joueur en jeu : le round suivant rééquilibre.
- [ ] `css_retake_scramble` (admin) : message puis scramble à la fin du round ; sans permission : refus.
- [ ] Map sans fichier de spawns (ex. `de_basalt`) : warning, spawns CS2 par défaut, aucune erreur.
- [ ] `css_plugins reload RetakeV4` en plein match : les joueurs en T/CT restent en jeu, aucune erreur.
- [ ] Un joueur admis depuis la file apparaît bien dans son équipe au round suivant (entrée via `ChangeTeam`).
- [ ] Un joueur qui ne choisit pas d'équipe (auto-assign du moteur à l'expiration de `mp_force_pick_time`) est remis spectateur au freeze end, mis en file et prévenu de sa position.
- [ ] Un joueur en jeu qui passe spectateur (commande `spectate` ou menu) quitte la partie sans relancer le round ; le round suivant rééquilibre.
- [ ] Incohérence persistante : au plus un round relancé (« Les équipes étaient incohérentes »), jamais deux de suite.

## Phase 2b — Armes, plant, InstaDefuse
- [ ] Log `… loaded with modules: Core, RoundTypes, Teams, Spawns, Allocation, Plant, InstaDefuse` ; `grenades.json`, `allocation.json`, `plant.json`, `instadefuse.json` créés ; `roundtypes.json` (version 2) contient les pools d'armes. Un ancien `roundtypes.json` (version 1, phase 2a) affiche « has no weapon pools (pre-v2 file); using the built-in … definition » pour Pistol, Mid et FullBuy, et les rounds utilisent bien les armes V3.
- [ ] Round Pistol : T glock, CT usp-s, kevlar sans casque, couteau conservé ; environ 1 CT sur 3 a un kit et **au moins un** CT en a un.
- [ ] Round Mid : T mac-10 + deagle, CT mp9 + deagle, kevlar + casque ; tous les CT ont un kit.
- [ ] Round FullBuy : T ak-47 + deagle, CT m4a4 + deagle, kevlar + casque ; aucune AWP (pas encore de préférences, phase 3).
- [ ] Chaque joueur a un kit de grenades aléatoire cohérent avec son camp (molotov côté T, incendiaire côté CT) ; certains n'en ont aucune.
- [ ] Un couteau personnalisé / une baïonnette est conservé ; aucune C4 dans les inventaires (AutoPlant).
- [ ] AutoPlant : au freeze end, la bombe est posée sous les pieds du planteur (T en zone de plant), le compte à rebours de 40 s démarre, l'annonce « bombe posée » apparaît.
- [ ] `plant.json` en `FastPlant` : seul le planteur reçoit la C4, message central, le plant est instantané ; s'il ne pose pas en 5 s : message et victoire CT.
- [ ] InstaDefuse : tous les T morts, un CT commence à défuser → défuse immédiat et message « … a instadefuse avec Xs restantes ».
- [ ] Un T encore en vie → défuse normal, aucun message.
- [ ] HE ou molotov lancée juste avant le défuse → message de blocage correspondant, défuse normal.
- [ ] Molotov qui brûle près de la bombe → message « du feu brûle près de la bombe ».
- [ ] Défuse commencé avec moins de 10 s (sans kit) → message « il manque Xs » et la bombe explose immédiatement.
- [ ] Un CT qui survit à un round Pistol ne garde pas son kit : au round suivant, seuls les CT tirés au sort en ont un.

## Phase 3a — Préférences et persistance
- [ ] Premier démarrage : `plugins/RetakeV4/data/retakev4.db` est créé, aucun avertissement de base de données dans les logs.
- [ ] `!awp` : message « Tu es maintenant volontaire pour l'AWP… » ; avec au moins 5 joueurs, un volontaire de chaque camp reçoit parfois l'AWP en FullBuy (≈ 30 %).
- [ ] `!awp` à nouveau : message « Tu n'es plus volontaire » et plus d'AWP.
- [ ] Déconnexion puis reconnexion (ou changement de map) : le volontariat AWP est conservé.
- [ ] `css_retake_import_v3 <chemin>/CS2Retake/data/CommandAllocator/cs2retake.db` (console serveur ou admin root) : message « N préférence(s) V3 importée(s) » ; un joueur importé retrouve ses armes V3 (ex. M4A1-S en FullBuy CT) après reconnexion.
- [ ] Import avec un chemin faux : message « Échec de l'import V3 : … », aucun crash.
- [ ] `allocation.json` en `MySql` avec une chaîne valide : table `player_loadout` créée, préférences conservées entre deux redémarrages.
- [ ] Base indisponible (fichier en lecture seule ou MySQL arrêté) : un avertissement « Preference database unavailable… », les joueurs reçoivent l'équipement par défaut, aucune erreur en boucle.
