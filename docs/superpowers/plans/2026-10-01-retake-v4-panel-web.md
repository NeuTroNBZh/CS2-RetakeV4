# RetakeV4 Panel (dépôt web) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un panel web générique, installable en Docker, où un joueur se connecte avec Steam et règle ses préférences d'armes RetakeV4.

**Architecture:** Application AdonisJS 7 (kit Vue/Inertia) dans un nouveau dépôt `CS2-RetakeV4-Panel`. Le cœur (`app/core/`) gère connexion Steam, permissions, langue et un registre de modules ; chaque fonctionnalité vit dans `app/modules/<nom>/` et déclare ses connexions, ses routes et ses entrées de menu. Le module `loadouts` lit le catalogue publié par le plugin (`retake_catalog`) et écrit `player_loadout` sur une connexion MySQL séparée, sans jamais toucher au schéma du plugin.

**Tech Stack:** Node 24, AdonisJS 7, Inertia 2 + Vue 3.5, TypeScript, Lucid (mysql2), VineJS, Shield, @adonisjs/limiter (store base), Japa, Docker, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-01-retake-v4-panel-design.md` (dans `CS2-RetakeV4`)

**Prérequis :** plan `2026-10-01-retake-v4-panel-plugin.md` exécuté (fixtures `contract/` disponibles).

## Global Constraints

- Dépôt : `C:\Users\alice\Documents\Mon-Code\plugincs2\CS2-RetakeV4-Panel`, futur `NeuTroNBZh/CS2-RetakeV4-Panel`. Créer le dépôt GitHub et publier l'image nécessitent l'accord explicite de l'utilisateur.
- Node ≥ 24 ; pas de Redis ; sessions `SESSION_DRIVER=cookie` ; rate-limit `LIMITER_STORE=database` sur la connexion `panel`.
- Connexions Lucid : `panel` (défaut, migrations du panel) et `retake` (définie seulement si `RETAKE_DB_HOST` est renseigné). Le panel ne crée ni ne modifie aucune table du plugin.
- SteamID64 toujours en chaîne (`^\d{17}$`), jamais en `number`.
- `team` : `T` → `0`, `CT` → `1` ; AWP sur `round_type = '*'` des deux équipes ; `updated_at = UTC_TIMESTAMP(6)`.
- Catalogue : format `1` uniquement ; autre version → message explicite.
- Production : refus de démarrer si `APP_URL` n'est pas en `https://`.
- Requêtes SQL paramétrées uniquement (`?` bindings) ; le SteamID d'une écriture vient de la session.
- Rate-limit : connexion 10/min par IP ; écritures loadouts 30/min par joueur.
- Textes joueur en `en` et `fr`, mêmes clés.
- TDD, couverture ≥ 80 % sur `app/` (`c8`), commits conventionnels.
- Les API d'AdonisJS 7 utilisées ici ont été vérifiées dans la doc v7 ; si le kit généré diffère sur un nom de fichier ou d'import, l'adapter et l'inscrire comme ruling.

## Review Focus

1. Retour OpenID forgé (signature valide pour une autre URL, `claimed_id` d'un autre domaine, `op_endpoint` modifié) : refus de connexion, aucune session.
2. Écriture pour un type de round ou une arme absents du catalogue courant (catalogue republié entre l'affichage et le clic) : 422, rien n'est écrit.
3. Base `retake` injoignable au moment d'une requête : page d'erreur du module, le reste du panel (accueil, connexion) répond.
4. Catalogue au JSON corrompu ou de format inconnu : message explicite, pas d'erreur 500.
5. Requête d'écriture sans jeton CSRF ou sans session : refus, rien n'est écrit.

Tests : 1 → Task 3 (`rejects a forged return_to`, `rejects a foreign claimed_id`, `rejects a foreign op_endpoint`), 2 → Task 7 (`rejects a weapon outside the catalog`, `rejects an unknown round type`), 3 → Task 7 (`shows the module error when the retake database fails`), 4 → Task 5 (`invalid JSON`, `unsupported version`), 5 → Task 7 (`requires a csrf token`, `redirects guests to login`) ; la déconnexion sans jeton est couverte par Shield, la Task 3 teste la déconnexion avec jeton.

---

### Task 1: Squelette, configuration et garde HTTPS

**Files:**
- Create: le projet via `npm create adonisjs@latest`
- Modify: `start/env.ts`, `config/database.ts`, `config/session.ts`, `config/limiter.ts`, `config/shield.ts`, `adonisrc.ts`, `.env.example`, `package.json`
- Create: `app/core/production_checks.ts`, `start/checks.ts`, `tests/unit/core/production_checks.spec.ts`, `compose.test.yml`, `.env.test`
- Delete: modèles, migrations, contrôleurs, validateurs et pages d'inscription/connexion par e-mail générés par le kit

**Interfaces:**
- Produces: `assertProductionUrl(appUrl: string, nodeEnv: string): void` (lève `Error`) ; connexions `panel` et `retake` (optionnelle) ; variables d'env listées à l'étape 3.

- [ ] **Step 1: Générer le projet**

```bash
cd C:/Users/alice/Documents/Mon-Code/plugincs2
npm create adonisjs@latest CS2-RetakeV4-Panel -- --kit=vue
```

Choix aux questions : base de données `MySQL`, guard d'authentification `session`, SSR non. Puis :

```bash
cd CS2-RetakeV4-Panel
node ace add @adonisjs/limiter
npm install -D c8
git init -b main
```

Supprimer ce que le kit a généré pour l'inscription et la connexion par e-mail (modèle `User`, migration `users`, contrôleurs `signup`/`session`, validateurs `user`, pages `auth/*`, routes associées). La page d'accueil reste.

- [ ] **Step 2: Écrire le test de la garde HTTPS (RED)**

`tests/unit/core/production_checks.spec.ts` :

```ts
import { test } from '@japa/runner'
import { assertProductionUrl } from '#core/production_checks'

test.group('assertProductionUrl', () => {
  test('accepts https in production', ({ assert }) => {
    assert.doesNotThrow(() => assertProductionUrl('https://panel.example.com', 'production'))
  })

  test('rejects http in production', ({ assert }) => {
    assert.throws(() => assertProductionUrl('http://panel.example.com', 'production'), /APP_URL must use https/)
  })

  test('rejects an invalid url in production', ({ assert }) => {
    assert.throws(() => assertProductionUrl('panel', 'production'), /APP_URL must use https/)
  })

  test('accepts http outside production', ({ assert }) => {
    assert.doesNotThrow(() => assertProductionUrl('http://localhost:3333', 'development'))
    assert.doesNotThrow(() => assertProductionUrl('http://localhost:3333', 'test'))
  })
})
```

Dans `package.json`, ajouter l'alias `"#core/*": "./app/core/*.js"` et `"#modules/*": "./app/modules/*.js"` à `imports`.

Run: `node ace test --files tests/unit/core/production_checks.spec.ts`
Expected: FAIL, module `#core/production_checks` introuvable.

- [ ] **Step 3: Implémenter la garde, l'env et les connexions**

`app/core/production_checks.ts` :

```ts
export function assertProductionUrl(appUrl: string, nodeEnv: string): void {
  if (nodeEnv !== 'production') {
    return
  }
  let protocol = ''
  try {
    protocol = new URL(appUrl).protocol
  } catch {
    protocol = ''
  }
  if (protocol !== 'https:') {
    throw new Error('APP_URL must use https in production (Steam login and secure cookies need it)')
  }
}
```

`start/checks.ts` :

```ts
import env from '#start/env'
import { assertProductionUrl } from '#core/production_checks'

assertProductionUrl(env.get('APP_URL'), env.get('NODE_ENV'))
```

Ajouter `() => import('#start/checks')` aux `preloads` de `adonisrc.ts`.

`start/env.ts` :

```ts
import { Env } from '@adonisjs/core/env'

export default await Env.create(new URL('../', import.meta.url), {
  NODE_ENV: Env.schema.enum(['development', 'production', 'test'] as const),
  PORT: Env.schema.number(),
  HOST: Env.schema.string({ format: 'host' }),
  LOG_LEVEL: Env.schema.enum(['fatal', 'error', 'warn', 'info', 'debug', 'trace']),
  APP_KEY: Env.schema.string(),
  APP_URL: Env.schema.string(),
  SESSION_DRIVER: Env.schema.enum(['cookie'] as const),
  LIMITER_STORE: Env.schema.enum(['database', 'memory'] as const),

  PANEL_DB_HOST: Env.schema.string({ format: 'host' }),
  PANEL_DB_PORT: Env.schema.number(),
  PANEL_DB_USER: Env.schema.string(),
  PANEL_DB_PASSWORD: Env.schema.string.optional(),
  PANEL_DB_DATABASE: Env.schema.string(),

  RETAKE_DB_HOST: Env.schema.string.optional({ format: 'host' }),
  RETAKE_DB_PORT: Env.schema.number.optional(),
  RETAKE_DB_USER: Env.schema.string.optional(),
  RETAKE_DB_PASSWORD: Env.schema.string.optional(),
  RETAKE_DB_DATABASE: Env.schema.string.optional(),

  PANEL_RETAKE_SERVER: Env.schema.string.optional(),
  PANEL_MODULES: Env.schema.string.optional(),
  PANEL_ADMINS: Env.schema.string.optional(),
  STEAM_API_KEY: Env.schema.string.optional(),
  PANEL_LOCALE: Env.schema.enum.optional(['en', 'fr'] as const),
})
```

`config/database.ts` :

```ts
import env from '#start/env'
import { defineConfig } from '@adonisjs/lucid'

const retakeHost = env.get('RETAKE_DB_HOST')

const dbConfig = defineConfig({
  connection: 'panel',
  connections: {
    panel: {
      client: 'mysql2',
      connection: {
        host: env.get('PANEL_DB_HOST'),
        port: env.get('PANEL_DB_PORT'),
        user: env.get('PANEL_DB_USER'),
        password: env.get('PANEL_DB_PASSWORD'),
        database: env.get('PANEL_DB_DATABASE'),
      },
      migrations: { naturalSort: true, paths: ['database/migrations'] },
    },
    // The plugin's database: the panel never migrates it.
    ...(retakeHost
      ? {
          retake: {
            client: 'mysql2' as const,
            connection: {
              host: retakeHost,
              port: env.get('RETAKE_DB_PORT', 3306),
              user: env.get('RETAKE_DB_USER'),
              password: env.get('RETAKE_DB_PASSWORD'),
              database: env.get('RETAKE_DB_DATABASE'),
              supportBigNumbers: true,
              bigNumberStrings: true,
            },
          },
        }
      : {}),
  },
})

export default dbConfig
```

`config/session.ts` : garder la config du kit et fixer `cookieName: 'retakev4-panel'`, `age: '30d'`, `cookie: { path: '/', httpOnly: true, secure: app.inProduction, sameSite: 'lax' }`, `stores: { cookie: stores.cookie() }`.

`config/limiter.ts` :

```ts
import env from '#start/env'
import { defineConfig, stores } from '@adonisjs/limiter'

const limiterConfig = defineConfig({
  default: env.get('LIMITER_STORE'),
  stores: {
    database: stores.database({ connectionName: 'panel', tableName: 'rate_limits' }),
    memory: stores.memory({}),
  },
})

export default limiterConfig

declare module '@adonisjs/limiter/types' {
  export interface LimitersList extends InferLimiters<typeof limiterConfig> {}
}
```

Vérifier que la migration `rate_limits` générée par `node ace add @adonisjs/limiter` est dans `database/migrations`.

`config/shield.ts` : `csrf.enabled: true`, `xFrame: { enabled: true, action: 'DENY' }`, `hsts: { enabled: true, maxAge: '180 days' }`, `contentTypeSniffing: { enabled: true }` et :

```ts
  csp: {
    enabled: true,
    directives: {
      defaultSrc: [`'self'`],
      scriptSrc: [`'self'`, '@viteDevUrl'],
      styleSrc: [`'self'`, `'unsafe-inline'`, '@viteDevUrl'],
      imgSrc: [`'self'`, 'data:', 'https://avatars.steamstatic.com', 'https://avatars.akamai.steamstatic.com'],
      connectSrc: [`'self'`, '@viteHmrUrl'],
      formAction: [`'self'`, 'https://steamcommunity.com'],
    },
    reportOnly: false,
  },
```

`.env.example` :

```dotenv
NODE_ENV=production
PORT=3333
HOST=0.0.0.0
LOG_LEVEL=info
# Generate with: node ace generate:key --show
APP_KEY=
# Public URL, https in production
APP_URL=https://panel.example.com
SESSION_DRIVER=cookie
LIMITER_STORE=database

# Panel database (full rights on this database)
PANEL_DB_HOST=mysql
PANEL_DB_PORT=3306
PANEL_DB_USER=retake_panel
PANEL_DB_PASSWORD=
PANEL_DB_DATABASE=retake_panel

# RetakeV4 plugin database (SELECT/INSERT/UPDATE on player_loadout, SELECT on retake_catalog)
RETAKE_DB_HOST=mysql
RETAKE_DB_PORT=3306
RETAKE_DB_USER=retake_panel
RETAKE_DB_PASSWORD=
RETAKE_DB_DATABASE=retakev4

# Optional
PANEL_RETAKE_SERVER=default
PANEL_MODULES=loadouts
PANEL_ADMINS=
STEAM_API_KEY=
PANEL_LOCALE=en
```

`compose.test.yml` :

```yaml
services:
  mysql:
    image: mysql:8.4
    environment:
      MYSQL_ROOT_PASSWORD: root
      MYSQL_DATABASE: panel_test
    ports: ['3307:3306']
    command: --default-authentication-plugin=caching_sha2_password
    healthcheck:
      test: ['CMD', 'mysqladmin', 'ping', '-proot']
      interval: 2s
      retries: 30
```

`.env.test` :

```dotenv
NODE_ENV=test
PORT=3334
HOST=localhost
LOG_LEVEL=warn
APP_KEY=test-key-test-key-test-key-test-key
APP_URL=http://localhost:3334
SESSION_DRIVER=cookie
LIMITER_STORE=memory
PANEL_DB_HOST=127.0.0.1
PANEL_DB_PORT=3307
PANEL_DB_USER=root
PANEL_DB_PASSWORD=root
PANEL_DB_DATABASE=panel_test
RETAKE_DB_HOST=127.0.0.1
RETAKE_DB_PORT=3307
RETAKE_DB_USER=root
RETAKE_DB_PASSWORD=root
RETAKE_DB_DATABASE=panel_test
PANEL_RETAKE_SERVER=default
PANEL_MODULES=loadouts
PANEL_ADMINS=76561198000000009
```

Dans `package.json`, scripts : `"test": "node ace test"`, `"coverage": "c8 --reporter=text --reporter=lcov --include=app/** --check-coverage --lines 80 node ace test"`, `"typecheck": "tsc --noEmit"`.

- [ ] **Step 4: Lancer les tests**

```bash
docker compose -f compose.test.yml up -d --wait
node ace test --files tests/unit/core/production_checks.spec.ts
```

Expected: 4 tests réussis.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: squelette AdonisJS 7 (Vue/Inertia), deux connexions MySQL, sessions cookie, limiter en base, garde HTTPS en production"
```

---

### Task 2: Registre de modules

**Files:**
- Create: `app/core/modules/types.ts`, `app/core/modules/registry.ts`, `app/core/modules/catalog.ts`, `start/modules.ts`
- Create: `tests/unit/core/registry.spec.ts`
- Modify: `start/routes.ts`

**Interfaces:**
- Produces:
  - `type Permission = 'player' | 'admin'`
  - `interface MenuEntry { labelKey: string; href: string; permission: Permission }`
  - `interface PanelModule { name: string; connections: string[]; menu: MenuEntry[]; registerRoutes(): void }`
  - `interface ModuleResolution { active: PanelModule[]; disabled: { name: string; reason: string }[] }`
  - `parseModuleList(raw: string | undefined, fallback: string[]): string[]`
  - `resolveModules(enabled: string[], available: PanelModule[], readyConnections: ReadonlySet<string>): ModuleResolution`
  - `start/modules.ts` exporte `activeModules: PanelModule[]` (résolu au démarrage).
  - `app/core/modules/catalog.ts` exporte `availableModules: PanelModule[]` (vide jusqu'à la Task 7).

- [ ] **Step 1: Écrire les tests (RED)**

`tests/unit/core/registry.spec.ts` :

```ts
import { test } from '@japa/runner'
import { parseModuleList, resolveModules } from '#core/modules/registry'
import type { PanelModule } from '#core/modules/types'

const module = (name: string, connections: string[] = []): PanelModule => ({
  name,
  connections,
  menu: [],
  registerRoutes: () => {},
})

test.group('parseModuleList', () => {
  test('uses the fallback when empty', ({ assert }) => {
    assert.deepEqual(parseModuleList(undefined, ['loadouts']), ['loadouts'])
    assert.deepEqual(parseModuleList('  ', ['loadouts']), ['loadouts'])
  })

  test('splits, trims and deduplicates', ({ assert }) => {
    assert.deepEqual(parseModuleList(' loadouts, stats ,loadouts', []), ['loadouts', 'stats'])
  })
})

test.group('resolveModules', () => {
  test('activates enabled modules whose connections are ready', ({ assert }) => {
    const result = resolveModules(['loadouts'], [module('loadouts', ['retake'])], new Set(['panel', 'retake']))
    assert.deepEqual(result.active.map((m) => m.name), ['loadouts'])
    assert.deepEqual(result.disabled, [])
  })

  test('disables a module whose connection is missing', ({ assert }) => {
    const result = resolveModules(['loadouts'], [module('loadouts', ['retake'])], new Set(['panel']))
    assert.deepEqual(result.active, [])
    assert.deepEqual(result.disabled, [{ name: 'loadouts', reason: 'connection "retake" is not available' }])
  })

  test('reports unknown modules', ({ assert }) => {
    const result = resolveModules(['skins'], [module('loadouts')], new Set())
    assert.deepEqual(result.disabled, [{ name: 'skins', reason: 'unknown module' }])
  })

  test('ignores available modules that are not enabled', ({ assert }) => {
    const result = resolveModules([], [module('loadouts')], new Set())
    assert.deepEqual(result.active, [])
    assert.deepEqual(result.disabled, [])
  })
})
```

Run: `node ace test --files tests/unit/core/registry.spec.ts`
Expected: FAIL, modules introuvables.

- [ ] **Step 2: Implémenter**

`app/core/modules/types.ts` :

```ts
export type Permission = 'player' | 'admin'

export interface MenuEntry {
  labelKey: string
  href: string
  permission: Permission
}

export interface PanelModule {
  name: string
  connections: string[]
  menu: MenuEntry[]
  registerRoutes(): void
}

export interface ModuleResolution {
  active: PanelModule[]
  disabled: { name: string; reason: string }[]
}
```

`app/core/modules/registry.ts` :

```ts
import type { ModuleResolution, PanelModule } from '#core/modules/types'

export function parseModuleList(raw: string | undefined, fallback: string[]): string[] {
  const names = (raw ?? '').split(',').map((name) => name.trim()).filter((name) => name.length > 0)
  return names.length === 0 ? fallback : [...new Set(names)]
}

export function resolveModules(
  enabled: string[],
  available: PanelModule[],
  readyConnections: ReadonlySet<string>
): ModuleResolution {
  return enabled.reduce<ModuleResolution>(
    (result, name) => {
      const module = available.find((candidate) => candidate.name === name)
      if (!module) {
        return { ...result, disabled: [...result.disabled, { name, reason: 'unknown module' }] }
      }
      const missing = module.connections.find((connection) => !readyConnections.has(connection))
      if (missing) {
        return { ...result, disabled: [...result.disabled, { name, reason: `connection "${missing}" is not available` }] }
      }
      return { ...result, active: [...result.active, module] }
    },
    { active: [], disabled: [] }
  )
}
```

`app/core/modules/catalog.ts` :

```ts
import type { PanelModule } from '#core/modules/types'

export const availableModules: PanelModule[] = []
```

`start/modules.ts` :

```ts
import db from '@adonisjs/lucid/services/db'
import logger from '@adonisjs/core/services/logger'
import env from '#start/env'
import dbConfig from '#config/database'
import { availableModules } from '#core/modules/catalog'
import { parseModuleList, resolveModules } from '#core/modules/registry'

const CONNECTION_TIMEOUT_MS = 5000

async function isReachable(name: string): Promise<boolean> {
  const probe = db.connection(name).rawQuery('SELECT 1')
  const timeout = new Promise<never>((_, reject) => setTimeout(() => reject(new Error('timeout')), CONNECTION_TIMEOUT_MS))
  try {
    await Promise.race([probe, timeout])
    return true
  } catch (error) {
    logger.warn({ err: error, connection: name }, 'database connection %s is not reachable', name)
    return false
  }
}

const configured = Object.keys(dbConfig.connections)
const reachable = await Promise.all(configured.map(async (name) => ((await isReachable(name)) ? name : null)))
const resolution = resolveModules(
  parseModuleList(env.get('PANEL_MODULES'), ['loadouts']),
  availableModules,
  new Set(reachable.filter((name): name is string => name !== null))
)

for (const { name, reason } of resolution.disabled) {
  logger.warn('module %s disabled: %s', name, reason)
}

export const activeModules = resolution.active
```

Dans `start/routes.ts`, après les routes du cœur :

```ts
import { activeModules } from '#start/modules'

for (const module of activeModules) {
  module.registerRoutes()
}
```

- [ ] **Step 3: Lancer les tests**

Run: `node ace test --files tests/unit/core/registry.spec.ts`
Expected: 6 tests réussis.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: registre de modules activés par configuration, désactivés si une connexion manque"
```

---

### Task 3: Permissions, connexion Steam et joueurs

**Files:**
- Create: `app/core/permissions.ts`, `app/core/steam/steam_id.ts`, `app/core/steam/openid.ts`, `app/core/steam/steam_openid_service.ts`, `app/core/steam/steam_profiles.ts`
- Create: `app/models/player.ts`, `database/migrations/1700000000000_create_players_table.ts`
- Create: `app/controllers/auth_controller.ts`, `start/limiter.ts`
- Modify: `config/auth.ts`, `start/routes.ts`, `tests/bootstrap.ts`
- Create: `tests/unit/core/permissions.spec.ts`, `tests/unit/core/openid.spec.ts`, `tests/unit/core/steam_profiles.spec.ts`, `tests/functional/auth.spec.ts`

**Interfaces:**
- Consumes: `Permission` (Task 2).
- Produces:
  - `parseAdmins(raw: string | undefined): ReadonlySet<string>`, `can(steamId: string | null, permission: Permission, admins: ReadonlySet<string>): boolean`
  - `isSteamId64(value: unknown): value is string`
  - `STEAM_OPENID_ENDPOINT`, `buildLoginUrl(returnTo: string, realm: string): string`, `verifyAssertion(query: Record<string, unknown>, expectedReturnTo: string, checkAuthentication: (body: URLSearchParams) => Promise<string>): Promise<string | null>`
  - classe `SteamOpenId` (injectable) : `loginUrl(): string`, `verify(query: Record<string, unknown>): Promise<string | null>`
  - `fetchProfile(steamId: string, apiKey: string | undefined, fetchFn: typeof fetch): Promise<{ name: string; avatarUrl: string } | null>`
  - modèle `Player` : `steamId: string` (PK), `displayName: string | null`, `avatarUrl: string | null`, `lastLoginAt: DateTime`
  - limiters `loginThrottle`, `writeThrottle` dans `start/limiter.ts`
  - routes : `GET /login` (`auth.login`), `GET /auth/steam/callback` (`auth.callback`), `POST /logout` (`auth.logout`)

- [ ] **Step 1: Écrire les tests unitaires (RED)**

`tests/unit/core/permissions.spec.ts` :

```ts
import { test } from '@japa/runner'
import { can, parseAdmins } from '#core/permissions'

const ADMIN = '76561198000000009'
const PLAYER = '76561198000000001'

test.group('permissions', () => {
  test('parses admins and drops invalid ids', ({ assert }) => {
    assert.deepEqual([...parseAdmins(` ${ADMIN}, nope, 123,${ADMIN}`)], [ADMIN])
    assert.equal(parseAdmins(undefined).size, 0)
  })

  test('players need a session', ({ assert }) => {
    assert.isFalse(can(null, 'player', new Set()))
    assert.isTrue(can(PLAYER, 'player', new Set()))
  })

  test('admins are listed', ({ assert }) => {
    const admins = parseAdmins(ADMIN)
    assert.isTrue(can(ADMIN, 'admin', admins))
    assert.isFalse(can(PLAYER, 'admin', admins))
    assert.isFalse(can(null, 'admin', admins))
  })
})
```

`tests/unit/core/openid.spec.ts` :

```ts
import { test } from '@japa/runner'
import { STEAM_OPENID_ENDPOINT, buildLoginUrl, verifyAssertion } from '#core/steam/openid'

const RETURN_TO = 'https://panel.example.com/auth/steam/callback'
const STEAM_ID = '76561198000000001'

const assertion = (overrides: Record<string, string> = {}) => ({
  'openid.ns': 'http://specs.openid.net/auth/2.0',
  'openid.mode': 'id_res',
  'openid.op_endpoint': STEAM_OPENID_ENDPOINT,
  'openid.claimed_id': `https://steamcommunity.com/openid/id/${STEAM_ID}`,
  'openid.identity': `https://steamcommunity.com/openid/id/${STEAM_ID}`,
  'openid.return_to': RETURN_TO,
  'openid.response_nonce': '2026-10-01T12:00:00Zabc',
  'openid.assoc_handle': '1234567890',
  'openid.signed': 'signed,op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle',
  'openid.sig': 'c2lnbmF0dXJl',
  ...overrides,
})

const valid = async () => 'ns:http://specs.openid.net/auth/2.0\nis_valid:true\n'
const invalid = async () => 'ns:http://specs.openid.net/auth/2.0\nis_valid:false\n'

test.group('Steam OpenID', () => {
  test('builds the login url', ({ assert }) => {
    const url = new URL(buildLoginUrl(RETURN_TO, 'https://panel.example.com'))
    assert.equal(url.origin + url.pathname, STEAM_OPENID_ENDPOINT)
    assert.equal(url.searchParams.get('openid.mode'), 'checkid_setup')
    assert.equal(url.searchParams.get('openid.return_to'), RETURN_TO)
    assert.equal(url.searchParams.get('openid.realm'), 'https://panel.example.com')
    assert.equal(url.searchParams.get('openid.identity'), 'http://specs.openid.net/auth/2.0/identifier_select')
  })

  test('returns the SteamID of a valid assertion', async ({ assert }) => {
    assert.equal(await verifyAssertion(assertion(), RETURN_TO, valid), STEAM_ID)
  })

  test('sends the assertion back with check_authentication', async ({ assert }) => {
    let sent: URLSearchParams | undefined
    await verifyAssertion(assertion(), RETURN_TO, async (body) => {
      sent = body
      return 'is_valid:true'
    })
    assert.equal(sent?.get('openid.mode'), 'check_authentication')
    assert.equal(sent?.get('openid.sig'), 'c2lnbmF0dXJl')
  })

  test('rejects when Steam says the signature is invalid', async ({ assert }) => {
    assert.isNull(await verifyAssertion(assertion(), RETURN_TO, invalid))
  })

  test('rejects a forged return_to', async ({ assert }) => {
    assert.isNull(await verifyAssertion(assertion({ 'openid.return_to': 'https://evil.example.com/cb' }), RETURN_TO, valid))
  })

  test('rejects a foreign claimed_id', async ({ assert }) => {
    const foreign = assertion({ 'openid.claimed_id': `https://evil.example.com/openid/id/${STEAM_ID}` })
    assert.isNull(await verifyAssertion(foreign, RETURN_TO, valid))
  })

  test('rejects a foreign op_endpoint', async ({ assert }) => {
    assert.isNull(await verifyAssertion(assertion({ 'openid.op_endpoint': 'https://evil.example.com/openid/login' }), RETURN_TO, valid))
  })

  test('rejects a cancelled login', async ({ assert }) => {
    assert.isNull(await verifyAssertion(assertion({ 'openid.mode': 'cancel' }), RETURN_TO, valid))
  })

  test('rejects when Steam cannot be reached', async ({ assert }) => {
    const failing = async () => {
      throw new Error('network')
    }
    assert.isNull(await verifyAssertion(assertion(), RETURN_TO, failing))
  })
})
```

`tests/unit/core/steam_profiles.spec.ts` :

```ts
import { test } from '@japa/runner'
import { fetchProfile } from '#core/steam/steam_profiles'

const STEAM_ID = '76561198000000001'

const respond = (body: unknown, ok = true): typeof fetch =>
  (async () => ({ ok, json: async () => body }) as Response) as typeof fetch

test.group('fetchProfile', () => {
  test('returns name and avatar', async ({ assert }) => {
    const body = { response: { players: [{ steamid: STEAM_ID, personaname: 'Alice', avatarfull: 'https://avatars.steamstatic.com/a.jpg' }] } }
    assert.deepEqual(await fetchProfile(STEAM_ID, 'key', respond(body)), { name: 'Alice', avatarUrl: 'https://avatars.steamstatic.com/a.jpg' })
  })

  test('returns null without an api key', async ({ assert }) => {
    assert.isNull(await fetchProfile(STEAM_ID, undefined, respond({})))
  })

  test('returns null on an error response or unexpected body', async ({ assert }) => {
    assert.isNull(await fetchProfile(STEAM_ID, 'key', respond({}, false)))
    assert.isNull(await fetchProfile(STEAM_ID, 'key', respond({ response: { players: [] } })))
  })

  test('returns null when the request throws', async ({ assert }) => {
    const failing = (async () => {
      throw new Error('network')
    }) as typeof fetch
    assert.isNull(await fetchProfile(STEAM_ID, 'key', failing))
  })
})
```

Run: `node ace test --files tests/unit/core`
Expected: FAIL, modules introuvables.

- [ ] **Step 2: Implémenter la logique pure**

`app/core/steam/steam_id.ts` :

```ts
const STEAM_ID_64 = /^\d{17}$/

export function isSteamId64(value: unknown): value is string {
  return typeof value === 'string' && STEAM_ID_64.test(value)
}
```

`app/core/permissions.ts` :

```ts
import type { Permission } from '#core/modules/types'
import { isSteamId64 } from '#core/steam/steam_id'

export function parseAdmins(raw: string | undefined): ReadonlySet<string> {
  return new Set((raw ?? '').split(',').map((id) => id.trim()).filter(isSteamId64))
}

export function can(steamId: string | null, permission: Permission, admins: ReadonlySet<string>): boolean {
  if (steamId === null) {
    return false
  }
  return permission === 'player' || admins.has(steamId)
}
```

`app/core/steam/openid.ts` :

```ts
export const STEAM_OPENID_ENDPOINT = 'https://steamcommunity.com/openid/login'

const OPENID_NS = 'http://specs.openid.net/auth/2.0'
const IDENTIFIER_SELECT = 'http://specs.openid.net/auth/2.0/identifier_select'
const CLAIMED_ID = /^https:\/\/steamcommunity\.com\/openid\/id\/(\d{17})$/

export function buildLoginUrl(returnTo: string, realm: string): string {
  const params = new URLSearchParams({
    'openid.ns': OPENID_NS,
    'openid.mode': 'checkid_setup',
    'openid.return_to': returnTo,
    'openid.realm': realm,
    'openid.identity': IDENTIFIER_SELECT,
    'openid.claimed_id': IDENTIFIER_SELECT,
  })
  return `${STEAM_OPENID_ENDPOINT}?${params.toString()}`
}

function text(query: Record<string, unknown>, key: string): string | null {
  const value = query[key]
  return typeof value === 'string' ? value : null
}

export async function verifyAssertion(
  query: Record<string, unknown>,
  expectedReturnTo: string,
  checkAuthentication: (body: URLSearchParams) => Promise<string>
): Promise<string | null> {
  if (text(query, 'openid.mode') !== 'id_res') return null
  if (text(query, 'openid.op_endpoint') !== STEAM_OPENID_ENDPOINT) return null
  if (text(query, 'openid.return_to') !== expectedReturnTo) return null
  const steamId = CLAIMED_ID.exec(text(query, 'openid.claimed_id') ?? '')?.[1]
  if (!steamId) return null

  const body = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (key.startsWith('openid.') && typeof value === 'string') {
      body.set(key, value)
    }
  }
  body.set('openid.mode', 'check_authentication')
  try {
    const answer = await checkAuthentication(body)
    return answer.split('\n').some((line) => line.trim() === 'is_valid:true') ? steamId : null
  } catch {
    return null
  }
}
```

`app/core/steam/steam_profiles.ts` :

```ts
const SUMMARIES_URL = 'https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/'

export interface SteamProfile {
  name: string
  avatarUrl: string
}

export async function fetchProfile(steamId: string, apiKey: string | undefined, fetchFn: typeof fetch): Promise<SteamProfile | null> {
  if (!apiKey) return null
  try {
    const url = `${SUMMARIES_URL}?${new URLSearchParams({ key: apiKey, steamids: steamId }).toString()}`
    const response = await fetchFn(url, { signal: AbortSignal.timeout(5000) })
    if (!response.ok) return null
    const body = (await response.json()) as { response?: { players?: { personaname?: unknown; avatarfull?: unknown }[] } }
    const player = body.response?.players?.[0]
    if (typeof player?.personaname !== 'string' || typeof player.avatarfull !== 'string') return null
    return { name: player.personaname, avatarUrl: player.avatarfull }
  } catch {
    return null
  }
}
```

- [ ] **Step 3: Lancer les tests unitaires**

Run: `node ace test --files tests/unit/core`
Expected: tous réussis.

- [ ] **Step 4: Écrire les tests fonctionnels (RED)**

`tests/functional/auth.spec.ts` :

```ts
import { test } from '@japa/runner'
import app from '@adonisjs/core/services/app'
import testUtils from '@adonisjs/core/services/test_utils'
import Player from '#models/player'
import { SteamOpenId } from '#core/steam/steam_openid_service'

const STEAM_ID = '76561198000000001'

class FakeSteamOpenId extends SteamOpenId {
  constructor(private readonly result: string | null) {
    super()
  }
  loginUrl() {
    return 'https://steamcommunity.com/openid/login?fake=1'
  }
  async verify() {
    return this.result
  }
}

test.group('Steam login', (group) => {
  group.each.setup(() => testUtils.db('panel').withGlobalTransaction())
  group.each.teardown(() => app.container.restore(SteamOpenId))

  test('redirects to Steam', async ({ client }) => {
    const response = await client.get('/login').redirects(0)
    response.assertStatus(302)
    response.assertHeader('location', 'https://steamcommunity.com/openid/login?fake=1')
  }).setup(() => app.container.swap(SteamOpenId, () => new FakeSteamOpenId(null)))

  test('a valid callback logs the player in and records him', async ({ client, assert }) => {
    app.container.swap(SteamOpenId, () => new FakeSteamOpenId(STEAM_ID))
    const response = await client.get('/auth/steam/callback').redirects(0)
    response.assertStatus(302)
    response.assertHeader('location', '/')
    const player = await Player.findOrFail(STEAM_ID)
    assert.equal(player.steamId, STEAM_ID)
  })

  test('an invalid callback does not log in', async ({ client, assert }) => {
    app.container.swap(SteamOpenId, () => new FakeSteamOpenId(null))
    const response = await client.get('/auth/steam/callback').redirects(0)
    response.assertStatus(302)
    response.assertHeader('location', '/')
    assert.isNull(await Player.find(STEAM_ID))
  })

  test('logout with a csrf token ends the session', async ({ client }) => {
    const player = await Player.create({ steamId: STEAM_ID })
    const response = await client.post('/logout').loginAs(player).withCsrfToken().redirects(0)
    response.assertStatus(302)
  })
})
```

`tests/bootstrap.ts` : garder les plugins du kit et s'assurer de la présence de `assert()`, `pluginAdonisJS(app)`, `apiClient()`, `sessionApiClient(app)` (`@adonisjs/session/plugins/api_client`), `shieldApiClient()` (`@adonisjs/shield/plugins/api_client`), `authApiClient(app)` (`@adonisjs/auth/plugins/api_client`), `inertiaApiClient(app)` (`@adonisjs/inertia/plugins/api_client`). Hooks :

```ts
export const runnerHooks: Required<Pick<Config, 'setup' | 'teardown'>> = {
  setup: [() => testUtils.db('panel').migrate()],
  teardown: [],
}
```

Run: `node ace test --files tests/functional/auth.spec.ts`
Expected: FAIL (modèle, service, routes absents).

- [ ] **Step 5: Implémenter modèle, service, contrôleur, routes**

`database/migrations/1700000000000_create_players_table.ts` :

```ts
import { BaseSchema } from '@adonisjs/lucid/schema'

export default class extends BaseSchema {
  protected tableName = 'players'

  async up() {
    this.schema.createTable(this.tableName, (table) => {
      table.string('steam_id', 17).primary()
      table.string('display_name', 64).nullable()
      table.string('avatar_url', 255).nullable()
      table.timestamp('last_login_at').notNullable()
    })
  }

  async down() {
    this.schema.dropTable(this.tableName)
  }
}
```

`app/models/player.ts` :

```ts
import { DateTime } from 'luxon'
import { BaseModel, column } from '@adonisjs/lucid/orm'

export default class Player extends BaseModel {
  static connection = 'panel'
  static selfAssignPrimaryKey = true

  @column({ isPrimary: true })
  declare steamId: string

  @column()
  declare displayName: string | null

  @column()
  declare avatarUrl: string | null

  @column.dateTime({ autoCreate: true })
  declare lastLoginAt: DateTime
}
```

`config/auth.ts` :

```ts
import { defineConfig } from '@adonisjs/auth'
import { sessionGuard, sessionUserProvider } from '@adonisjs/auth/session'

const authConfig = defineConfig({
  default: 'web',
  guards: {
    web: sessionGuard({
      useRememberMeTokens: false,
      provider: sessionUserProvider({ model: () => import('#models/player') }),
    }),
  },
})

export default authConfig
```

`app/core/steam/steam_openid_service.ts` :

```ts
import env from '#start/env'
import { buildLoginUrl, STEAM_OPENID_ENDPOINT, verifyAssertion } from '#core/steam/openid'

export class SteamOpenId {
  private callbackUrl(): string {
    return new URL('/auth/steam/callback', env.get('APP_URL')).toString()
  }

  loginUrl(): string {
    return buildLoginUrl(this.callbackUrl(), new URL(env.get('APP_URL')).origin)
  }

  async verify(query: Record<string, unknown>): Promise<string | null> {
    return verifyAssertion(query, this.callbackUrl(), async (body) => {
      const response = await fetch(STEAM_OPENID_ENDPOINT, { method: 'POST', body, signal: AbortSignal.timeout(10000) })
      return response.text()
    })
  }
}
```

`start/limiter.ts` :

```ts
import limiter from '@adonisjs/limiter/services/main'

export const loginThrottle = limiter.define('login', (ctx) =>
  limiter.allowRequests(10).every('1 minute').usingKey(`login_${ctx.request.ip()}`)
)

export const writeThrottle = limiter.define('writes', (ctx) =>
  limiter.allowRequests(30).every('1 minute').usingKey(`write_${ctx.auth.user?.steamId ?? ctx.request.ip()}`)
)
```

`app/controllers/auth_controller.ts` :

```ts
import { inject } from '@adonisjs/core'
import { DateTime } from 'luxon'
import type { HttpContext } from '@adonisjs/core/http'
import env from '#start/env'
import Player from '#models/player'
import { SteamOpenId } from '#core/steam/steam_openid_service'
import { fetchProfile } from '#core/steam/steam_profiles'

@inject()
export default class AuthController {
  constructor(private readonly steam: SteamOpenId) {}

  async login({ response }: HttpContext) {
    return response.redirect(this.steam.loginUrl())
  }

  async callback({ request, response, auth, session }: HttpContext) {
    const steamId = await this.steam.verify(request.qs())
    if (!steamId) {
      session.flash('error', 'core.login.failed')
      return response.redirect('/')
    }
    const profile = await fetchProfile(steamId, env.get('STEAM_API_KEY'), fetch)
    const player = await Player.updateOrCreate(
      { steamId },
      {
        lastLoginAt: DateTime.utc(),
        ...(profile ? { displayName: profile.name, avatarUrl: profile.avatarUrl } : {}),
      }
    )
    await auth.use('web').login(player)
    return response.redirect('/')
  }

  async logout({ response, auth }: HttpContext) {
    await auth.use('web').logout()
    return response.redirect('/')
  }
}
```

Dans `start/routes.ts` :

```ts
import router from '@adonisjs/core/services/router'
import { loginThrottle } from '#start/limiter'

const AuthController = () => import('#controllers/auth_controller')

router.get('/login', [AuthController, 'login']).as('auth.login').use(loginThrottle)
router.get('/auth/steam/callback', [AuthController, 'callback']).as('auth.callback').use(loginThrottle)
router.post('/logout', [AuthController, 'logout']).as('auth.logout')
```

- [ ] **Step 6: Lancer les tests**

Run: `node ace test --files tests/functional/auth.spec.ts` puis `node ace test`
Expected: tous réussis.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: connexion Steam OpenID vérifiée auprès de Steam, joueurs, permissions admin par SteamID, rate-limit de connexion"
```

---

### Task 4: Layout, langue et accueil

**Files:**
- Create: `app/core/locale.ts`, `inertia/i18n/en.ts`, `inertia/i18n/fr.ts`, `inertia/i18n/index.ts`
- Create: `app/controllers/home_controller.ts`, `app/controllers/locale_controller.ts`
- Modify: `app/middleware/inertia_middleware.ts`, `start/routes.ts`
- Create: `inertia/layouts/default.vue`, `inertia/pages/home.vue`
- Create: `tests/unit/core/locale.spec.ts`, `tests/unit/core/i18n.spec.ts`, `tests/functional/home.spec.ts`

**Interfaces:**
- Consumes: `activeModules` (Task 2), `can`, `parseAdmins` (Task 3), `Player`.
- Produces:
  - `type Locale = 'en' | 'fr'`, `LOCALE_COOKIE = 'panel_locale'`, `resolveLocale(cookie: unknown, acceptLanguage: string | undefined, fallback: Locale): Locale`
  - `translate(locale: Locale, key: string, params?: Record<string, string | number>): string`, `messages: Record<Locale, Record<string, string>>`
  - props partagées Inertia : `locale: Locale`, `user: { steamId: string; name: string; avatarUrl: string | null } | null`, `menu: { labelKey: string; href: string }[]`, `flash: { error?: string; success?: string }`
  - `POST /locale` (`locale.update`, corps `{ locale }`)

- [ ] **Step 1: Écrire les tests (RED)**

`tests/unit/core/locale.spec.ts` :

```ts
import { test } from '@japa/runner'
import { resolveLocale } from '#core/locale'

test.group('resolveLocale', () => {
  test('the cookie wins', ({ assert }) => {
    assert.equal(resolveLocale('fr', 'en-US,en;q=0.9', 'en'), 'fr')
  })

  test('then the browser language', ({ assert }) => {
    assert.equal(resolveLocale(undefined, 'fr-FR,fr;q=0.9,en;q=0.8', 'en'), 'fr')
    assert.equal(resolveLocale('de', 'de-DE,en;q=0.5', 'fr'), 'en')
  })

  test('then the fallback', ({ assert }) => {
    assert.equal(resolveLocale(undefined, undefined, 'fr'), 'fr')
    assert.equal(resolveLocale(42, 'de-DE', 'en'), 'en')
  })
})
```

`tests/unit/core/i18n.spec.ts` :

```ts
import { test } from '@japa/runner'
import { messages, translate } from '../../../inertia/i18n/index.js'

test.group('i18n', () => {
  test('en and fr have the same keys', ({ assert }) => {
    assert.deepEqual(Object.keys(messages.fr).sort(), Object.keys(messages.en).sort())
  })

  test('replaces parameters', ({ assert }) => {
    assert.equal(translate('en', 'core.welcome', { name: 'Alice' }), 'Welcome, Alice')
  })

  test('falls back to the key when missing', ({ assert }) => {
    assert.equal(translate('fr', 'missing.key'), 'missing.key')
  })
})
```

`tests/functional/home.spec.ts` :

```ts
import { test } from '@japa/runner'
import testUtils from '@adonisjs/core/services/test_utils'
import Player from '#models/player'

test.group('Home', (group) => {
  group.each.setup(() => testUtils.db('panel').withGlobalTransaction())

  test('guests see the login button and no menu', async ({ client }) => {
    const response = await client.get('/').withInertia()
    response.assertStatus(200)
    response.assertInertiaComponent('home')
    response.assertInertiaPropsContains({ user: null, menu: [] })
  })

  test('players see their menu', async ({ client }) => {
    const player = await Player.create({ steamId: '76561198000000001' })
    const response = await client.get('/').loginAs(player).withInertia()
    response.assertInertiaPropsContains({ user: { steamId: '76561198000000001' } })
  })

  test('the locale cookie is honoured', async ({ client }) => {
    const response = await client.post('/locale').withCsrfToken().form({ locale: 'fr' }).redirects(0)
    response.assertStatus(302)
    response.assertCookie('panel_locale', 'fr')
  })

  test('an unknown locale is rejected', async ({ client }) => {
    const response = await client.post('/locale').withCsrfToken().form({ locale: 'de' }).redirects(0)
    response.assertStatus(302)
    response.assertCookieMissing('panel_locale')
  })
})
```

Run: `node ace test --files tests/unit/core/locale.spec.ts,tests/unit/core/i18n.spec.ts,tests/functional/home.spec.ts`
Expected: FAIL.

- [ ] **Step 2: Implémenter la langue**

`app/core/locale.ts` :

```ts
export type Locale = 'en' | 'fr'

export const LOCALES: readonly Locale[] = ['en', 'fr']
export const LOCALE_COOKIE = 'panel_locale'

export function isLocale(value: unknown): value is Locale {
  return typeof value === 'string' && (LOCALES as readonly string[]).includes(value)
}

export function resolveLocale(cookie: unknown, acceptLanguage: string | undefined, fallback: Locale): Locale {
  if (isLocale(cookie)) return cookie
  const preferred = (acceptLanguage ?? '')
    .split(',')
    .map((part) => part.split(';')[0].trim().slice(0, 2).toLowerCase())
    .find(isLocale)
  return preferred ?? fallback
}
```

`inertia/i18n/en.ts` :

```ts
export default {
  'core.title': 'Retake panel',
  'core.welcome': 'Welcome, {name}',
  'core.login': 'Sign in through Steam',
  'core.logout': 'Sign out',
  'core.login.failed': 'Steam sign-in failed, please try again.',
  'core.home.intro': 'Set up your retake loadouts from your browser.',
  'core.language': 'Language',
  'loadouts.menu': 'My weapons',
  'loadouts.title': 'My weapons',
  'loadouts.team.T': 'Terrorists',
  'loadouts.team.CT': 'Counter-Terrorists',
  'loadouts.primary': 'Primary',
  'loadouts.secondary': 'Pistol',
  'loadouts.default': 'Default',
  'loadouts.reset': 'Reset to default',
  'loadouts.awp': 'AWP volunteer',
  'loadouts.awp.help': 'You may receive the AWP on rounds that hand it out.',
  'loadouts.saved': 'Saved, applied from the next round.',
  'loadouts.no_choice': 'No choice on this round type.',
  'loadouts.catalog.missing': 'The server has not published its weapons yet.',
  'loadouts.catalog.unsupported': 'This panel does not support the catalog published by the server (format {version}). Update the panel.',
  'loadouts.catalog.invalid': 'The catalog published by the server is unreadable.',
  'loadouts.unavailable': 'The weapons database is unreachable right now.',
} as const
```

`inertia/i18n/fr.ts` :

```ts
export default {
  'core.title': 'Panel retake',
  'core.welcome': 'Bienvenue, {name}',
  'core.login': 'Se connecter avec Steam',
  'core.logout': 'Se déconnecter',
  'core.login.failed': 'La connexion Steam a échoué, réessaie.',
  'core.home.intro': 'Règle tes armes de retake depuis ton navigateur.',
  'core.language': 'Langue',
  'loadouts.menu': 'Mes armes',
  'loadouts.title': 'Mes armes',
  'loadouts.team.T': 'Terroristes',
  'loadouts.team.CT': 'Antiterroristes',
  'loadouts.primary': 'Arme principale',
  'loadouts.secondary': 'Pistolet',
  'loadouts.default': 'Par défaut',
  'loadouts.reset': 'Revenir au défaut',
  'loadouts.awp': 'Volontaire AWP',
  'loadouts.awp.help': "Tu peux recevoir l'AWP sur les rounds qui en distribuent.",
  'loadouts.saved': 'Enregistré, appliqué au prochain round.',
  'loadouts.no_choice': 'Pas de choix sur ce type de round.',
  'loadouts.catalog.missing': "Le serveur n'a pas encore publié ses armes.",
  'loadouts.catalog.unsupported': 'Ce panel ne gère pas le catalogue publié par le serveur (format {version}). Mets le panel à jour.',
  'loadouts.catalog.invalid': 'Le catalogue publié par le serveur est illisible.',
  'loadouts.unavailable': "La base des armes est injoignable pour l'instant.",
} as const
```

`inertia/i18n/index.ts` :

```ts
import en from './en.js'
import fr from './fr.js'

export type Locale = 'en' | 'fr'

export const messages: Record<Locale, Record<string, string>> = { en, fr }

export function translate(locale: Locale, key: string, params: Record<string, string | number> = {}): string {
  const template = messages[locale][key] ?? key
  return template.replace(/\{(\w+)\}/g, (match, name: string) => (name in params ? String(params[name]) : match))
}
```

- [ ] **Step 3: Props partagées, contrôleurs et routes**

`app/middleware/inertia_middleware.ts` (garder `handle` et l'augmentation de types du kit) :

```ts
import type { HttpContext } from '@adonisjs/core/http'
import BaseInertiaMiddleware from '@adonisjs/inertia/inertia_middleware'
import env from '#start/env'
import { activeModules } from '#start/modules'
import { can, parseAdmins } from '#core/permissions'
import { LOCALE_COOKIE, resolveLocale } from '#core/locale'

const admins = parseAdmins(env.get('PANEL_ADMINS'))

export default class InertiaMiddleware extends BaseInertiaMiddleware {
  share(ctx: HttpContext) {
    const { session, auth } = ctx as Partial<HttpContext>
    const player = auth?.user ?? null
    const steamId = player?.steamId ?? null
    return {
      errors: ctx.inertia.always(this.getValidationErrors(ctx)),
      flash: ctx.inertia.always({
        error: session?.flashMessages.get('error') as string | undefined,
        success: session?.flashMessages.get('success') as string | undefined,
      }),
      locale: ctx.inertia.always(
        resolveLocale(ctx.request.cookie(LOCALE_COOKIE), ctx.request.header('accept-language'), env.get('PANEL_LOCALE', 'en'))
      ),
      user: ctx.inertia.always(
        player ? { steamId: player.steamId, name: player.displayName ?? player.steamId, avatarUrl: player.avatarUrl } : null
      ),
      menu: ctx.inertia.always(
        activeModules
          .flatMap((module) => module.menu)
          .filter((entry) => can(steamId, entry.permission, admins))
          .map(({ labelKey, href }) => ({ labelKey, href }))
      ),
    }
  }
}
```

Le middleware `silent_auth` du kit doit être dans le kernel (router middleware) pour que `auth.user` soit renseigné sur toutes les pages ; l'ajouter s'il manque.

`app/controllers/home_controller.ts` :

```ts
import type { HttpContext } from '@adonisjs/core/http'

export default class HomeController {
  async show({ inertia }: HttpContext) {
    return inertia.render('home', {})
  }
}
```

`app/controllers/locale_controller.ts` :

```ts
import type { HttpContext } from '@adonisjs/core/http'
import { isLocale, LOCALE_COOKIE } from '#core/locale'

export default class LocaleController {
  async update({ request, response }: HttpContext) {
    const locale = request.input('locale')
    if (isLocale(locale)) {
      response.cookie(LOCALE_COOKIE, locale, { maxAge: '1y', httpOnly: true, sameSite: 'lax' })
    }
    return response.redirect().back()
  }
}
```

`start/routes.ts`, ajouter :

```ts
const HomeController = () => import('#controllers/home_controller')
const LocaleController = () => import('#controllers/locale_controller')

router.get('/', [HomeController, 'show']).as('home')
router.post('/locale', [LocaleController, 'update']).as('locale.update')
```

- [ ] **Step 4: Layout et accueil (Vue)**

`inertia/layouts/default.vue` :

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { router, usePage } from '@inertiajs/vue3'
import { translate, type Locale } from '../i18n/index.js'

const page = usePage<{
  locale: Locale
  user: { steamId: string; name: string; avatarUrl: string | null } | null
  menu: { labelKey: string; href: string }[]
  flash: { error?: string; success?: string }
}>()
const t = (key: string, params?: Record<string, string | number>) => translate(page.props.locale, key, params)
const flashError = computed(() => (page.props.flash.error ? t(page.props.flash.error) : null))

function switchLocale(locale: Locale) {
  router.post('/locale', { locale }, { preserveScroll: true })
}

function logout() {
  router.post('/logout')
}
</script>

<template>
  <div class="shell">
    <header class="topbar">
      <a href="/" class="brand">{{ t('core.title') }}</a>
      <nav class="menu">
        <a v-for="entry in page.props.menu" :key="entry.href" :href="entry.href">{{ t(entry.labelKey) }}</a>
      </nav>
      <div class="account">
        <button type="button" :aria-pressed="page.props.locale === 'en'" @click="switchLocale('en')">EN</button>
        <button type="button" :aria-pressed="page.props.locale === 'fr'" @click="switchLocale('fr')">FR</button>
        <template v-if="page.props.user">
          <img v-if="page.props.user.avatarUrl" :src="page.props.user.avatarUrl" alt="" class="avatar" />
          <span>{{ page.props.user.name }}</span>
          <button type="button" @click="logout">{{ t('core.logout') }}</button>
        </template>
        <a v-else href="/login" class="button">{{ t('core.login') }}</a>
      </div>
    </header>
    <p v-if="flashError" class="alert" role="alert">{{ flashError }}</p>
    <main class="content"><slot /></main>
  </div>
</template>
```

`inertia/pages/home.vue` :

```vue
<script setup lang="ts">
import { usePage } from '@inertiajs/vue3'
import DefaultLayout from '../layouts/default.vue'
import { translate, type Locale } from '../i18n/index.js'

const page = usePage<{ locale: Locale; user: { name: string } | null }>()
const t = (key: string, params?: Record<string, string | number>) => translate(page.props.locale, key, params)
</script>

<template>
  <DefaultLayout>
    <h1 v-if="page.props.user">{{ t('core.welcome', { name: page.props.user.name }) }}</h1>
    <h1 v-else>{{ t('core.title') }}</h1>
    <p>{{ t('core.home.intro') }}</p>
    <a v-if="!page.props.user" href="/login" class="button">{{ t('core.login') }}</a>
  </DefaultLayout>
</template>
```

Styles dans `inertia/css/app.css` : thème sombre sobre (fond `#111418`, texte `#e6e8eb`, accent `#e0a43a`), barre du haut en flex, cartes en grille `repeat(auto-fill, minmax(160px, 1fr))`, `.alert` rouge, mise en page lisible jusqu'à 360 px de large.

- [ ] **Step 5: Lancer les tests**

Run: `node ace test`
Expected: tous réussis.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: layout, accueil, langue en/fr (cookie, navigateur, défaut) et menu des modules filtré par permission"
```

---

### Task 5: Catalogue et armes (logique pure du module loadouts)

**Files:**
- Create: `app/modules/loadouts/catalog.ts`, `app/modules/loadouts/weapons.ts`
- Create: `tests/fixtures/contract/catalog.v1.json`, `tests/fixtures/contract/player_loadout.json` (copies exactes de `CS2-RetakeV4/contract/`)
- Create: `tests/unit/loadouts/catalog.spec.ts`, `tests/unit/loadouts/weapons.spec.ts`

**Interfaces:**
- Produces:
  - `type Team = 'T' | 'CT'`, `type Slot = 'primary' | 'secondary'`, `TEAMS: readonly Team[]`
  - `TEAM_VALUE: Record<Team, number>` (`{ T: 0, CT: 1 }`), `ANY_ROUND_TYPE = '*'`, `CATALOG_FORMAT_VERSION = 1`
  - `interface TeamCatalog { primaries: string[]; secondaries: string[]; defaultPrimary: string | null; defaultSecondary: string | null; awp: boolean }`
  - `interface RoundTypeCatalog { name: string; teams: Record<Team, TeamCatalog> }`, `interface Catalog { roundTypes: RoundTypeCatalog[] }`
  - `type CatalogResult = { kind: 'ok'; catalog: Catalog } | { kind: 'missing' } | { kind: 'unsupported'; formatVersion: number } | { kind: 'invalid' }`
  - `readCatalog(row: { format_version: number; catalog: string } | null): CatalogResult`
  - `options(catalog: Catalog, roundType: string, team: Team, slot: Slot): string[]`
  - `hasChoice(roundType: RoundTypeCatalog, team: Team): boolean`
  - `isAllowed(catalog: Catalog, selection: { roundType: string; team: Team; slot: Slot; weapon: string }): boolean`
  - `offersAwp(catalog: Catalog): boolean`
  - `weaponLabel(id: string): string`, `weaponCategory(id: string): 'rifle' | 'smg' | 'heavy' | 'sniper' | 'pistol' | 'unknown'`

- [ ] **Step 1: Copier les fixtures**

```bash
mkdir -p tests/fixtures/contract
cp ../CS2RetakeV4/contract/catalog.v1.json ../CS2RetakeV4/contract/player_loadout.json tests/fixtures/contract/
```

- [ ] **Step 2: Écrire les tests (RED)**

`tests/unit/loadouts/catalog.spec.ts` :

```ts
import { test } from '@japa/runner'
import { readFile } from 'node:fs/promises'
import {
  ANY_ROUND_TYPE,
  TEAM_VALUE,
  hasChoice,
  isAllowed,
  offersAwp,
  options,
  readCatalog,
  type Catalog,
} from '#modules/loadouts/catalog'

const fixture = async (name: string) => readFile(new URL(`../../fixtures/contract/${name}`, import.meta.url), 'utf8')

async function contractCatalog(): Promise<Catalog> {
  const result = readCatalog({ format_version: 1, catalog: await fixture('catalog.v1.json') })
  if (result.kind !== 'ok') throw new Error(`fixture should parse, got ${result.kind}`)
  return result.catalog
}

test.group('readCatalog', () => {
  test('reads the contract fixture', async ({ assert }) => {
    const catalog = await contractCatalog()
    assert.deepEqual(catalog.roundTypes.map((r) => r.name), ['Pistol', 'FullBuy'])
  })

  test('missing row', ({ assert }) => {
    assert.deepEqual(readCatalog(null), { kind: 'missing' })
  })

  test('unsupported version', ({ assert }) => {
    assert.deepEqual(readCatalog({ format_version: 2, catalog: '{"roundTypes":[]}' }), { kind: 'unsupported', formatVersion: 2 })
  })

  test('invalid JSON', ({ assert }) => {
    assert.deepEqual(readCatalog({ format_version: 1, catalog: '{oops' }), { kind: 'invalid' })
  })

  test('wrong shape', ({ assert }) => {
    assert.deepEqual(readCatalog({ format_version: 1, catalog: '{"roundTypes":[{"name":"X"}]}' }), { kind: 'invalid' })
    assert.deepEqual(readCatalog({ format_version: 1, catalog: '[]' }), { kind: 'invalid' })
  })
})

test.group('catalog rules', () => {
  test('options per round type, team and slot', async ({ assert }) => {
    const catalog = await contractCatalog()
    assert.deepEqual(options(catalog, 'FullBuy', 'CT', 'primary'), ['weapon_m4a1_silencer', 'weapon_aug'])
    assert.deepEqual(options(catalog, 'Pistol', 'T', 'primary'), [])
    assert.deepEqual(options(catalog, 'Unknown', 'T', 'primary'), [])
  })

  test('a round type has a choice when one slot offers more than one weapon', async ({ assert }) => {
    const catalog = await contractCatalog()
    assert.isTrue(hasChoice(catalog.roundTypes[0], 'T'))
  })

  test('only catalog weapons are allowed', async ({ assert }) => {
    const catalog = await contractCatalog()
    assert.isTrue(isAllowed(catalog, { roundType: 'FullBuy', team: 'T', slot: 'primary', weapon: 'weapon_sg556' }))
    assert.isFalse(isAllowed(catalog, { roundType: 'FullBuy', team: 'T', slot: 'primary', weapon: 'weapon_awp' }))
    assert.isFalse(isAllowed(catalog, { roundType: 'FullBuy', team: 'T', slot: 'primary', weapon: 'weapon_m4a1_silencer' }))
    assert.isFalse(isAllowed(catalog, { roundType: 'Nope', team: 'T', slot: 'primary', weapon: 'weapon_ak47' }))
  })

  test('the AWP toggle shows when a round type hands it out', async ({ assert }) => {
    assert.isTrue(offersAwp(await contractCatalog()))
    assert.isFalse(offersAwp({ roundTypes: [] }))
  })
})

test.group('player_loadout contract', () => {
  test('team values and AWP key match the plugin', async ({ assert }) => {
    const contract = JSON.parse(await fixture('player_loadout.json'))
    assert.deepEqual(contract.teams, TEAM_VALUE)
    assert.equal(contract.anyRoundType, ANY_ROUND_TYPE)
  })
})
```

`tests/unit/loadouts/weapons.spec.ts` :

```ts
import { test } from '@japa/runner'
import { weaponCategory, weaponLabel } from '#modules/loadouts/weapons'

test.group('weapons', () => {
  test('known weapons have a label and a category', ({ assert }) => {
    assert.equal(weaponLabel('weapon_m4a1_silencer'), 'M4A1-S')
    assert.equal(weaponCategory('weapon_m4a1_silencer'), 'rifle')
    assert.equal(weaponCategory('weapon_deagle'), 'pistol')
  })

  test('unknown weapons show their raw id', ({ assert }) => {
    assert.equal(weaponLabel('weapon_future_gun'), 'weapon_future_gun')
    assert.equal(weaponCategory('weapon_future_gun'), 'unknown')
  })
})
```

Run: `node ace test --files tests/unit/loadouts`
Expected: FAIL.

- [ ] **Step 3: Implémenter**

`app/modules/loadouts/catalog.ts` :

```ts
export type Team = 'T' | 'CT'
export type Slot = 'primary' | 'secondary'

export const TEAMS: readonly Team[] = ['T', 'CT']
export const TEAM_VALUE: Record<Team, number> = { T: 0, CT: 1 }
export const ANY_ROUND_TYPE = '*'
export const CATALOG_FORMAT_VERSION = 1

export interface TeamCatalog {
  primaries: string[]
  secondaries: string[]
  defaultPrimary: string | null
  defaultSecondary: string | null
  awp: boolean
}

export interface RoundTypeCatalog {
  name: string
  teams: Record<Team, TeamCatalog>
}

export interface Catalog {
  roundTypes: RoundTypeCatalog[]
}

export type CatalogResult =
  | { kind: 'ok'; catalog: Catalog }
  | { kind: 'missing' }
  | { kind: 'unsupported'; formatVersion: number }
  | { kind: 'invalid' }

const isStringList = (value: unknown): value is string[] =>
  Array.isArray(value) && value.every((item) => typeof item === 'string')

const isNullableString = (value: unknown): value is string | null => value === null || typeof value === 'string'

function isTeamCatalog(value: unknown): value is TeamCatalog {
  const team = value as Partial<TeamCatalog> | null
  return (
    typeof team === 'object' &&
    team !== null &&
    isStringList(team.primaries) &&
    isStringList(team.secondaries) &&
    isNullableString(team.defaultPrimary) &&
    isNullableString(team.defaultSecondary) &&
    typeof team.awp === 'boolean'
  )
}

function isRoundType(value: unknown): value is RoundTypeCatalog {
  const roundType = value as Partial<RoundTypeCatalog> | null
  return (
    typeof roundType === 'object' &&
    roundType !== null &&
    typeof roundType.name === 'string' &&
    typeof roundType.teams === 'object' &&
    roundType.teams !== null &&
    TEAMS.every((team) => isTeamCatalog(roundType.teams?.[team]))
  )
}

export function readCatalog(row: { format_version: number; catalog: string } | null): CatalogResult {
  if (row === null) return { kind: 'missing' }
  if (Number(row.format_version) !== CATALOG_FORMAT_VERSION) {
    return { kind: 'unsupported', formatVersion: Number(row.format_version) }
  }
  let parsed: unknown
  try {
    parsed = JSON.parse(row.catalog)
  } catch {
    return { kind: 'invalid' }
  }
  const roundTypes = (parsed as { roundTypes?: unknown } | null)?.roundTypes
  if (!Array.isArray(roundTypes) || !roundTypes.every(isRoundType)) return { kind: 'invalid' }
  return { kind: 'ok', catalog: { roundTypes } }
}

const findRoundType = (catalog: Catalog, name: string) => catalog.roundTypes.find((roundType) => roundType.name === name)

export function options(catalog: Catalog, roundType: string, team: Team, slot: Slot): string[] {
  const teamCatalog = findRoundType(catalog, roundType)?.teams[team]
  if (!teamCatalog) return []
  return slot === 'primary' ? teamCatalog.primaries : teamCatalog.secondaries
}

export function hasChoice(roundType: RoundTypeCatalog, team: Team): boolean {
  const teamCatalog = roundType.teams[team]
  return teamCatalog.primaries.length > 1 || teamCatalog.secondaries.length > 1
}

export function isAllowed(catalog: Catalog, selection: { roundType: string; team: Team; slot: Slot; weapon: string }): boolean {
  return options(catalog, selection.roundType, selection.team, selection.slot).includes(selection.weapon)
}

export function offersAwp(catalog: Catalog): boolean {
  return catalog.roundTypes.some((roundType) => TEAMS.some((team) => roundType.teams[team].awp))
}
```

`app/modules/loadouts/weapons.ts` :

```ts
type Category = 'rifle' | 'smg' | 'heavy' | 'sniper' | 'pistol'

const WEAPONS: Record<string, { label: string; category: Category }> = {
  weapon_ak47: { label: 'AK-47', category: 'rifle' },
  weapon_m4a1: { label: 'M4A4', category: 'rifle' },
  weapon_m4a1_silencer: { label: 'M4A1-S', category: 'rifle' },
  weapon_famas: { label: 'FAMAS', category: 'rifle' },
  weapon_galilar: { label: 'Galil AR', category: 'rifle' },
  weapon_aug: { label: 'AUG', category: 'rifle' },
  weapon_sg556: { label: 'SG 553', category: 'rifle' },
  weapon_ssg08: { label: 'SSG 08', category: 'sniper' },
  weapon_awp: { label: 'AWP', category: 'sniper' },
  weapon_scar20: { label: 'SCAR-20', category: 'sniper' },
  weapon_g3sg1: { label: 'G3SG1', category: 'sniper' },
  weapon_mac10: { label: 'MAC-10', category: 'smg' },
  weapon_mp9: { label: 'MP9', category: 'smg' },
  weapon_mp7: { label: 'MP7', category: 'smg' },
  weapon_mp5sd: { label: 'MP5-SD', category: 'smg' },
  weapon_ump45: { label: 'UMP-45', category: 'smg' },
  weapon_p90: { label: 'P90', category: 'smg' },
  weapon_bizon: { label: 'PP-Bizon', category: 'smg' },
  weapon_nova: { label: 'Nova', category: 'heavy' },
  weapon_xm1014: { label: 'XM1014', category: 'heavy' },
  weapon_mag7: { label: 'MAG-7', category: 'heavy' },
  weapon_sawedoff: { label: 'Sawed-Off', category: 'heavy' },
  weapon_m249: { label: 'M249', category: 'heavy' },
  weapon_negev: { label: 'Negev', category: 'heavy' },
  weapon_glock: { label: 'Glock-18', category: 'pistol' },
  weapon_hkp2000: { label: 'P2000', category: 'pistol' },
  weapon_usp_silencer: { label: 'USP-S', category: 'pistol' },
  weapon_p250: { label: 'P250', category: 'pistol' },
  weapon_fiveseven: { label: 'Five-SeveN', category: 'pistol' },
  weapon_tec9: { label: 'Tec-9', category: 'pistol' },
  weapon_cz75a: { label: 'CZ75-Auto', category: 'pistol' },
  weapon_deagle: { label: 'Desert Eagle', category: 'pistol' },
  weapon_revolver: { label: 'R8 Revolver', category: 'pistol' },
  weapon_elite: { label: 'Dual Berettas', category: 'pistol' },
}

export function weaponLabel(id: string): string {
  return WEAPONS[id]?.label ?? id
}

export function weaponCategory(id: string): Category | 'unknown' {
  return WEAPONS[id]?.category ?? 'unknown'
}
```

Ruling prévu par ce plan : les « images SVG locales » de la spec sont une icône SVG par catégorie (`public/weapons/<categorie>.svg`, 6 fichiers dont `unknown.svg`), dessinée en silhouette simple ; aucun visuel d'arme sous licence n'est embarqué.

- [ ] **Step 4: Lancer les tests**

Run: `node ace test --files tests/unit/loadouts`
Expected: tous réussis.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: lecture et règles du catalogue d'armes publié par le plugin, noms des armes, fixtures de contrat"
```

---

### Task 6: Dépôt des préférences (connexion `retake`)

**Files:**
- Create: `app/modules/loadouts/loadout_repository.ts`
- Create: `tests/fixtures/plugin_schema.sql`
- Create: `tests/functional/loadouts/loadout_repository.spec.ts`
- Modify: `tests/bootstrap.ts`

**Interfaces:**
- Consumes: `Team`, `Slot`, `TEAM_VALUE`, `ANY_ROUND_TYPE`, `TEAMS` (Task 5).
- Produces:
  - `interface PreferenceRow { team: Team; roundType: string; primary: string | null; secondary: string | null; awpOptIn: boolean }`
  - classe `LoadoutRepository` (injectable, connexion `retake`) :
    - `catalogRow(serverKey: string): Promise<{ format_version: number; catalog: string } | null>`
    - `preferences(steamId: string): Promise<PreferenceRow[]>`
    - `setWeapon(steamId: string, team: Team, roundType: string, slot: Slot, weapon: string): Promise<void>`
    - `reset(steamId: string, team: Team, roundType: string): Promise<void>`
    - `setAwp(steamId: string, optIn: boolean): Promise<void>`
  - helper de test `resetPluginTables(): Promise<void>` exporté par `tests/bootstrap.ts`

- [ ] **Step 1: Schéma du plugin pour les tests**

`tests/fixtures/plugin_schema.sql` (copie du DDL MySQL de `MySqlPreferenceRepository` du plugin) :

```sql
CREATE TABLE IF NOT EXISTS player_loadout (
    steam_id BIGINT UNSIGNED NOT NULL,
    team TINYINT NOT NULL,
    round_type VARCHAR(64) NOT NULL,
    primary_weapon VARCHAR(64) NULL,
    secondary_weapon VARCHAR(64) NULL,
    awp_opt_in TINYINT(1) NOT NULL DEFAULT 0,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (steam_id, team, round_type));
CREATE TABLE IF NOT EXISTS retake_catalog (
    server_key VARCHAR(64) NOT NULL PRIMARY KEY,
    format_version INT NOT NULL,
    catalog MEDIUMTEXT NOT NULL,
    updated_at DATETIME(6) NOT NULL);
```

Dans `tests/bootstrap.ts` :

```ts
import { readFile } from 'node:fs/promises'
import db from '@adonisjs/lucid/services/db'

async function createPluginTables() {
  const sql = await readFile(new URL('./fixtures/plugin_schema.sql', import.meta.url), 'utf8')
  for (const statement of sql.split(';').map((s) => s.trim()).filter(Boolean)) {
    await db.connection('retake').rawQuery(statement)
  }
}

export async function resetPluginTables() {
  await db.connection('retake').rawQuery('DELETE FROM player_loadout')
  await db.connection('retake').rawQuery('DELETE FROM retake_catalog')
}
```

et ajouter `createPluginTables` aux `runnerHooks.setup` après la migration du panel.

- [ ] **Step 2: Écrire les tests (RED)**

`tests/functional/loadouts/loadout_repository.spec.ts` :

```ts
import { test } from '@japa/runner'
import db from '@adonisjs/lucid/services/db'
import { LoadoutRepository } from '#modules/loadouts/loadout_repository'
import { resetPluginTables } from '#tests/bootstrap'

const ALICE = '76561198000000001'
const HUGE = '18446744073709551614'

async function rows(steamId: string) {
  const [result] = await db
    .connection('retake')
    .rawQuery(
      'SELECT team, round_type, primary_weapon, secondary_weapon, awp_opt_in, updated_at FROM player_loadout WHERE steam_id = ? ORDER BY team, round_type',
      [steamId]
    )
  return result as { team: number; round_type: string; primary_weapon: string | null; secondary_weapon: string | null; awp_opt_in: number; updated_at: Date }[]
}

test.group('LoadoutRepository', (group) => {
  group.each.setup(() => resetPluginTables())
  const repository = new LoadoutRepository()

  test('setWeapon creates then updates only its slot', async ({ assert }) => {
    await repository.setWeapon(ALICE, 'CT', 'FullBuy', 'primary', 'weapon_aug')
    await repository.setWeapon(ALICE, 'CT', 'FullBuy', 'secondary', 'weapon_deagle')
    const [row] = await rows(ALICE)
    assert.equal(row.team, 1)
    assert.equal(row.primary_weapon, 'weapon_aug')
    assert.equal(row.secondary_weapon, 'weapon_deagle')
    assert.equal(row.awp_opt_in, 0)
  })

  test('every write moves updated_at forward', async ({ assert }) => {
    await repository.setWeapon(ALICE, 'T', 'Mid', 'primary', 'weapon_mac10')
    const [before] = await rows(ALICE)
    await new Promise((resolve) => setTimeout(resolve, 5))
    await repository.setWeapon(ALICE, 'T', 'Mid', 'primary', 'weapon_mp9')
    const [after] = await rows(ALICE)
    assert.isAbove(after.updated_at.getTime(), before.updated_at.getTime())
  })

  test('reset clears both weapons', async ({ assert }) => {
    await repository.setWeapon(ALICE, 'T', 'Mid', 'primary', 'weapon_mac10')
    await repository.reset(ALICE, 'T', 'Mid')
    const [row] = await rows(ALICE)
    assert.isNull(row.primary_weapon)
    assert.isNull(row.secondary_weapon)
  })

  test('setAwp writes the * row of both teams and keeps weapons', async ({ assert }) => {
    await repository.setAwp(ALICE, true)
    const all = await rows(ALICE)
    assert.deepEqual(all.map((r) => [r.team, r.round_type, r.awp_opt_in]), [[0, '*', 1], [1, '*', 1]])
    await repository.setAwp(ALICE, false)
    assert.deepEqual((await rows(ALICE)).map((r) => r.awp_opt_in), [0, 0])
  })

  test('preferences reads the rows of the player only', async ({ assert }) => {
    await repository.setWeapon(ALICE, 'CT', 'FullBuy', 'primary', 'weapon_aug')
    await repository.setWeapon('76561198000000002', 'CT', 'FullBuy', 'primary', 'weapon_m4a1')
    assert.deepEqual(await repository.preferences(ALICE), [
      { team: 'CT', roundType: 'FullBuy', primary: 'weapon_aug', secondary: null, awpOptIn: false },
    ])
  })

  test('steam ids above 2^63 round trip', async ({ assert }) => {
    await repository.setWeapon(HUGE, 'T', 'Mid', 'primary', 'weapon_mac10')
    assert.lengthOf(await repository.preferences(HUGE), 1)
  })

  test('catalogRow reads the configured server', async ({ assert }) => {
    await db
      .connection('retake')
      .rawQuery("INSERT INTO retake_catalog VALUES ('default', 1, '{\"roundTypes\":[]}', UTC_TIMESTAMP(6))")
    assert.deepEqual(await repository.catalogRow('default'), { format_version: 1, catalog: '{"roundTypes":[]}' })
    assert.isNull(await repository.catalogRow('other'))
  })
})
```

Ajouter `"#tests/*": "./tests/*.js"` aux `imports` de `package.json`.

Run: `node ace test --files tests/functional/loadouts/loadout_repository.spec.ts`
Expected: FAIL, `LoadoutRepository` introuvable.

- [ ] **Step 3: Implémenter**

`app/modules/loadouts/loadout_repository.ts` :

```ts
import db from '@adonisjs/lucid/services/db'
import { ANY_ROUND_TYPE, TEAMS, TEAM_VALUE, type Slot, type Team } from '#modules/loadouts/catalog'

export interface PreferenceRow {
  team: Team
  roundType: string
  primary: string | null
  secondary: string | null
  awpOptIn: boolean
}

// The RetakeV4 plugin owns these tables: the panel only reads and writes rows, with the plugin's exact encoding.
export class LoadoutRepository {
  private get connection() {
    return db.connection('retake')
  }

  async catalogRow(serverKey: string): Promise<{ format_version: number; catalog: string } | null> {
    const [rows] = await this.connection.rawQuery(
      'SELECT format_version, catalog FROM retake_catalog WHERE server_key = ?',
      [serverKey]
    )
    const row = (rows as { format_version: number; catalog: string }[])[0]
    return row ? { format_version: Number(row.format_version), catalog: row.catalog } : null
  }

  async preferences(steamId: string): Promise<PreferenceRow[]> {
    const [rows] = await this.connection.rawQuery(
      'SELECT team, round_type, primary_weapon, secondary_weapon, awp_opt_in FROM player_loadout WHERE steam_id = ? ORDER BY team, round_type',
      [steamId]
    )
    return (rows as { team: number; round_type: string; primary_weapon: string | null; secondary_weapon: string | null; awp_opt_in: number }[]).map(
      (row) => ({
        team: row.team === TEAM_VALUE.T ? 'T' : 'CT',
        roundType: row.round_type,
        primary: row.primary_weapon,
        secondary: row.secondary_weapon,
        awpOptIn: Number(row.awp_opt_in) === 1,
      })
    )
  }

  async setWeapon(steamId: string, team: Team, roundType: string, slot: Slot, weapon: string): Promise<void> {
    const column = slot === 'primary' ? 'primary_weapon' : 'secondary_weapon'
    await this.connection.rawQuery(
      `INSERT INTO player_loadout (steam_id, team, round_type, ${column}, awp_opt_in, updated_at)
       VALUES (?, ?, ?, ?, 0, UTC_TIMESTAMP(6))
       ON DUPLICATE KEY UPDATE ${column} = VALUES(${column}), updated_at = VALUES(updated_at)`,
      [steamId, TEAM_VALUE[team], roundType, weapon]
    )
  }

  async reset(steamId: string, team: Team, roundType: string): Promise<void> {
    await this.connection.rawQuery(
      `UPDATE player_loadout SET primary_weapon = NULL, secondary_weapon = NULL, updated_at = UTC_TIMESTAMP(6)
       WHERE steam_id = ? AND team = ? AND round_type = ?`,
      [steamId, TEAM_VALUE[team], roundType]
    )
  }

  async setAwp(steamId: string, optIn: boolean): Promise<void> {
    await this.connection.transaction(async (trx) => {
      for (const team of TEAMS) {
        await trx.rawQuery(
          `INSERT INTO player_loadout (steam_id, team, round_type, awp_opt_in, updated_at)
           VALUES (?, ?, ?, ?, UTC_TIMESTAMP(6))
           ON DUPLICATE KEY UPDATE awp_opt_in = VALUES(awp_opt_in), updated_at = VALUES(updated_at)`,
          [steamId, TEAM_VALUE[team], ANY_ROUND_TYPE, optIn ? 1 : 0]
        )
      }
    })
  }
}
```

`column` ne vient jamais de l'utilisateur (seulement de `slot`, déjà typé et validé) : c'est la seule interpolation de la requête.

- [ ] **Step 4: Lancer les tests**

Run: `node ace test --files tests/functional/loadouts/loadout_repository.spec.ts`
Expected: 7 tests réussis.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: lecture et écriture de player_loadout et retake_catalog avec l'encodage du plugin"
```

---

### Task 7: Module loadouts (vue, contrôleur, page)

**Files:**
- Create: `app/modules/loadouts/loadouts_view.ts`, `app/modules/loadouts/validators.ts`, `app/modules/loadouts/loadouts_controller.ts`, `app/modules/loadouts/module.ts`
- Modify: `app/core/modules/catalog.ts`
- Create: `inertia/pages/loadouts/index.vue`, `inertia/components/weapon_card.vue`, `public/weapons/{rifle,smg,heavy,sniper,pistol,unknown}.svg`
- Create: `tests/unit/loadouts/loadouts_view.spec.ts`, `tests/functional/loadouts/loadouts.spec.ts`

**Interfaces:**
- Consumes: `CatalogResult`, `readCatalog`, `isAllowed`, `hasChoice`, `offersAwp`, `weaponLabel`, `weaponCategory` (Task 5) ; `LoadoutRepository`, `PreferenceRow` (Task 6) ; `PanelModule` (Task 2) ; `writeThrottle` (Task 3).
- Produces:
  - `buildLoadoutsView(result: CatalogResult, preferences: PreferenceRow[]): LoadoutsView` où
    `LoadoutsView = { status: 'ok' | 'missing' | 'unsupported' | 'invalid'; formatVersion: number | null; awp: { offered: boolean; optIn: boolean }; teams: Record<Team, RoundTypeView[]> }`,
    `RoundTypeView = { name: string; primary: SlotView; secondary: SlotView }`,
    `SlotView = { choices: { id: string; label: string; category: string }[]; selected: string | null; defaultId: string | null }`
  - `loadoutsModule: PanelModule` (`name: 'loadouts'`, `connections: ['retake']`, menu `{ labelKey: 'loadouts.menu', href: '/loadouts', permission: 'player' }`)
  - routes : `GET /loadouts` (`loadouts.show`), `POST /loadouts/weapon`, `POST /loadouts/reset`, `POST /loadouts/awp`

- [ ] **Step 1: Écrire les tests (RED)**

`tests/unit/loadouts/loadouts_view.spec.ts` :

```ts
import { test } from '@japa/runner'
import { readFile } from 'node:fs/promises'
import { readCatalog } from '#modules/loadouts/catalog'
import { buildLoadoutsView } from '#modules/loadouts/loadouts_view'

const catalog = async () =>
  readCatalog({
    format_version: 1,
    catalog: await readFile(new URL('../../fixtures/contract/catalog.v1.json', import.meta.url), 'utf8'),
  })

test.group('buildLoadoutsView', () => {
  test('lists round types with a choice, per team', async ({ assert }) => {
    const view = buildLoadoutsView(await catalog(), [])
    assert.equal(view.status, 'ok')
    assert.deepEqual(view.teams.CT.map((r) => r.name), ['Pistol', 'FullBuy'])
    assert.deepEqual(view.teams.CT[1].primary.choices.map((c) => c.id), ['weapon_m4a1_silencer', 'weapon_aug'])
    assert.equal(view.teams.CT[1].primary.defaultId, 'weapon_m4a1_silencer')
    assert.equal(view.teams.CT[1].primary.choices[0].label, 'M4A1-S')
  })

  test('marks the saved choices and the AWP opt-in', async ({ assert }) => {
    const view = buildLoadoutsView(await catalog(), [
      { team: 'CT', roundType: 'FullBuy', primary: 'weapon_aug', secondary: null, awpOptIn: false },
      { team: 'T', roundType: '*', primary: null, secondary: null, awpOptIn: true },
    ])
    assert.equal(view.teams.CT[1].primary.selected, 'weapon_aug')
    assert.isNull(view.teams.CT[1].secondary.selected)
    assert.deepEqual(view.awp, { offered: true, optIn: true })
  })

  test('a saved weapon no longer offered is not shown as selected', async ({ assert }) => {
    const view = buildLoadoutsView(await catalog(), [
      { team: 'CT', roundType: 'FullBuy', primary: 'weapon_famas', secondary: null, awpOptIn: false },
    ])
    assert.isNull(view.teams.CT[1].primary.selected)
  })

  test('reports catalog problems', ({ assert }) => {
    assert.equal(buildLoadoutsView({ kind: 'missing' }, []).status, 'missing')
    const unsupported = buildLoadoutsView({ kind: 'unsupported', formatVersion: 3 }, [])
    assert.deepEqual([unsupported.status, unsupported.formatVersion], ['unsupported', 3])
    assert.deepEqual(buildLoadoutsView({ kind: 'invalid' }, []).teams, { T: [], CT: [] })
  })
})
```

`tests/functional/loadouts/loadouts.spec.ts` :

```ts
import { test } from '@japa/runner'
import { readFile } from 'node:fs/promises'
import app from '@adonisjs/core/services/app'
import db from '@adonisjs/lucid/services/db'
import testUtils from '@adonisjs/core/services/test_utils'
import Player from '#models/player'
import { LoadoutRepository } from '#modules/loadouts/loadout_repository'
import { resetPluginTables } from '#tests/bootstrap'

const ALICE = '76561198000000001'

async function publishCatalog() {
  const catalog = await readFile(new URL('../../fixtures/contract/catalog.v1.json', import.meta.url), 'utf8')
  await db.connection('retake').rawQuery('INSERT INTO retake_catalog VALUES (?, 1, ?, UTC_TIMESTAMP(6))', ['default', catalog])
}

async function savedPrimary(team: number, roundType: string) {
  const [rows] = await db
    .connection('retake')
    .rawQuery('SELECT primary_weapon FROM player_loadout WHERE steam_id = ? AND team = ? AND round_type = ?', [ALICE, team, roundType])
  return (rows as { primary_weapon: string | null }[])[0]?.primary_weapon ?? null
}

test.group('Loadouts', (group) => {
  group.each.setup(() => testUtils.db('panel').withGlobalTransaction())
  group.each.setup(() => resetPluginTables())
  group.each.teardown(() => app.container.restore(LoadoutRepository))

  test('redirects guests to login', async ({ client }) => {
    const response = await client.get('/loadouts').redirects(0)
    response.assertStatus(302)
    response.assertHeader('location', '/login')
  })

  test('shows the catalog and the saved choices', async ({ client }) => {
    await publishCatalog()
    const player = await Player.create({ steamId: ALICE })
    const response = await client.get('/loadouts').loginAs(player).withInertia()
    response.assertInertiaComponent('loadouts/index')
    response.assertInertiaPropsContains({ view: { status: 'ok', awp: { offered: true, optIn: false } } })
  })

  test('says when no catalog was published', async ({ client }) => {
    const player = await Player.create({ steamId: ALICE })
    const response = await client.get('/loadouts').loginAs(player).withInertia()
    response.assertInertiaPropsContains({ view: { status: 'missing' } })
  })

  test('saves a weapon from the catalog', async ({ client, assert }) => {
    await publishCatalog()
    const player = await Player.create({ steamId: ALICE })
    const response = await client
      .post('/loadouts/weapon')
      .loginAs(player)
      .withCsrfToken()
      .json({ team: 'CT', roundType: 'FullBuy', slot: 'primary', weapon: 'weapon_aug' })
      .redirects(0)
    response.assertStatus(302)
    assert.equal(await savedPrimary(1, 'FullBuy'), 'weapon_aug')
  })

  test('rejects a weapon outside the catalog', async ({ client, assert }) => {
    await publishCatalog()
    const player = await Player.create({ steamId: ALICE })
    const response = await client
      .post('/loadouts/weapon')
      .loginAs(player)
      .withCsrfToken()
      .json({ team: 'CT', roundType: 'FullBuy', slot: 'primary', weapon: 'weapon_awp' })
    response.assertStatus(422)
    assert.isNull(await savedPrimary(1, 'FullBuy'))
  })

  test('rejects an unknown round type', async ({ client }) => {
    await publishCatalog()
    const player = await Player.create({ steamId: ALICE })
    const response = await client
      .post('/loadouts/weapon')
      .loginAs(player)
      .withCsrfToken()
      .json({ team: 'CT', roundType: 'Nope', slot: 'primary', weapon: 'weapon_aug' })
    response.assertStatus(422)
  })

  test('rejects malformed input', async ({ client }) => {
    await publishCatalog()
    const player = await Player.create({ steamId: ALICE })
    const response = await client
      .post('/loadouts/weapon')
      .loginAs(player)
      .withCsrfToken()
      .json({ team: 'X', roundType: 'FullBuy', slot: 'primary', weapon: 'weapon_aug' })
      .accept('json')
    response.assertStatus(422)
  })

  test('requires a csrf token', async ({ client, assert }) => {
    await publishCatalog()
    const player = await Player.create({ steamId: ALICE })
    const response = await client
      .post('/loadouts/weapon')
      .loginAs(player)
      .json({ team: 'CT', roundType: 'FullBuy', slot: 'primary', weapon: 'weapon_aug' })
      .redirects(0)
    // Shield answers a bad token by redirecting back with an error: what matters is that nothing was written.
    assert.notEqual(response.status(), 200)
    assert.isNull(await savedPrimary(1, 'FullBuy'))
  })

  test('only writes for the logged in player', async ({ client, assert }) => {
    await publishCatalog()
    const player = await Player.create({ steamId: ALICE })
    await client
      .post('/loadouts/weapon')
      .loginAs(player)
      .withCsrfToken()
      .json({ team: 'CT', roundType: 'FullBuy', slot: 'primary', weapon: 'weapon_aug', steamId: '76561198000000002' })
    const [rows] = await db.connection('retake').rawQuery('SELECT CAST(steam_id AS CHAR) AS steam_id FROM player_loadout')
    assert.deepEqual((rows as { steam_id: string }[]).map((r) => r.steam_id), [ALICE])
  })

  test('resets a round type and toggles the AWP', async ({ client, assert }) => {
    await publishCatalog()
    const player = await Player.create({ steamId: ALICE })
    await client.post('/loadouts/weapon').loginAs(player).withCsrfToken().json({ team: 'T', roundType: 'FullBuy', slot: 'primary', weapon: 'weapon_sg556' })
    await client.post('/loadouts/reset').loginAs(player).withCsrfToken().json({ team: 'T', roundType: 'FullBuy' })
    assert.isNull(await savedPrimary(0, 'FullBuy'))
    const awp = await client.post('/loadouts/awp').loginAs(player).withCsrfToken().json({ optIn: true }).redirects(0)
    awp.assertStatus(302)
  })

  test('shows the module error when the retake database fails', async ({ client }) => {
    class FailingRepository extends LoadoutRepository {
      async catalogRow(): Promise<never> {
        throw new Error('connection refused')
      }
    }
    app.container.swap(LoadoutRepository, () => new FailingRepository())
    const player = await Player.create({ steamId: ALICE })
    const response = await client.get('/loadouts').loginAs(player).withInertia()
    response.assertStatus(503)
    response.assertInertiaComponent('loadouts/unavailable')
    const home = await client.get('/').loginAs(player).withInertia()
    home.assertStatus(200)
  })
})
```

Run: `node ace test --files tests/unit/loadouts/loadouts_view.spec.ts,tests/functional/loadouts/loadouts.spec.ts`
Expected: FAIL.

- [ ] **Step 2: Vue (logique pure) et validateurs**

`app/modules/loadouts/loadouts_view.ts` :

```ts
import { ANY_ROUND_TYPE, TEAMS, hasChoice, offersAwp, type CatalogResult, type Slot, type Team } from '#modules/loadouts/catalog'
import type { PreferenceRow } from '#modules/loadouts/loadout_repository'
import { weaponCategory, weaponLabel } from '#modules/loadouts/weapons'

export interface SlotView {
  choices: { id: string; label: string; category: string }[]
  selected: string | null
  defaultId: string | null
}

export interface RoundTypeView {
  name: string
  primary: SlotView
  secondary: SlotView
}

export interface LoadoutsView {
  status: 'ok' | 'missing' | 'unsupported' | 'invalid'
  formatVersion: number | null
  awp: { offered: boolean; optIn: boolean }
  teams: Record<Team, RoundTypeView[]>
}

function slotView(choices: string[], defaultId: string | null, saved: string | null): SlotView {
  return {
    choices: choices.map((id) => ({ id, label: weaponLabel(id), category: weaponCategory(id) })),
    selected: saved !== null && choices.includes(saved) ? saved : null,
    defaultId,
  }
}

export function buildLoadoutsView(result: CatalogResult, preferences: PreferenceRow[]): LoadoutsView {
  const optIn = preferences.some((row) => row.roundType === ANY_ROUND_TYPE && row.awpOptIn)
  if (result.kind !== 'ok') {
    return {
      status: result.kind,
      formatVersion: result.kind === 'unsupported' ? result.formatVersion : null,
      awp: { offered: false, optIn },
      teams: { T: [], CT: [] },
    }
  }
  const saved = (team: Team, roundType: string, slot: Slot) => {
    const row = preferences.find((p) => p.team === team && p.roundType === roundType)
    return (slot === 'primary' ? row?.primary : row?.secondary) ?? null
  }
  const teamViews = (team: Team): RoundTypeView[] =>
    result.catalog.roundTypes
      .filter((roundType) => hasChoice(roundType, team))
      .map((roundType) => {
        const catalog = roundType.teams[team]
        return {
          name: roundType.name,
          primary: slotView(catalog.primaries, catalog.defaultPrimary, saved(team, roundType.name, 'primary')),
          secondary: slotView(catalog.secondaries, catalog.defaultSecondary, saved(team, roundType.name, 'secondary')),
        }
      })
  return {
    status: 'ok',
    formatVersion: null,
    awp: { offered: offersAwp(result.catalog), optIn },
    teams: Object.fromEntries(TEAMS.map((team) => [team, teamViews(team)])) as Record<Team, RoundTypeView[]>,
  }
}
```

`app/modules/loadouts/validators.ts` :

```ts
import vine from '@vinejs/vine'

const team = vine.enum(['T', 'CT'] as const)
const roundType = vine.string().trim().minLength(1).maxLength(64)

export const weaponValidator = vine.create({
  team,
  roundType,
  slot: vine.enum(['primary', 'secondary'] as const),
  weapon: vine.string().trim().minLength(1).maxLength(64),
})

export const resetValidator = vine.create({ team, roundType })

export const awpValidator = vine.create({ optIn: vine.boolean() })
```

- [ ] **Step 3: Contrôleur, module, routes**

`app/modules/loadouts/loadouts_controller.ts` :

```ts
import { inject } from '@adonisjs/core'
import type { HttpContext } from '@adonisjs/core/http'
import env from '#start/env'
import { LoadoutRepository } from '#modules/loadouts/loadout_repository'
import { isAllowed, readCatalog } from '#modules/loadouts/catalog'
import { buildLoadoutsView } from '#modules/loadouts/loadouts_view'
import { awpValidator, resetValidator, weaponValidator } from '#modules/loadouts/validators'

const SERVICE_UNAVAILABLE = 503

@inject()
export default class LoadoutsController {
  constructor(private readonly repository: LoadoutRepository) {}

  private serverKey(): string {
    return env.get('PANEL_RETAKE_SERVER', 'default')
  }

  async show({ inertia, auth, response, logger }: HttpContext) {
    try {
      const [row, preferences] = await Promise.all([
        this.repository.catalogRow(this.serverKey()),
        this.repository.preferences(auth.user!.steamId),
      ])
      return inertia.render('loadouts/index', { view: buildLoadoutsView(readCatalog(row), preferences) })
    } catch (error) {
      logger.error({ err: error }, 'loadouts: retake database unavailable')
      response.status(SERVICE_UNAVAILABLE)
      return inertia.render('loadouts/unavailable', {})
    }
  }

  async updateWeapon({ request, response, auth, session }: HttpContext) {
    const selection = await request.validateUsing(weaponValidator)
    const result = readCatalog(await this.repository.catalogRow(this.serverKey()))
    if (result.kind !== 'ok' || !isAllowed(result.catalog, selection)) {
      // The catalog may have been republished since the page was shown: Inertia gets a flash, API clients a 422.
      if (request.header('x-inertia')) {
        session.flash('error', 'loadouts.not_allowed')
        return response.redirect().back()
      }
      return response.unprocessableEntity({ errors: [{ field: 'weapon', message: 'loadouts.not_allowed' }] })
    }
    await this.repository.setWeapon(auth.user!.steamId, selection.team, selection.roundType, selection.slot, selection.weapon)
    session.flash('success', 'loadouts.saved')
    return response.redirect().back()
  }

  async reset({ request, response, auth, session }: HttpContext) {
    const { team, roundType } = await request.validateUsing(resetValidator)
    await this.repository.reset(auth.user!.steamId, team, roundType)
    session.flash('success', 'loadouts.saved')
    return response.redirect().back()
  }

  async updateAwp({ request, response, auth, session }: HttpContext) {
    const { optIn } = await request.validateUsing(awpValidator)
    await this.repository.setAwp(auth.user!.steamId, optIn)
    session.flash('success', 'loadouts.saved')
    return response.redirect().back()
  }
}
```

`app/modules/loadouts/module.ts` :

```ts
import router from '@adonisjs/core/services/router'
import { middleware } from '#start/kernel'
import { writeThrottle } from '#start/limiter'
import type { PanelModule } from '#core/modules/types'

const LoadoutsController = () => import('#modules/loadouts/loadouts_controller')

export const loadoutsModule: PanelModule = {
  name: 'loadouts',
  connections: ['retake'],
  menu: [{ labelKey: 'loadouts.menu', href: '/loadouts', permission: 'player' }],
  registerRoutes() {
    router
      .group(() => {
        router.get('/loadouts', [LoadoutsController, 'show']).as('loadouts.show')
        router.post('/loadouts/weapon', [LoadoutsController, 'updateWeapon']).as('loadouts.weapon').use(writeThrottle)
        router.post('/loadouts/reset', [LoadoutsController, 'reset']).as('loadouts.reset').use(writeThrottle)
        router.post('/loadouts/awp', [LoadoutsController, 'updateAwp']).as('loadouts.awp').use(writeThrottle)
      })
      .use(middleware.auth({ guards: ['web'] }))
  },
}
```

Le middleware `auth` du kit redirige vers `/login` (`redirectTo = '/login'`) ; le régler ainsi s'il pointe ailleurs.

`app/core/modules/catalog.ts` devient :

```ts
import type { PanelModule } from '#core/modules/types'
import { loadoutsModule } from '#modules/loadouts/module'

export const availableModules: PanelModule[] = [loadoutsModule]
```

Ajouter la clé `'loadouts.not_allowed'` (`This weapon is not offered on this round.` / `Cette arme n'est pas proposée sur ce round.`) dans `en.ts` et `fr.ts`.

- [ ] **Step 4: Pages Vue**

`inertia/components/weapon_card.vue` :

```vue
<script setup lang="ts">
defineProps<{ label: string; category: string; selected: boolean; isDefault: boolean; defaultLabel: string }>()
defineEmits<{ choose: [] }>()
</script>

<template>
  <button type="button" class="weapon-card" :class="{ selected }" :aria-pressed="selected" @click="$emit('choose')">
    <img :src="`/weapons/${category}.svg`" alt="" width="64" height="32" />
    <span class="weapon-name">{{ label }}</span>
    <span v-if="isDefault" class="weapon-default">{{ defaultLabel }}</span>
  </button>
</template>
```

`inertia/pages/loadouts/index.vue` :

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { router, usePage } from '@inertiajs/vue3'
import DefaultLayout from '../../layouts/default.vue'
import WeaponCard from '../../components/weapon_card.vue'
import { translate, type Locale } from '../../i18n/index.js'

type Team = 'T' | 'CT'
type Slot = 'primary' | 'secondary'
interface SlotView { choices: { id: string; label: string; category: string }[]; selected: string | null; defaultId: string | null }
interface RoundTypeView { name: string; primary: SlotView; secondary: SlotView }

const props = defineProps<{
  view: {
    status: 'ok' | 'missing' | 'unsupported' | 'invalid'
    formatVersion: number | null
    awp: { offered: boolean; optIn: boolean }
    teams: Record<Team, RoundTypeView[]>
  }
}>()
const page = usePage<{ locale: Locale; flash: { success?: string } }>()
const t = (key: string, params?: Record<string, string | number>) => translate(page.props.locale, key, params)
const team = ref<Team>('CT')
const slots: Slot[] = ['primary', 'secondary']

const post = (url: string, data: Record<string, unknown>) => router.post(url, data, { preserveScroll: true })
const choose = (roundType: string, slot: Slot, weapon: string) => post('/loadouts/weapon', { team: team.value, roundType, slot, weapon })
const reset = (roundType: string) => post('/loadouts/reset', { team: team.value, roundType })
const toggleAwp = () => post('/loadouts/awp', { optIn: !props.view.awp.optIn })
const isShown = (slot: SlotView, id: string) => (slot.selected ?? slot.defaultId) === id
</script>

<template>
  <DefaultLayout>
    <h1>{{ t('loadouts.title') }}</h1>
    <p v-if="page.props.flash.success" class="notice" role="status">{{ t(page.props.flash.success) }}</p>

    <p v-if="props.view.status === 'missing'" class="alert">{{ t('loadouts.catalog.missing') }}</p>
    <p v-else-if="props.view.status === 'unsupported'" class="alert">
      {{ t('loadouts.catalog.unsupported', { version: props.view.formatVersion ?? '?' }) }}
    </p>
    <p v-else-if="props.view.status === 'invalid'" class="alert">{{ t('loadouts.catalog.invalid') }}</p>

    <template v-else>
      <label v-if="props.view.awp.offered" class="awp-toggle">
        <input type="checkbox" :checked="props.view.awp.optIn" @change="toggleAwp" />
        {{ t('loadouts.awp') }}
        <small>{{ t('loadouts.awp.help') }}</small>
      </label>

      <div class="tabs" role="tablist">
        <button v-for="side in (['T', 'CT'] as const)" :key="side" type="button" role="tab" :aria-selected="team === side" @click="team = side">
          {{ t(`loadouts.team.${side}`) }}
        </button>
      </div>

      <p v-if="props.view.teams[team].length === 0">{{ t('loadouts.no_choice') }}</p>
      <section v-for="roundType in props.view.teams[team]" :key="roundType.name" class="round-type">
        <header>
          <h2>{{ roundType.name }}</h2>
          <button type="button" class="link" @click="reset(roundType.name)">{{ t('loadouts.reset') }}</button>
        </header>
        <div v-for="slot in slots" :key="slot">
          <template v-if="roundType[slot].choices.length > 1">
            <h3>{{ t(`loadouts.${slot}`) }}</h3>
            <div class="weapon-grid">
              <WeaponCard
                v-for="choice in roundType[slot].choices"
                :key="choice.id"
                :label="choice.label"
                :category="choice.category"
                :selected="isShown(roundType[slot], choice.id)"
                :is-default="roundType[slot].defaultId === choice.id"
                :default-label="t('loadouts.default')"
                @choose="choose(roundType.name, slot, choice.id)"
              />
            </div>
          </template>
        </div>
      </section>
    </template>
  </DefaultLayout>
</template>
```

`inertia/pages/loadouts/unavailable.vue` :

```vue
<script setup lang="ts">
import { usePage } from '@inertiajs/vue3'
import DefaultLayout from '../../layouts/default.vue'
import { translate, type Locale } from '../../i18n/index.js'

const page = usePage<{ locale: Locale }>()
</script>

<template>
  <DefaultLayout>
    <h1>{{ translate(page.props.locale, 'loadouts.title') }}</h1>
    <p class="alert" role="alert">{{ translate(page.props.locale, 'loadouts.unavailable') }}</p>
  </DefaultLayout>
</template>
```

Icônes `public/weapons/<categorie>.svg` : six silhouettes simples en `viewBox="0 0 64 32"`, `fill="currentColor"`, sans texte (rifle, smg, heavy, sniper, pistol, unknown).

- [ ] **Step 5: Lancer les tests**

Run: `node ace test`
Expected: tous réussis.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: module loadouts (page Mes armes, choix validés contre le catalogue, AWP, réinitialisation, erreur de base isolée)"
```

---

### Task 8: Santé, Docker, CI et documentation

**Files:**
- Create: `app/controllers/health_controller.ts`, `tests/functional/health.spec.ts`
- Create: `Dockerfile`, `.dockerignore`, `docker/docker-compose.yml`, `docker/Caddyfile`, `docker/mysql-user.sql`
- Create: `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- Create: `README.md`, `README.fr.md`, `LICENSE` (MIT)
- Modify: `start/routes.ts`

**Interfaces:**
- Produces: `GET /health` → `200 {"status":"ok"}` ; image `ghcr.io/neutronbzh/cs2-retakev4-panel:<version>` ; release GitHub avec `docker-compose.yml`.

- [ ] **Step 1: Test de santé (RED)**

`tests/functional/health.spec.ts` :

```ts
import { test } from '@japa/runner'

test.group('Health', () => {
  test('answers without a session', async ({ client }) => {
    const response = await client.get('/health')
    response.assertStatus(200)
    response.assertBody({ status: 'ok' })
  })
})
```

Run: `node ace test --files tests/functional/health.spec.ts`
Expected: FAIL (404).

- [ ] **Step 2: Implémenter**

`app/controllers/health_controller.ts` :

```ts
import type { HttpContext } from '@adonisjs/core/http'

export default class HealthController {
  async show({ response }: HttpContext) {
    return response.ok({ status: 'ok' })
  }
}
```

`start/routes.ts` : `router.get('/health', [() => import('#controllers/health_controller'), 'show']).as('health')`

Run: `node ace test`
Expected: tous réussis.

- [ ] **Step 3: Docker**

`Dockerfile` :

```dockerfile
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN node ace build && cd build && npm ci --omit=dev

FROM node:24-alpine
ENV NODE_ENV=production HOST=0.0.0.0 PORT=3333 SESSION_DRIVER=cookie LIMITER_STORE=database
WORKDIR /app
COPY --from=build --chown=node:node /app/build ./
USER node
EXPOSE 3333
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s CMD wget -qO- http://127.0.0.1:3333/health || exit 1
CMD ["sh", "-c", "node ace migration:run --force && node bin/server.js"]
```

`.dockerignore` :

```
node_modules
build
.env
.env.*
!.env.example
coverage
tests
.git
```

`docker/docker-compose.yml` :

```yaml
services:
  panel:
    image: ghcr.io/neutronbzh/cs2-retakev4-panel:latest
    env_file: .env
    restart: unless-stopped
  caddy:
    image: caddy:2
    ports: ['80:80', '443:443']
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
    depends_on: [panel]
    restart: unless-stopped
volumes:
  caddy_data:
```

`docker/Caddyfile` :

```
{$PANEL_DOMAIN} {
  reverse_proxy panel:3333
}
```

`docker/mysql-user.sql` :

```sql
-- Run as a MySQL administrator. Replace the password.
CREATE DATABASE IF NOT EXISTS retake_panel;
CREATE USER IF NOT EXISTS 'retake_panel'@'%' IDENTIFIED BY 'change-me';
GRANT ALL PRIVILEGES ON retake_panel.* TO 'retake_panel'@'%';
-- retakev4 = the database of the plugin (allocation.json, Database.MySqlConnectionString)
GRANT SELECT, INSERT, UPDATE ON retakev4.player_loadout TO 'retake_panel'@'%';
GRANT SELECT ON retakev4.retake_catalog TO 'retake_panel'@'%';
FLUSH PRIVILEGES;
```

Vérifier localement : `docker build -t retakev4-panel .` puis `docker run --rm --env-file .env.test -e NODE_ENV=production -e APP_URL=https://localhost retakev4-panel node -e "1"`.
Expected: build réussi.

- [ ] **Step 4: CI**

`.github/workflows/ci.yml` :

```yaml
name: CI
on:
  push:
  pull_request:
jobs:
  test:
    runs-on: ubuntu-latest
    services:
      mysql:
        image: mysql:8.4
        env:
          MYSQL_ROOT_PASSWORD: root
          MYSQL_DATABASE: panel_test
        ports: ['3307:3306']
        options: >-
          --health-cmd="mysqladmin ping -proot" --health-interval=5s --health-timeout=5s --health-retries=20
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: 24
          cache: npm
      - run: npm ci
      - run: npm run lint
      - run: npm run typecheck
      - run: npm run coverage
```

`.github/workflows/release.yml` :

```yaml
name: Release
on:
  push:
    tags: ['v*']
permissions:
  contents: write
  packages: write
jobs:
  image:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: docker/setup-qemu-action@v3
      - uses: docker/setup-buildx-action@v3
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - uses: docker/build-push-action@v6
        with:
          context: .
          platforms: linux/amd64,linux/arm64
          push: true
          tags: |
            ghcr.io/neutronbzh/cs2-retakev4-panel:${{ github.ref_name }}
            ghcr.io/neutronbzh/cs2-retakev4-panel:latest
      - uses: softprops/action-gh-release@v2
        with:
          files: |
            docker/docker-compose.yml
            docker/Caddyfile
            docker/mysql-user.sql
            .env.example
```

- [ ] **Step 5: README**

`README.md` (anglais) et `README.fr.md` (même contenu en français), sections :

1. **What it is** : panel web du plugin RetakeV4, connexion Steam, préférences d'armes appliquées au round suivant.
2. **Requirements** : RetakeV4 ≥ 4.1.0 avec `Database.Type = MySql` ; Docker ; un domaine pointant vers le serveur.
3. **Install in 5 steps** :
   1. exécuter `mysql-user.sql` (remplacer le mot de passe et `retakev4` par la base du plugin) ;
   2. copier `.env.example` en `.env`, renseigner `APP_KEY` (`docker run --rm ghcr.io/neutronbzh/cs2-retakev4-panel node ace generate:key --show`), `APP_URL`, les accès MySQL ;
   3. placer `docker-compose.yml` et `Caddyfile` à côté, définir `PANEL_DOMAIN` ;
   4. `docker compose up -d` ;
   5. dans `allocation.json` du plugin, vérifier `Database.ServerKey` = `PANEL_RETAKE_SERVER` (défaut `default`), redémarrer le serveur CS2, ouvrir `https://<domaine>`.
4. **Configuration** : tableau des variables (section 8 de la spec).
5. **Modules** : `PANEL_MODULES`, comportement quand une connexion manque.
6. **Security** : HTTPS obligatoire, utilisateur MySQL limité, `PANEL_ADMINS`.
7. **Development** : `docker compose -f compose.test.yml up -d --wait`, `npm run dev`, `node ace test`.

- [ ] **Step 6: Vérification complète**

Run: `npm run lint && npm run typecheck && npm run coverage`
Expected: 0 erreur, couverture `app/` ≥ 80 %.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "chore: santé, image Docker non-root, compose avec Caddy, CI (lint, types, tests MySQL, couverture) et release GHCR multi-arch ; README en/fr"
```

- [ ] **Step 8: Publication (sur accord explicite de l'utilisateur)**

```bash
gh repo create NeuTroNBZh/CS2-RetakeV4-Panel --public --source . --push
```
