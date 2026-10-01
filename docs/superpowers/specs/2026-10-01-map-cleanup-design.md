# Module MapCleanup (RetakeV4 4.4.0) — design

- **Date** : 2026-10-01
- **Remplace** : CS2-BreakerAndOpenDoor 1.0.1 (retiré du serveur Agora)

## Objectif

À chaque round, ouvrir les portes et casser les vitres et les aérations, **sans jamais toucher au reste de la map**. L'ancien plugin cassait des objets physiques, envoyait `Break` à des entités inconnues (`func_brush`, `prop_dynamic`…) et refaisait jusqu'à 7 passes : maps buguées et catégories mélangées.

Critères de réussite :
- seules les entités classées porte, vitre ou aération sont touchées, avec uniquement `Open` (portes) ou `Break` (vitres, aérations) ;
- une entité dont la catégorie est incertaine n'est pas touchée ;
- un admin peut corriger la catégorie de n'importe quelle entité en jeu, et la correction s'applique à chaque round sur cette map ;
- une passe par round, une vérification à la fin du freeze time, aucune autre.

## Catégories et comportement par défaut

| Catégorie | Action | Par défaut |
|---|---|---|
| `Door` | `Open` | activé, chance d'ouverture 100 % (réglable) ; jamais `Break` |
| `Window` | `Break` | activé |
| `Vent` | `Break` | activé |
| `Ignore` | aucune | toutes les autres entités, y compris `prop_physics*`, `func_brush`, `prop_dynamic`, entités inconnues |

## Classification (Domain)

`EntityFacts(string ClassName, string? ModelName, string? TargetName, Vector Origin)` → `CleanupKind` (`Door`, `Window`, `Vent`, `Ignore`), par une fonction pure `CleanupClassifier.Classify(facts, overrides)` :

1. Une correction de la map pour cette entité gagne toujours.
2. Règles automatiques (insensibles à la casse) :
   - `func_door`, `func_door_rotating`, `prop_door_rotating` → `Door` ;
   - `func_breakable_surf` → `Window` ;
   - `func_breakable` dont le modèle contient `glass` ou `window` → `Window` ;
   - `func_breakable` dont le modèle contient `vent` ou `grate` → `Vent` ;
   - tout le reste → `Ignore`.

`EntityKey` (identifiant stable d'une entité d'un round à l'autre) : `name:<targetname>` si l'entité a un nom unique dans la map, sinon `pos:<classname>@<x>,<y>,<z>` avec la position arrondie au point entier. Calculé par `EntityKey.For(facts, nameIsUnique)`.

Tirage des portes : `DoorRoll.ShouldOpen(chancePercent, IRandom)` ; 0 n'ouvre jamais, 100 toujours.

## Configuration

`mapcleanup.json` (`MapCleanupConfig`) :

```json
{
  "Version": 1,
  "Enabled": true,
  "OpenDoors": true,
  "DoorOpenChancePercent": 100,
  "BreakWindows": true,
  "BreakVents": true,
  "MaxEntitiesPerRound": 512,
  "FreezeEndCheck": true
}
```

Validation : chance 0–100, `MaxEntitiesPerRound` 1–4096 ; valeur invalide → défaut + avertissement.

Corrections par map : `configs/plugins/RetakeV4/mapcleanup/<map>.json`, liste `{ "Key": "<EntityKey>", "Kind": "Door|Window|Vent|Ignore", "Note": "<classe / modèle au moment de la correction>" }`. Écriture atomique avec `.bak` (même approche que `SpawnFileStore`). Fichier absent : aucune correction. Fichier illisible : aucune correction, avertissement, fichier conservé.

## Moment d'action

- Étape de préparation `PreparationOrder.Cleanup = 70`, après la pose de la bombe : une passe sur les entités de la map, au plus `MaxEntitiesPerRound` actions.
- Si `FreezeEndCheck` : à `round_freeze_end`, les portes ciblées encore fermées et les vitres/aérations ciblées encore entières reçoivent une seconde fois leur action. Pas d'autre passe.
- Rien pendant l'échauffement ni pendant l'éditeur de spawns.

## Éditeur en jeu

- Ouverture : entrée « Nettoyage de map » du menu admin Retake (donc aussi dans `!admin` de SimpleAdmin par le pont existant) ou `!retake cleanup`. Permission `@retakev4/admin`. Demande publiée sur le bus (`MapCleanupEditorRequested`), appliquée par le module MapCleanup (règle des actions d'administration).
- Panneau (menu HUD) : entité visée (trace depuis le regard, portée 1000 unités) avec classe, modèle, nom, catégorie détectée et catégorie effective ; choix `Porte`, `Vitre`, `Aération`, `Ne pas toucher`, `Auto` (retire la correction) ; `Tester ce round` (rejoue la passe) ; `Sauvegarder` ; `Quitter` (avec ou sans sauvegarde si modifié).
- Surbrillance optionnelle des entités classées (couleur par catégorie) visible du seul éditeur.
- Une seule session d'édition à la fois ; la passe automatique est suspendue pendant l'édition.

## Sécurité

- Entrées autorisées : `Open` pour `Door`, `Break` pour `Window` et `Vent`. Jamais `Kill`, `Remove`, ni d'autre entrée.
- Exception sur une entité : ignorée, comptée, une ligne de log par round au plus.
- Toutes les décisions (classification, priorité des corrections, clés, tirage) dans le Domain ; le module ne fait que lire les entités et envoyer les entrées.

## Tests

- Domain (TDD) : règles de classification, priorité des corrections, `EntityKey` (nom unique, repli position, arrondi), `DoorRoll`, sérialisation des corrections (fichier invalide signalé), plan de passe (limite d'entités, catégories désactivées).
- Intégration : validateur de config, `ConfigExporter` écrit `mapcleanup.json`, magasin des corrections (absent, illisible, atomique), `ModuleCatalog`, clés de lang en/fr.
- `docs/CHECKLIST-INGAME.md` : section 4.4.0 (Mirage, Inferno, Nuke, Dust2 : portes ouvertes, vitres et aérations cassées, rien d'autre ; éditeur ; correction persistée).

## Hors périmètre

Fermeture/ouverture aléatoire porte par porte au-delà de la chance globale, nettoyage en cours de round, intégration à d'autres modes que le retake.
