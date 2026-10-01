# Module MapVote (RetakeV4 4.5.0) — design

- **Date** : 2026-10-01
- **Remplace** : RockTheVote 1.8.5 (abnerfs), qui ne se charge plus sur CounterStrikeSharp 1.0.376 (`EventPlayerChat` supprimé)

## Objectif

Faire tourner les maps du serveur Retake : à la fin d'une partie, les joueurs votent pour la map suivante, et `!rtv` permet de quitter plus tôt une map que la majorité ne veut plus.

Critères de réussite :
- les maps proposées sont exactement celles qui ont des spawns Retake (moins la map en cours et les maps exclues), sans liste à tenir à jour ;
- le vote se fait dans le menu HUD Retake, sans geler les joueurs ;
- la map change toute seule à la fin de la partie, ou à la fin du round après un `!rtv` réussi ;
- aucun plugin tiers.

## Maps proposées

`MapPool.Build(mapsWithSpawns, currentMap, excluded, isValid)` (Domain) :
- point de départ : les noms des fichiers `plugins/RetakeV4/spawns/<map>.json` ;
- retirées : la map en cours, les maps de `ExcludedMaps` (insensible à la casse), celles que le serveur ne connaît pas (`Server.IsMapValid`) ;
- triées par nom ;
- moins de 2 maps restantes : aucun vote n'est ouvert, un avertissement est logué une fois par map.

## Vote de fin de partie

- Déclenchement (`VoteTrigger`, Domain) : au début d'un round, si `mp_maxrounds` > 0, hors échauffement, aucun vote déjà fait sur cette map, et `mp_maxrounds - TotalRoundsPlayed <= TriggerRoundsBeforeEnd`.
- Le menu `mapvote.menu` s'ouvre pour chaque joueur humain connecté (T, CT ou spectateur). Il liste toutes les maps, sur plusieurs pages si besoin. Une ligne d'en-tête rappelle le temps restant.
- Un joueur peut changer son vote tant que le vote est ouvert. Un joueur qui arrive pendant le vote reçoit le menu.
- Durée : `VoteSeconds`. À la fin, le menu se ferme pour tous.
- Résultat (`MapVote.Result(IRandom)`, Domain) : la map qui a le plus de voix ; égalité → tirage au sort parmi les ex æquo ; aucune voix → tirage au sort dans la liste.
- Annonce dans le chat : la map choisie et son nombre de voix. Le module fixe `nextlevel <map>`.
- À `cs_win_panel_match`, après `ChangeDelaySeconds`, `changelevel <map>`.
- `!nextmap` (et `css_nextmap`) répond à tous : la map choisie, ou « pas encore votée ».

## !rtv

- `!rtv` (et `css_rtv`) : un joueur signale qu'il veut changer de map. Refusé avec un message si `RtvEnabled` est faux, pendant l'échauffement, avec moins de `RtvMinPlayers` joueurs humains, ou avant `RtvMinRounds` rounds joués.
- Seuil (`RtvTracker`, Domain) : `ceil(joueurs humains × RtvPercentage / 100)`, au moins 1. Le compte est annoncé dans le chat à chaque `!rtv`.
- Seuil atteint :
  - si aucun vote n'est fait sur cette map, un vote s'ouvre tout de suite (même menu, même durée) ;
  - si le vote de fin de partie est déjà décidé, ce résultat sert directement ;
  - dans les deux cas, la map change à `round_end`, après `ChangeDelaySeconds` (pas d'attente de la fin de partie).
- Une fois le seuil atteint, `!rtv` répond « déjà en cours ». Un joueur qui se déconnecte perd son `!rtv` et sa voix ; le seuil est recalculé avec les joueurs restants.

## HUD : menu sans gel

`Menu` (Domain) reçoit `bool FreezeWhileOpen = true`. Le moteur HUD ne gèle le joueur que si `HudConfig.Menu.FreezeWhileOpen` **et** `Menu.FreezeWhileOpen` sont vrais. Le menu de vote passe `false`. Les autres menus ne changent pas.

## Configuration

`mapvote.json` (`MapVoteConfig`) :

```json
{
  "Version": 1,
  "Enabled": true,
  "TriggerRoundsBeforeEnd": 3,
  "VoteSeconds": 30,
  "ChangeDelaySeconds": 8,
  "RtvEnabled": true,
  "RtvPercentage": 60,
  "RtvMinPlayers": 2,
  "RtvMinRounds": 3,
  "ExcludedMaps": []
}
```

Validation (valeur hors limites → défaut + avertissement) : `TriggerRoundsBeforeEnd` 1–10, `VoteSeconds` 10–120, `ChangeDelaySeconds` 3–30, `RtvPercentage` 1–100, `RtvMinPlayers` 1–64, `RtvMinRounds` 0–30.

## Architecture

- Domain `RetakeV4.Domain/MapVote` : `MapPool`, `MapVote` (voix immuables : `Cast`, `Remove`, `Result`), `RtvTracker` (immuable : `Want`, `Left`, `IsReached`), `VoteTrigger`, `MapVoteMenu` (construction du menu et lecture des identifiants).
- Module `Modules/MapVote` : `MapVoteModule` (rounds, commandes, menus, changement de map), `MapVoteConfig` et son validateur. Lecture de la liste des spawns par le dossier `spawns` (lecture seule, pas d'accès à `SpawnCatalog`).
- Minuterie du vote : `hooks.RepeatTimer` (1 s) comme les autres modules ; `changelevel` dans un callback gardé par `context.Guard.Run`.
- Textes `mapvote.*` en/fr.

## Erreurs

- Map gagnante devenue invalide au moment du changement : log d'erreur, pas de changement (la partie redémarre comme sans le module).
- Exception dans un handler : gardée par `ModuleGuard` comme partout.

## Tests

- Domain (TDD) : liste des maps (spawns, map en cours, exclusions, maps invalides, tri), déclenchement (`mp_maxrounds` 0, échauffement, déjà voté, bornes), voix (changement de vote, départ d'un joueur, égalité, aucune voix), seuil `!rtv` (arrondi, départs, minimum 1), menu (identifiants, lecture).
- Intégration : validateur, `ConfigExporter` écrit `mapvote.json`, `ModuleCatalog`, clés de lang en/fr.
- `docs/CHECKLIST-INGAME.md` section 4.5.0 : vote de fin de partie à 3 rounds de la fin, changement de map, `!rtv` à 2 joueurs, `!nextmap`, pas de gel pendant le vote.

## Hors périmètre

`!nominate`, vote pour prolonger la map en cours, maps workshop, cooldown sur plusieurs maps.
