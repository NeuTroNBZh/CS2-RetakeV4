# Contrat plugin ↔ panel

Fichiers partagés avec le dépôt `CS2-RetakeV4-Panel` (copiés à l'identique dans `tests/fixtures/contract/`).

- `catalog.v1.json` : contenu de `retake_catalog.catalog` (format 1) pour les round types `Pistol` et `FullBuy` définis dans `CatalogExportTests`.
- `player_loadout.json` : valeurs de `team`, clé AWP et lignes de `player_loadout` telles que le panel les écrit.
- `stats/views.v1.json` : nom et ordre des colonnes des vues publiques `v_stats_*` (module Stats), lues par le panel et les sites externes. Les définitions et types sont dans `docs/STATS.md`. Une vue ne change jamais de façon incompatible : une v2 crée `views.v2.json` et de nouvelles vues.
- `stats/views.v2.json` : vues ajoutées en 4.9 (`v2_stats_*` parallèles aux vues v1 avec les colonnes nouvelles, `v_stats_player_events`, `v_stats_player_network`, `v_stats_server_health`, `v_stats_presence`). `views.v1.json` reste inchangé ; le panel lit v1, les sites peuvent lire les deux.

Toute modification se fait dans les deux dépôts, avec leurs tests de contrat.
