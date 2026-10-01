# CLAUDE.md — RetakeV4

Plugin CounterStrikeSharp (C# / .NET 10, CSSharp 1.0.370+) de retake CS2, réécriture complète de CS2RetakeV3.

- Spec : `docs/superpowers/specs/2026-09-30-retake-v4-design.md` (source de vérité fonctionnelle)
- Plans : `docs/superpowers/plans/`
- Résultats du prototype HUD : `docs/spikes/hud-probe-findings.md`

## Structure
- `src/RetakeV4.Domain` : logique pure, **aucune référence à CounterStrikeSharp**, couverte à ≥ 80 %.
- `src/RetakeV4` : plugin CSSharp, adaptateurs fins uniquement. Un dossier par module sous `Modules/`, un JSON par module.
- `tests/RetakeV4.Domain.Tests` : tests unitaires du Domain. `tests/RetakeV4.Integration.Tests` : config, lang, hôte de modules.
- `spikes/` : prototypes jetables, hors solution.
- `src/RetakeV4/Adapters` : accès CSSharp partagés (gamerules, requêtes joueurs).
- `src/RetakeV4/Persistence` : dépôts de préférences (SQLite, MySQL, NoOp), migrations, magasin résilient, file d'écriture, lecteur de base V3.
- `src/RetakeV4/Modules/Hud` : moteur HUD (bloc centré, menus `point_worldtext`, entrées). Logique dans `src/RetakeV4.Domain/Hud`.
- `tools/RetakeV4.SpawnMigrator` : convertit des spawns V3 (tableau à plat) au format V2 (`dotnet run --project tools/RetakeV4.SpawnMigrator -- <in> <out>`).

## Commandes
- Build : `dotnet build RetakeV4.sln -c Release` (0 warning exigé, TreatWarningsAsErrors)
- Tests : `dotnet test RetakeV4.sln`
- Couverture Domain : `dotnet test tests/RetakeV4.Domain.Tests -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line` (utiliser `-p:` et non `/p:` : Git Bash réécrit `/p:` en chemin)
- Package serveur de test : `pwsh -NoProfile -File scripts/package-dev.ps1` → `artifacts/dev/`

## Règles
- Toute décision métier va dans le Domain avec ses tests (TDD) ; les modules ne font que traduire événements CS2 ↔ Domain.
- Un module ne référence jamais un autre module : passer par `IEventBus`, `PreparationPipeline` ou les services du `ModuleContext`.
- Tout handler CSSharp, abonnement au bus ou étape de préparation d'un module passe par `context.Hooks` (`ModuleHooks`) : gardé par `ModuleGuard` et libéré automatiquement au déchargement.
- Tout hasard passe par `IRandom` (`SystemRandom.Shared` en jeu, `FixedRandom` en test).
- Textes joueurs uniquement via `lang/*.json` (clés `module.section.key`, en + fr synchronisés) ; logs en anglais avec templates constants.
- Valeurs par défaut de config uniquement dans les records C# (`*Config.cs`).
- Tout callback `Server.NextFrame` ou timer d'un module passe par `context.Guard.Run` (via `_context?.Guard`, le module a pu être déchargé entre-temps).
- Les définitions de types de round (armes, armure, AWP, kits, Zeus, pool de grenades) vivent dans `roundtypes.json` et arrivent aux modules via `PreparationContext.RoundTypeDefinition`.
- Tests en jeu : `docs/CHECKLIST-INGAME.md`, une section par phase.
- Jamais d'accès base de données sur le thread de jeu : `Task.Run` pour lire, `PreferenceWriteQueue` pour écrire, retour au jeu via `Server.NextFrame` + `ModuleGuard`. Requêtes SQL paramétrées uniquement.
- Menus HUD : un module construit un `Menu` (Domain), publie `HudMenuOpen` / `HudMenuClose` et écoute `HudMenuSelected` ; messages éphémères via `HudAlert`. Aucune entité HUD hors du module Hud.
- Spawns : lecture et écriture uniquement via `SpawnCatalog` / `SpawnFileStore` (écriture atomique, `.bak`). Entités d'éditeur uniquement via `SpawnMarkers`, jamais entre `round_prestart` et la frame après `round_start`.
- Actions d'administration : le module Admin vérifie `@retakev4/admin` puis publie une demande (`SpawnEditorRequested`, `ForceSiteRequested`, `ScrambleRequested`) ; le module propriétaire l'applique. Le menu admin (`AdminMenu`, Domain) sert au HUD et au pont SimpleAdmin.
- Achat natif : un achat n'équipe jamais rien. La résolution (`NativeBuyResolver`) et la décision (`NativeBuy.Decide`) sont dans le Domain ; `NativeBuySelector` applique via le même chemin que le menu d'armes. Timers de module uniquement via `hooks.RepeatTimer`.
