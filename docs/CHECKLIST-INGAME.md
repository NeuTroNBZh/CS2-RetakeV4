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
