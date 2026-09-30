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
