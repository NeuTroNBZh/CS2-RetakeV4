# RetakeV4 Panel — design

Date : 2026-10-01
Statut : validé en conversation, en attente de relecture de la spec

## 1. Objectif

Un panel web générique, installable par n'importe quel serveur RetakeV4, où chaque joueur se connecte avec Steam et règle ses préférences d'armes à la souris. La v1 ne livre que les préférences joueur ; l'architecture est modulaire pour accueillir ensuite des modules stats (CS2-STATPLAY), admin, skins (WeaponPaints) et d'autres, sans toucher au cœur. Une personnalisation Agora viendra après, à partir de ce panel générique.

Critères de succès de la v1 :
- un admin de serveur installe le panel avec Docker en suivant le README, sans modifier de code ;
- un joueur se connecte avec Steam, choisit ses armes par équipe et type de round, et le choix s'applique en jeu au round suivant, sans reconnexion ;
- le panel ne propose que des armes que le plugin accepte réellement.

Hors périmètre v1 : stats, admin, skins, personnalisation Agora, choix du serveur dans l'interface (multi-serveur).

## 2. Architecture

### 2.1 Dépôts et stack

- Nouveau dépôt `NeuTroNBZh/CS2-RetakeV4-Panel`, versionné indépendamment du plugin.
- AdonisJS 7, Inertia 2 + Vue 3.5, TypeScript, Vite, Lucid (mysql2), VineJS, Adonis Shield, Japa. Node 24.
- Pas de Redis : sessions en cookie chiffré, rate-limit stocké en base.
- Les évolutions côté plugin se font dans ce dépôt (`CS2-RetakeV4`), décrites en section 5.

### 2.2 Bases de données

| Connexion | Contenu | Droits du panel | Schéma géré par |
|-----------|---------|-----------------|-----------------|
| `panel` | sessions, rate-limit, réglages du panel | complets | migrations Lucid du panel |
| `retake` | `player_loadout`, `retake_catalog` | `SELECT/INSERT/UPDATE` sur `player_loadout`, `SELECT` sur `retake_catalog` | plugin RetakeV4 |

Les deux connexions peuvent pointer vers la même base MySQL. Le panel ne crée ni ne modifie jamais une table du plugin. Les modules futurs déclareront leurs propres connexions (statplay en lecture seule, tables `wp_*` de WeaponPaints).

### 2.3 Système de modules

- Chaque fonctionnalité vit dans `app/modules/<nom>/` (contrôleurs, services, validateurs) et `inertia/pages/<nom>/`.
- Un module exporte une définition : nom, routes, entrées de menu, connexions requises, permissions requises.
- Le cœur charge les modules activés par configuration (`PANEL_MODULES`, par défaut `loadouts`). Un module dont une connexion requise n'est pas configurée ou injoignable au démarrage est désactivé, avec une ligne de log explicite ; le reste du panel fonctionne.
- Un module ne dépend jamais d'un autre module, seulement du cœur.
- v1 : `core` (toujours actif : connexion Steam, layout, i18n en/fr, permissions) et `loadouts`.

## 3. Connexion Steam et sécurité

### 3.1 Connexion

- Steam OpenID 2.0, sans clé API obligatoire. Au retour :
  - vérification de la signature par `check_authentication` auprès de Steam ;
  - `openid.return_to` doit correspondre exactement à l'URL de callback du panel ;
  - `openid.claimed_id` doit correspondre à `^https://steamcommunity\.com/openid/id/(\d{17})$`.
- Le SteamID64 est toujours manipulé comme chaîne (jamais comme nombre JS) ; en SQL, comparé à une colonne `BIGINT UNSIGNED` via paramètre chaîne, et lu avec `CAST(steam_id AS CHAR)`.
- Clé API Steam facultative (`STEAM_API_KEY`) : pseudo et avatar. Sans elle, le panel affiche le SteamID.

### 3.2 Session

- Cookie de session chiffré, `HttpOnly`, `Secure`, `SameSite=Lax`, durée 30 jours.
- En production, le panel refuse de démarrer si `APP_URL` n'est pas en `https://`.

### 3.3 Protections

- Shield : CSRF sur toute écriture, HSTS, CSP stricte, `X-Frame-Options: DENY`.
- Rate-limit en base : connexion (10/min par IP), écritures de préférences (30/min par joueur).
- Le SteamID d'une écriture vient toujours de la session, jamais de la requête.
- Toute requête SQL est paramétrée.

### 3.4 Permissions

- Le cœur fournit `can(user, permission)`. v1 : rôle `player` pour tout joueur connecté, rôle `admin` pour les SteamID listés dans `PANEL_ADMINS` (liste séparée par des virgules).
- Aucune page admin en v1 ; le mécanisme existe pour les modules futurs.

### 3.5 Secrets

Uniquement par variables d'environnement, documentées dans `.env.example`. Rien dans l'image.

## 4. Module loadouts

### 4.1 Catalogue

Le plugin est la seule source des règles. Il publie dans `retake_catalog` :

| Colonne | Type | Sens |
|---------|------|------|
| `server_key` | `VARCHAR(64)` PK | clé du serveur (config plugin `ServerKey`, défaut `default`) |
| `format_version` | `INT` | version du format JSON (v1 : `1`) |
| `catalog` | `TEXT` (JSON) | voir ci-dessous |
| `updated_at` | `DATETIME(6)` UTC | date de publication |

JSON v1 :

```json
{
  "roundTypes": [
    {
      "name": "FullBuy",
      "teams": {
        "T":  { "primaries": ["weapon_ak47"], "secondaries": ["weapon_glock"], "defaultPrimary": "weapon_ak47", "defaultSecondary": "weapon_glock", "awp": true },
        "CT": { "primaries": ["weapon_m4a1"], "secondaries": ["weapon_usp_silencer"], "defaultPrimary": "weapon_m4a1", "defaultSecondary": "weapon_usp_silencer", "awp": true }
      }
    }
  ]
}
```

- Les listes sont calculées par `WeaponMenu.Options` du Domain : exactement les choix du menu en jeu, AWP exclue des armes principales.
- `awp` vaut `true` si le type de round distribue des AWP à cette équipe.
- Le panel lit la ligne `server_key = PANEL_RETAKE_SERVER` (défaut `default`). `format_version` inconnu → message explicite, pas de plantage.

### 4.2 Page « Mes armes »

- Onglets T et CT.
- Pour chaque type de round où le joueur a un choix (plus d'une option en principale ou en secondaire) : sélection de l'arme principale et de la secondaire, en cartes cliquables (nom et image), l'arme par défaut marquée.
- Interrupteur « volontaire AWP », affiché si au moins un type de round distribue l'AWP.
- Bouton « réinitialiser » par type de round : supprime le choix (retour au défaut du plugin), en mettant `primary_weapon` et `secondary_weapon` à `NULL`.
- Après une écriture : confirmation « appliqué au prochain round ».

### 4.3 Écriture dans `player_loadout`

Format identique à celui du plugin :
- clé `(steam_id, team, round_type)` ; `team` vaut `0` pour T et `1` pour CT (valeurs de l'enum `TeamSide` du plugin, différentes de celles de WeaponPaints) ;
- le volontariat AWP est porté par la ligne `round_type = '*'` de chaque équipe (`awp_opt_in`) ;
- upsert (`INSERT ... ON DUPLICATE KEY UPDATE`), `updated_at` en UTC avec précision microseconde ;
- validation : VineJS pour la forme, puis l'arme doit figurer dans les options du catalogue pour ce type de round, cette équipe et cet emplacement ; sinon 422.

### 4.4 Noms et images

Table statique dans le panel : `weapon_xxx` → nom affiché en/fr et image SVG locale. Pas d'appel externe. Arme inconnue → identifiant brut, image générique.

### 4.5 Cas dégradés

- Aucun catalogue : « le serveur n'a pas encore publié ses armes ».
- Base `retake` injoignable pendant une requête : page d'erreur propre pour ce module, le reste du panel fonctionne.

## 5. Évolutions du plugin RetakeV4

Phase dédiée, dans ce dépôt, avec les règles habituelles (Domain + TDD, aucun accès base sur le thread de jeu).

1. **Config** : `Database.ServerKey` (défaut `"default"`) dans la config d'Allocation.
2. **Migration** `retake_catalog` pour MySQL et SQLite (SQLite : écrit aussi, mais le panel ne fonctionne qu'avec MySQL ; documenté).
3. **Export du catalogue** : fonction Domain `CatalogExport.Build(definitions)` → JSON v1 ci-dessus, testée. Écrit via la file d'écriture existante à chaque chargement de `roundtypes.json`.
4. **Recharge des préférences modifiées** : le plugin retient, pour chaque joueur connecté, la plus grande valeur `updated_at` de ses lignes au moment du chargement. À chaque début de round, une requête en arrière-plan `SELECT CAST(steam_id AS CHAR), MAX(updated_at) FROM player_loadout WHERE steam_id IN (connectés) GROUP BY steam_id` ; tout joueur dont la valeur diffère de celle retenue est rechargé dans `PreferenceBook`, retour au jeu par `Server.NextFrame` + `ModuleGuard`. La comparaison porte sur l'égalité des valeurs stockées, pas sur des horloges : aucun écart d'heure entre le serveur de jeu, le panel et MySQL ne peut faire manquer une modification. Une écriture faite en jeu met à jour la valeur retenue, pour ne pas recharger inutilement.
5. Choix en jeu et choix web écrivent la même ligne : la dernière écriture gagne.

## 6. Tests

- **Panel** (Japa, TDD) :
  - unitaires : vérification OpenID, lecture du catalogue (versions, JSON invalide), validation d'une sélection, rendu d'une arme inconnue ;
  - fonctionnels avec MySQL réel (service Docker en CI) : connexion simulée, lecture/écriture « Mes armes », refus hors catalogue, impossibilité d'écrire pour un autre SteamID, CSRF, rate-limit, module désactivé sans connexion ;
  - couverture ≥ 80 % sur `app/`.
- **Contrat plugin ↔ panel** : fixtures partagées (`contract/player_loadout.json`, `contract/catalog.v1.json`). Le plugin a un test qui vérifie que ses sorties correspondent aux fixtures ; le panel a un test qui les relit. Les fixtures sont copiées dans le dépôt du panel ; toute modification passe par les deux dépôts.
- **Plugin** : tests Domain de `CatalogExport`, tests d'intégration de la migration et de la requête de recharge (SQLite en test).

## 7. CI et livrable

- CI à chaque push : lint, vérification de types, tests avec service MySQL.
- Tag `v*` : image multi-arch (amd64, arm64) publiée sur `ghcr.io/neutronbzh/cs2-retakev4-panel`, release GitHub avec `docker-compose.yml`.
- Image : Node 24 alpine, utilisateur non-root, healthcheck HTTP, migrations du panel exécutées au démarrage.
- `docker-compose.yml` d'exemple : panel + Caddy (HTTPS automatique).
- README d'installation (anglais, version française) : créer l'utilisateur MySQL (SQL fourni), remplir `.env`, `docker compose up -d`, configurer `ServerKey` côté plugin, vérifier.

## 8. Variables d'environnement (v1)

| Variable | Obligatoire | Rôle |
|----------|-------------|------|
| `APP_URL` | oui | URL publique, `https://` en production |
| `APP_KEY` | oui | clé de chiffrement des cookies |
| `PANEL_DB_*` | oui | connexion `panel` (host, port, user, password, database) |
| `RETAKE_DB_*` | oui pour `loadouts` | connexion `retake` |
| `PANEL_RETAKE_SERVER` | non | clé du serveur lue dans `retake_catalog` (défaut `default`) |
| `PANEL_MODULES` | non | modules activés (défaut `loadouts`) |
| `PANEL_ADMINS` | non | SteamID64 admins |
| `STEAM_API_KEY` | non | pseudos et avatars |
| `PANEL_LOCALE` | non | langue par défaut (`en`, `fr`) |
