# Checklist de test en jeu — RetakeV4

Chaque phase ajoute sa section. Cocher sur un serveur de test avant de passer à la phase suivante.
Préparation : retirer `plugins/CS2Retake/` (V3), lancer `pwsh -NoProfile -File scripts/package-dev.ps1`, copier `artifacts/dev/*` à la racine `csgo/` du serveur.

## Phase 1 — Fondations
- [ ] Au démarrage : log `RetakeV4 <version> loaded with modules: …` (liste complète des modules), aucune erreur.
- [ ] `configs/plugins/RetakeV4/core.json` est créé avec les valeurs par défaut.
- [ ] Log `RetakeV4 cvars loaded` à chaque changement de map.
- [ ] Mode compétitif : le warmup se termine seul (~16 s) avec le message « Fin du warmup, le retake commence ! » (client en `css_lang fr`).
- [ ] Avec `"Debug": true` dans `core.json` (puis restart map) : chaque round logue `PostRound -> Preparing`, `Preparing -> FreezeTime`, `FreezeTime -> Live`, `Live -> PostRound`, avec un numéro de round croissant.
- [ ] `mp_restartgame 1` en plein round : pas d'erreur, le numéro de round continue de croître.
- [ ] `css_retake_info` (console serveur et joueur) : `RetakeV4 v<version> par NeuTroNBZh` (fr) / `by` (en).
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

## Phase 3b — HUD et menu d'armes
- [ ] Premier démarrage : `hud.json` est créé, aucun avertissement de config.
- [ ] Début de round : le bloc centré affiche « <type> - site <A/B> » et « CT n contre n T » pendant ~6 s ; la série de victoires T apparaît quand elle existe.
- [ ] Joueur en file d'attente : « File d'attente : position/total » visible en continu, « Accès prioritaire » pour un VIP.
- [ ] Avec `Menu.Display = WorldText` : `!guns` (et un alias, ex. `!gun`) ouvre le menu face au joueur, lisible (sinon essayer `Menu.Orientation` 1 puis 2).
- [ ] Le menu suit le joueur sans tremblement gênant (sinon tester `Menu.FollowMode` = `Parent`).
- [ ] WorldText, freeze time : viser une ligne la met en surbrillance ; clic gauche = sélection, sans tirer ; avancer/reculer déplacent le curseur, E valide. Précision : < 2 erreurs sur 20 essais. (CS2 n'envoie pas les touches 1-9 au serveur.)
- [ ] WorldText, round live, vivant : le menu n'intercepte rien (viser/cliquer tire, avancer/reculer/E bougent et interagissent normalement).
- [ ] Un second joueur ne voit pas le menu du premier.
- [ ] Choisir une arme principale pendant le freeze time : l'arme est remplacée immédiatement, alerte « Armes mises à jour. » ; kit, grenades et AWP éventuelle conservés.
- [ ] Choisir une arme en round live ou pour une autre configuration : alerte « Enregistré, utilisé dès le prochain round. », appliqué au round suivant.
- [ ] Les sections Terroristes / Antiterroristes listent les types de round qui offrent un choix ; un pistol round ne propose que le pistolet.
- [ ] AWP : l'interrupteur n'apparaît que dans les types de round qui distribuent l'AWP (FullBuy par défaut), séparément pour T et CT ; ON/OFF persiste après reconnexion ; `!awp` bascule l'équipe actuelle ; recevoir l'AWP affiche « Tu as l'AWP ce round. ».
- [ ] Nouveau joueur : le menu s'ouvre seul au freeze time, puis se ferme au début du round live ; il se rouvre quand le type de round change.
- [ ] Menu ouvert pendant la fin de round / le restart : aucune erreur console, le menu réapparaît au round suivant.
- [ ] Mort ou spectateur : le menu s'ouvre et se pilote au viseur.
- [ ] Déconnexion avec le menu ouvert : aucune entité orpheline (`ent_find point_worldtext`).
- [ ] Perf : 2+ menus ouverts, aucune chute de fps notable.
- [ ] `hud.json` → `Menu.Input` = `Keys` : la visée ne sélectionne plus, le clavier fonctionne.
- [ ] `plant.json` en `FastPlant` : le poseur change d'arme pendant le freeze time et garde la C4 (pas de victoire CT forcée).
- [ ] `ent_remove` d'une ligne de menu : le menu se reconstruit, le module Hud n'est pas désactivé.

## Phase 4a — Éditeur de spawns et forçage de site
- [ ] `css_retake_edit` sans la permission `@retakev4/admin` : refusé.
- [ ] `css_retake_edit` (admin) : le jeu passe en warmup en pause, le menu « Éditeur de spawns (N) » s'ouvre, piliers rouges (T) et bleus (CT), site B translucide, étiquettes `[CT][A] #07` lisibles de tous les côtés.
- [ ] Un joueur qui n'édite pas ne voit aucun marqueur ; deux éditeurs voient chacun seulement leur propre anneau jaune.
- [ ] L'anneau jaune suit le spawn le plus proche (< 150 unités) ; l'entrée « Le plus proche » du menu se met à jour.
- [ ] Ajouter un spawn T A (C4) ici, modifier équipe / site / C4 du plus proche, le supprimer : marqueurs et titre (`*`) à jour.
- [ ] « Aller à un spawn » (liste paginée) et `css_retake_tpspawn 3` téléportent ; `css_retake_teleport 0 0 0` aussi.
- [ ] Noclip via le menu et via la touche `noclip` (sans `sv_cheats`) ; il revient après le restart du warmup.
- [ ] « Quitter » avec des modifications : sous-menu « Sauvegarder et quitter / Quitter sans sauvegarder ». Sans sauvegarde, le fichier est rechargé.
- [ ] Sauvegarde : `spawns/<map>.json` réécrit en format V2, `<map>.json.bak` créé ; sur un fichier V3, `<map>.json.v3.bak` aussi.
- [ ] Le dernier éditeur qui sort relance le jeu (fin du warmup si le jeu n'était pas en warmup avant) ; le watchdog de warmup ne coupe jamais l'édition.
- [ ] Commandes console V3 : `css_retake_addspawn 2 0`, `css_retake_addspawn CT B`, `css_retake_delspawn`, `css_retake_savespawns`, `css_retake_reloadspawns` depuis la console serveur (save/reload) et en jeu.
- [ ] Déconnexion du dernier éditeur avec des modifications non sauvegardées : le jeu reprend avec le fichier sauvegardé, aucun marqueur orphelin (`ent_find beam`).
- [ ] `css_retake_forcesite B` : le round suivant est sur B, puis tirage normal ; `css_retake_forcesite A sticky` : tous les rounds sur A jusqu'à `css_retake_forcesite off`.
- [ ] `css_retake_forcesite B` sur une map sans spawn B : refusé.
- [ ] Map sans fichier de spawns : chaque admin reçoit l'alerte HUD « Aucun spawn pour <map> » à chaque round.
- [ ] `css_retake_addspawn` / `css_retake_delspawn` hors de l'éditeur : refusés (« Entre d'abord dans l'éditeur »).
- [ ] En noclip, ouvrir « Le plus proche » puis s'éloigner vers un autre spawn avant de cliquer « Supprimer » : c'est bien le spawn affiché qui est supprimé.
- [ ] Fichier `spawns/<map>.json` volontairement cassé (virgule en trop) puis deux sauvegardes : `<map>.json.<date>.invalid.bak` contient toujours l'original.

## Phase 4b — Menu admin, SimpleAdmin et Links
- [ ] `!retake` sans la permission `@retakev4/admin` : refusé ; `!retake` depuis la console serveur : message « s'utilise en jeu ».
- [ ] `!retake` (admin) : menu « Admin Retake » avec Éditeur de spawns, Forcer le site, Mélanger les équipes.
- [ ] Menu → Éditeur de spawns : l'éditeur s'ouvre (comme `css_retake_edit`) ; `!retake edit` fait de même ; `!retake foo` affiche l'usage.
- [ ] Menu → Forcer le site → « Site B au prochain round » : round suivant sur B ; « jusqu'à annulation » puis « Annuler le forçage » ; un site sans spawn est refusé avec le même message que la console.
- [ ] Menu → Mélanger les équipes : annonce dans le chat, équipes mélangées à la fin du round.
- [ ] Sans CS2-SimpleAdmin : log « CS2-SimpleAdmin not found », aucune erreur.
- [ ] Avec CS2-SimpleAdmin : catégorie « Admin Retake » dans son menu admin, les trois entrées fonctionnent (le forçage ouvre un sous-menu) ; `css_plugins reload RetakeV4` ne duplique pas les entrées.
- [ ] `admin.json` → `SimpleAdminBridge: false` : rien n'est ajouté au menu SimpleAdmin.
- [ ] `links.json` avec `{ "Commands": ["discord", "dc"], "Message": "{green}Discord :{default} https://discord.gg/xxx" }` : `!discord` et `!dc` affichent le message en couleur au joueur.
- [ ] `links.json` avec une commande `guns` ou `retake_test` : entrée ignorée, avertissement dans les logs, les autres liens marchent.

## Phase 5a — Achat natif (NativeBuy / Both)
- [ ] `Mode: Menu` (défaut) : menu d'achat fermé (`mp_buytime 0`), `!guns` inchangé, rappel « Tape !guns… » toutes les 5 minutes.
- [ ] `Mode: NativeBuy` : `!guns` répond par le rappel ; pas d'ouverture automatique du menu ; 16000 $ affichés à chaque round.
- [ ] Freeze time, vivant : acheter une arme du pool dans le menu d'achat (B) → l'arme est remplacée immédiatement, alerte « Armes mises à jour. », argent revenu à 16000 $, aucune arme en double au sol.
- [ ] « Acheter » l'arme qu'on tient déjà → on la garde.
- [ ] Round live : un achat depuis le menu d'achat (B) est refusé (« … que pendant le freeze time … »), rien n'est donné ni lâché ; `buy galilar` en console → « Enregistré, utilisé dès le prochain round. ».
- [ ] Acheter une flash par le menu d'achat avec déjà une flash allouée : la flash allouée reste, seule l'achetée disparaît.
- [ ] Acheter une arme hors du pool du round (ex. M4A4 en T) → « Cette arme n'est pas disponible pour ce round. », préférence inchangée.
- [ ] Acheter l'AWP → « Tu es maintenant volontaire pour l'AWP. », visible dans `!guns` (mode Both).
- [ ] Acheter une grenade, l'armure ou le kit → « … donnés automatiquement. », rien n'est ajouté.
- [ ] `buy ak47` dans la console (achat nommé) : traité sans passer par la capture, aussi en round live.
- [ ] `Mode: Both` : `!guns` et le menu d'achat modifient la même préférence.
- [ ] `HowToIntervalMinutes: 0` : aucun rappel ; changement de map puis `css_plugins reload RetakeV4` : un seul rappel par intervalle (pas de timer en double).
- [ ] Mode FastPlant + achat pendant le freeze time : le poseur garde la C4.

## Phase 5b — API publique et release
- [ ] Installer le zip `RetakeV4-4.0.0.zip` sur un serveur propre : le plugin démarre, `css_retake_info` affiche 4.0.0, les configs fournies sont lues sans avertissement.
- [ ] Mise à jour avec `RetakeV4-4.0.0-no-configs.zip` : vos configs existantes ne sont pas touchées.
- [ ] Un plugin de test qui lit `RetakeApi.Capability.Get()` dans `OnAllPluginsLoaded` reçoit `RoundPrepared` (type, site, poseur), `LoadoutAssigned`, `BombPlanted`, `RoundEnded` (vainqueur) et `PlayerQueued`.
- [ ] `LastPlayerAlive` : déclenché une fois quand une équipe de 2+ joueurs n'a plus qu'un vivant ; jamais pour une équipe d'un seul joueur.
- [ ] Un abonné qui lève une exception : avertissement dans les logs, le round continue.
- [ ] `css_plugins reload RetakeV4` : un nouvel appel à `Get()` renvoie l'API rechargée (et non null).
- [ ] `api.json` → `Enabled: false` : `Get()` renvoie null, sans exception.

## Phase panel (4.1.0)

- [ ] Avec `Database.Type = MySql`, au démarrage, la table `retake_catalog` contient une ligne `server_key = default` dont le JSON liste les types de round de `roundtypes.json`.
- [ ] Modifier `Database.ServerKey` en `test-1`, redémarrer : une ligne `test-1` apparaît.
- [ ] Joueur connecté : changer en base `primary_weapon` d'une de ses lignes (et `updated_at`), attendre le round suivant : l'arme reçue est la nouvelle, sans reconnexion.
- [ ] Choisir une arme dans `!guns` puis lancer un round : le choix reste (pas d'écrasement par l'ancienne valeur).
- [ ] Couper MySQL pendant la partie : les joueurs gardent leurs armes choisies, un seul avertissement par période de retry dans la console.

## Menus de chat (`Menu.Display`)

- [ ] `hud.json` avec `"Menu": { "Display": "Chat" }`, redémarrer : `!guns` affiche une liste numérotée dans le chat, aucune entité de menu devant le joueur.
- [ ] `!1` sur un sous-menu affiche le niveau suivant ; « Retour » revient au niveau parent.
- [ ] Choisir une arme ferme le menu et l'arme est appliquée (en freeze time) et enregistrée (visible sur le panel).
- [ ] Le toggle AWP affiche son état (ON/OFF) à la réouverture de `!guns`.
- [ ] Menu admin (`!retake`) et éditeur de spawns fonctionnent aussi en chat.
- [ ] Choisir dans le chat ne provoque ni kick ni « Long frame » au premier usage.
- [ ] `"Display": "WorldText"` (ou absent) : retour aux menus devant le joueur, comportement inchangé.
- [ ] Valeur invalide (`"Display": "Foo"`) : avertissement « invalid JSON » dans les logs, tout `hud.json` repris par défaut (menus `WorldText`).

## Panneau central et échauffement (4.2.0)

- [ ] Sans `hud.json` (créé par défaut) : `!guns` ouvre le panneau au centre de l'écran, le bloc d'infos réapparaît à la fermeture.
- [ ] Avancer / reculer déplace la ligne surlignée (en boucle en haut et en bas) ; Utiliser (E) valide, dans toutes les phases.
- [ ] Liste longue (pistolets) : le panneau défile avec le curseur, flèches ▲ / ▼ quand des lignes sont cachées ; `Menu.CenterVisibleLines` règle le nombre de lignes.
- [ ] Tenir avancer au moment où le menu s'ouvre seul ne déplace pas le curseur.
- [ ] Après un choix, la confirmation (« Armes mises à jour » / « Enregistré… ») s'affiche dans le panneau.
- [ ] Le panneau tient en entier à l'écran avec 7 lignes ou plus (titre, lignes, confirmation, aide).
- [ ] Sections Terroristes (orange) et Antiterroristes (bleu), chaque type de round affiche son résumé « Type : principale / pistolet » ; l'arme choisie est cochée.
- [ ] Échauffement sans fin (hébergeur qui annonce une fin infinie) : il se termine au bout de `WarmupFallbackSeconds`, avec le message de fin forcée.
- [ ] Premier round après démarrage avec des joueurs : aucun `Slow handler` au-dessus de ~200 ms, aucun kick `NETWORK_DISCONNECT_OVERFLOW`.

## 4.3.0 — personnalisation

- [ ] Sans `lang/` ni annonces : préfixes et messages identiques à 4.2.1, aucune annonce.
- [ ] `lang/fr.json` avec `core.prefix` changé : le nouveau préfixe apparaît, un joueur en anglais garde l'ancien.
- [ ] Clé inconnue dans `lang/fr.json` : avertissement au démarrage, le reste fonctionne.
- [ ] `announcements.json` avec 2 messages et `IntervalSeconds` 30 : un message toutes les 30 s, jamais deux fois le même de suite ; liste de la map courante utilisée.
- [ ] Accueil : reçu une fois en rejoignant une équipe, pas après un changement d'équipe ni un changement de map, de nouveau après reconnexion.
- [ ] `!regles` avec `Lines` : chaque ligne avec le préfixe d'aide.
- [ ] Refus de permission (`!scramble` sans droit) : préfixe d'alerte.

## 4.3.1 — nettoyage et alertes HUD

- [ ] Instadefuse réussi ou refusé : le message apparaît dans le chat et en alerte dans le bloc centré, pour tous les joueurs.
- [ ] Joueur déplacé (rotation après victoire CT, scramble, rééquilibrage) : l'alerte « tu es maintenant T/CT » apparaît dans son bloc centré.
- [ ] Scramble demandé par un admin : alerte visible par tous.
- [ ] Menus (CenterHtml, WorldText, Chat) : navigation inchangée ; les touches 1 à 9 n'ont plus aucun effet sur les menus (comportement déjà observé, les commandes n'arrivent pas au serveur).

## 4.3.2 — couteau et gel du menu

- [ ] Avec WeaponPaints et un couteau personnalisé : chaque spawn (pistol, mid, full buy) donne le couteau choisi ; sans skin choisi, le couteau par défaut de l'équipe.
- [ ] `!guns` ouvert pendant le round : le joueur ne bouge plus tant que le menu est ouvert, retrouve sa vitesse normale dès la fermeture (choix, Retour/Fermer, début du round).
- [ ] `Menu.FreezeWhileOpen = false` dans `hud.json` : le joueur bouge à nouveau avec le menu ouvert.

## 4.4.0 — nettoyage de map

- [ ] Mirage, Inferno, Nuke, Dust2 : au début de chaque round, portes ouvertes, vitres et aérations cassées ; caisses, barils et décor intacts ; aucune entité qui disparaît ou reste en l'air.
- [ ] `DoorOpenChancePercent` à 0 : aucune porte ouverte, vitres et aérations toujours cassées.
- [ ] Pendant l'échauffement et pendant l'éditeur de spawns : rien n'est touché.
- [ ] `!retake cleanup` (ou menu admin → Nettoyage de map, et `!admin` → Retake avec SimpleAdmin) : le panneau montre l'entité visée (classe, modèle, nom).
- [ ] Corriger une porte en « Ne pas toucher », Sauvegarder, Quitter : elle reste fermée aux rounds suivants, et après un redémarrage du serveur.
- [ ] « Auto » retire la correction ; « Rejouer le nettoyage » applique tout de suite la sélection en cours.
- [ ] Un second admin qui ouvre l'éditeur reçoit « déjà en cours ».
- [ ] Changement de map avec l'éditeur ouvert : il se ferme, aucune erreur dans les logs.
- [ ] Éditeur ouvert puis `!guns` (ou 3 minutes sans action) : l'éditeur se ferme et le nettoyage reprend au round suivant.
