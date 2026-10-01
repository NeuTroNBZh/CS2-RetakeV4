# Personnalisation serveur (RetakeV4 4.3.0) et pack Agora — design

- **Date** : 2026-10-01
- **Dépôts** : `CS2-RetakeV4` (partie 1), `agora-retake` (partie 2)
- **Remplace** : agora-retake 3.1.1 (fork V3)

## Objectif

Le serveur Agora tourne avec RetakeV4, sans fork : tout ce qui rend Agora différent (préfixes, textes, annonces, liens, règles, réglages, spawns) est de la configuration. RetakeV4 gagne les points d'extension génériques qui manquaient ; `agora-retake` devient un pack de configuration posé sur une release RetakeV4 précise.

Critères de réussite :
- un serveur sans configuration Agora se comporte exactement comme RetakeV4 4.2.1 ;
- le zip Agora installé, on retrouve en jeu les préfixes, messages aléatoires et par map, message d'accueil, `!agora`, `!discord`, `!skin`, `!site`, `!commandes`, `!regles` de la 3.1.1 ;
- une mise à jour de RetakeV4 n'écrase aucune personnalisation et une clé de texte remplacée qui n'existe plus fait échouer la CI du pack.

## Partie 1 — RetakeV4 4.3.0

### 1.1 Textes remplaçables

- Fichiers optionnels `configs/plugins/RetakeV4/lang/<langue>.json` (même format que `lang/*.json` du plugin, clés partielles).
- Résolution d'une clé pour une langue : remplacement de cette langue, sinon texte du plugin (comportement CSSharp actuel, repli inclus). Un remplacement `en` ne s'applique pas à un joueur `fr`.
- Domain : `TextOverrides` (pur) — construit à partir des dictionnaires lus, rejette les clés absentes des textes du plugin (signalées, ignorées) et les valeurs vides ; expose `TryGet(culture, key)`.
- Adaptateur : `TextService` interroge `TextOverrides` avant `IStringLocalizer`, puis applique `string.Format` avec les arguments et les balises de couleur, comme aujourd'hui. Une valeur remplacée dont les `{n}` ne correspondent pas aux arguments est rejetée au chargement (comparaison avec le nombre de paramètres du texte d'origine).
- Lecture au chargement du plugin (fichier illisible ou JSON invalide : avertissement, remplacements de ce fichier ignorés, aucun plantage). Pas de rechargement à chaud au-delà du rechargement du plugin.

### 1.2 Préfixes

- Nouvelles clés `core.prefix_alert` et `core.prefix_help`, valant `core.prefix` dans `en.json` et `fr.json` livrés.
- `ITextService` : `Chat`/`ChatAll` inchangés (préfixe normal) ; ajout de `ChatAlert(player, key, args)` et `ChatHelp(player, key, args)`.
- Usage : réponses de commandes (Links, `css_retake_info`, retours de `!guns`/`!awp` hors menu) → aide ; refus et erreurs signalés au joueur (droits admin manquants, commande impossible) → alerte. La liste exacte des appels est fixée dans le plan.

### 1.3 Module Announcements (`announcements.json`)

```json
{
  "Version": 1,
  "IntervalSeconds": 420,
  "Messages": [],
  "MapMessages": { "de_mirage": [] },
  "Welcome": ""
}
```

- Messages réguliers : toutes les `IntervalSeconds`, un message de `MapMessages[map]` s'il existe et n'est pas vide, sinon de `Messages`, affiché à tous avec le préfixe normal. Pas deux fois de suite le même message quand la liste en a au moins deux. Aucun message si les deux listes applicables sont vides.
- Accueil : `Welcome` envoyé une fois par session (connexion → déconnexion) au joueur, à sa première entrée en équipe, avec le préfixe d'aide. Ignoré si vide.
- Les textes sont du contenu serveur écrit tel quel (balises de couleur acceptées), comme `links.json` ; ils ne passent pas par `lang/`.
- Domain : `AnnouncementPicker` (choix par map, non-répétition, `IRandom`) et `WelcomeTracker` (une fois par session). Timer via `hooks.RepeatTimer`, tué et recréé au changement de map.
- Validation : `IntervalSeconds` ≥ 30 ; messages vides ignorés.
- Par défaut, toutes les listes sont vides : le module ne fait rien.

### 1.4 Liens sur plusieurs lignes (`links.json` version 2)

- `LinkConfig` gagne `Lines` (liste). Un lien a `Message` ou `Lines` (au moins un non vide, pas les deux). Chaque ligne est envoyée au joueur avec le préfixe d'aide ; `Message` garde son affichage actuel.
- Une config version 1 reste valide sans changement.

### 1.5 Hors périmètre

Nom du créateur (une ligne de `!commandes`), rechargement à chaud des textes, panel web.

### 1.6 Tests et version

- TDD Domain : `TextOverrides`, `AnnouncementPicker`, `WelcomeTracker`, validateurs Announcements et Links v2.
- Intégration : chargement des remplacements (fichier absent, invalide, clé inconnue, placeholders incohérents), `ConfigExporter` écrit `announcements.json`.
- `docs/CHECKLIST-INGAME.md` : section 4.3.0. README : section « Personnaliser son serveur ».
- Version 4.3.0.

## Partie 2 — dépôt agora-retake

### 2.1 Structure

```
retakev4.version          # ex. 4.3.0
pack/addons/counterstrikesharp/configs/plugins/RetakeV4/
  lang/fr.json, lang/en.json
  announcements.json, links.json
  <configs modifiées par rapport aux défauts V4>
pack/addons/counterstrikesharp/configs/plugins/RetakeV4/spawns/   # seulement si différents des défauts V4
build.ps1, tests/, README.md, CHANGELOG.md, LICENSE
```

- Avant remplacement : tag `v3.1.1-final` et branche `v3` sur l'état actuel.

### 2.2 Contenu Agora

- Préfixes : `[Agora-Retake]` doré, `[! Agora-Retake]` (`!` rouge), `[? Agora-Retake]` (`?` bleu clair).
- Textes `fr` (et `en` pour les mêmes clés) reformulés depuis les messages de la 3.1.1 (`MessageUtils`, InstaDefuse, allocation, AWP) quand un équivalent V4 existe.
- Annonces : les 4 messages généraux, les listes de Mirage, Inferno, Nuke, Dust2, intervalle 420 s, message d'accueil de la 3.1.1.
- Liens : `agora`/`agoralinks` (site, skin changer, Discord), `dis`/`discord`, `skin`/`skinchanger`, `site`, `commandes`/`commands`/`help`/`aide` (liste des commandes + « Créateur : NeuTroNBZh »), `regles`/`rules`/`regle`/`rule` (5 règles).
- URLs : `https://agora-retake.fr`, `https://skin-changer.agora-retake.fr`, `https://discord.gg/CkyU2Wehsw`.
- Réglages : comparaison de la config 3.1.1 avec les défauts V4 ; seuls les écarts sont écrits dans le pack.
- Spawns : les 11 fichiers de la 3.1.1 convertis par `SpawnMigrator` ; inclus seulement pour les maps où ils diffèrent des spawns V4 par défaut.

### 2.3 Construction et publication

- `build.ps1 [-RetakeV4Dir <dossier>]` : télécharge les deux zips de la release fixée (ou les prend dans un dossier local), produit `artifacts/agora-retake-<version>-agora.<n>.zip` (zip complet RetakeV4 + `pack/` superposé) et `agora-retake-<version>-agora.<n>-no-configs.zip` (le zip `-no-configs` RetakeV4 tel quel, pour une mise à jour qui garde la config du serveur).
- GitHub Actions : sur tag `v*`, tests puis build et release avec les deux zips.
- Version du pack : `<version V4>-agora.<n>`.

### 2.4 Tests du pack (CI)

- Chaque JSON du pack passe les validateurs RetakeV4 de la version fixée (petit outil .NET de test référençant la release, ou exécution de `ConfigExporter`/validateurs depuis le dépôt RetakeV4 au tag fixé).
- Chaque clé de `lang/*.json` existe dans les textes de RetakeV4 à cette version, avec le même nombre de placeholders.
- Les spawns passent le chargement de `SpawnCatalog`.

### 2.5 Documentation

README : installation (zip à la racine du serveur), personnalisation, mise à jour (zip `-no-configs`), migration depuis la 3.1.1 (import des préférences V3 par la commande d'import V4 existante).

## Risques

- Clés de texte renommées en V4 : couvert par le test de clés du pack.
- Le serveur Dathost actuel a déjà la config V4 et les préférences importées : l'installation du pack y remplace seulement les fichiers du pack.
