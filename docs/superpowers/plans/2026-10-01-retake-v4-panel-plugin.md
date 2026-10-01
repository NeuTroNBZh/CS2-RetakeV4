# RetakeV4 — phase panel (côté plugin) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Le plugin publie son catalogue d'armes pour le panel web et recharge, à chaque début de round, les préférences modifiées hors du jeu.

**Architecture:** Le Domain calcule le catalogue (`CatalogExport`, même règle que le menu en jeu) et décide quels joueurs recharger (`PreferenceSync`). La persistance ajoute la table `retake_catalog`, un instantané par joueur (lignes + tampon `MAX(updated_at)`) et une requête de tampons groupée. `PreferenceService` orchestre en arrière-plan et revient sur le thread de jeu ; `AllocationModule` ne fait que brancher les événements. Des fixtures de contrat (`contract/`) figent le format partagé avec le panel.

**Tech Stack:** C# / .NET 10, CounterStrikeSharp 1.0.370, Microsoft.Data.Sqlite, MySqlConnector, System.Text.Json, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-01-retake-v4-panel-design.md` (sections 4.1, 4.3, 5, 6)

## Global Constraints

- Build : `dotnet build RetakeV4.sln -c Release` avec 0 warning (TreatWarningsAsErrors).
- Tests : `dotnet test RetakeV4.sln` ; couverture Domain ≥ 80 % : `dotnet test tests/RetakeV4.Domain.Tests -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`.
- `src/RetakeV4.Domain` ne référence jamais CounterStrikeSharp.
- Jamais d'accès base sur le thread de jeu : `Task.Run` pour lire, `PreferenceWriteQueue` pour écrire, retour via `Server.NextFrame` + `ModuleGuard`.
- Requêtes SQL paramétrées uniquement.
- `team` en base : `0` = T, `1` = CT (valeurs de `TeamSide`). AWP : ligne `round_type = '*'`.
- Format du catalogue : JSON v1 `{"roundTypes":[{"name","teams":{"T":{...},"CT":{...}}}]}`, champs d'équipe `primaries`, `secondaries`, `defaultPrimary`, `defaultSecondary`, `awp` ; `CatalogExport.FormatVersion = 1`.
- `Database.ServerKey` : défaut `"default"`, `^[A-Za-z0-9_\-]{1,64}$`.
- Logs en anglais avec templates constants ; commits conventionnels en français comme l'historique.
- Version plugin : `4.1.0`.

## Review Focus

1. Base de préférences indisponible pendant un contrôle ou un rechargement : les préférences en mémoire ne doivent jamais être effacées (instantané `null` → rien n'est appliqué).
2. Choix fait en jeu pendant qu'un rechargement est en vol, ou écriture en jeu pas encore enregistrée : le choix en jeu ne doit pas être écrasé par l'ancienne valeur de la base (`PreferenceSync.Pending` / `CanApply`).
3. Joueur qui se déconnecte pendant un rechargement : aucune entrée fantôme dans `PreferenceBook` ni dans `PreferenceSync`.
4. SteamID au-delà de `long.MaxValue` (stockage SQLite signé) : tampons et instantanés doivent revenir avec le bon `ulong`.
5. Base existante créée par la 4.0.x (une seule migration appliquée) : la migration `retake_catalog` doit s'appliquer sans toucher `player_loadout`.

Chaque point a son test dans la tâche qui possède le code : 1 → Task 5 (`DatabaseDown_DuringCheck_KeepsPreferences`), 2 → Task 2 (`PendingWrite_IsNotReloaded`, `EditDuringReload_BlocksApply`), 3 → Task 5 (`DisconnectDuringReload_LeavesNoTrace`), 4 → Task 3 (`Stamps_HandleLargeSteamIds`), 5 → Task 3 (`ExistingV1Database_GetsTheCatalogTable`).

---

### Task 1: Catalogue d'armes exporté (Domain + fixture de contrat)

**Files:**
- Create: `src/RetakeV4.Domain/Loadouts/CatalogExport.cs`
- Create: `contract/catalog.v1.json`
- Create: `contract/README.md`
- Create: `tests/RetakeV4.Domain.Tests/Loadouts/CatalogExportTests.cs`
- Modify: `tests/RetakeV4.Domain.Tests/RetakeV4.Domain.Tests.csproj`

**Interfaces:**
- Consumes: `WeaponMenu.Options(RoundTypeDefinition, TeamSide, WeaponSlot) : IReadOnlyList<string>`, `RoundTypeDefinition`.
- Produces: `CatalogExport.FormatVersion : int` (= 1), `CatalogExport.Build(IReadOnlyList<RoundTypeDefinition>) : string` (JSON compact).

- [ ] **Step 1: Copier les fixtures dans la sortie des tests Domain**

Dans `tests/RetakeV4.Domain.Tests/RetakeV4.Domain.Tests.csproj`, ajouter avant `</Project>` :

```xml
  <ItemGroup>
    <None Include="..\..\contract\**\*.json" LinkBase="contract" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Écrire la fixture `contract/catalog.v1.json`**

```json
{
  "roundTypes": [
    {
      "name": "Pistol",
      "teams": {
        "T": { "primaries": [], "secondaries": ["weapon_glock", "weapon_tec9"], "defaultPrimary": null, "defaultSecondary": "weapon_glock", "awp": false },
        "CT": { "primaries": [], "secondaries": ["weapon_usp_silencer", "weapon_p250"], "defaultPrimary": null, "defaultSecondary": "weapon_usp_silencer", "awp": false }
      }
    },
    {
      "name": "FullBuy",
      "teams": {
        "T": { "primaries": ["weapon_ak47", "weapon_sg556"], "secondaries": ["weapon_glock", "weapon_deagle"], "defaultPrimary": "weapon_ak47", "defaultSecondary": "weapon_glock", "awp": true },
        "CT": { "primaries": ["weapon_m4a1_silencer", "weapon_aug"], "secondaries": ["weapon_usp_silencer", "weapon_deagle"], "defaultPrimary": "weapon_m4a1_silencer", "defaultSecondary": "weapon_usp_silencer", "awp": true }
      }
    }
  ]
}
```

Et `contract/README.md` :

```markdown
# Contrat plugin ↔ panel

Fichiers partagés avec le dépôt `CS2-RetakeV4-Panel` (copiés à l'identique dans `tests/fixtures/contract/`).

- `catalog.v1.json` : contenu de `retake_catalog.catalog` (format 1) pour les round types `Pistol` et `FullBuy` définis dans `CatalogExportTests`.
- `player_loadout.json` : valeurs de `team`, clé AWP et lignes de `player_loadout` telles que le panel les écrit.

Toute modification se fait dans les deux dépôts, avec leurs tests de contrat.
```

- [ ] **Step 3: Écrire les tests (RED)**

`tests/RetakeV4.Domain.Tests/Loadouts/CatalogExportTests.cs` :

```csharp
using System.Text.Json.Nodes;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Tests.Loadouts;

public class CatalogExportTests
{
    private static readonly RoundTypeDefinition Pistol = new(
        "Pistol", ArmorKind.Kevlar,
        TeamWeapons.Empty,
        new TeamWeapons(new[] { "weapon_glock", "weapon_tec9" }, new[] { "weapon_usp_silencer", "weapon_p250" }, Array.Empty<string>()),
        new TeamDefault(null, "weapon_glock"), new TeamDefault(null, "weapon_usp_silencer"),
        new AwpSettings(false, 0, 0, 0), new DefuseKitSettings(DefuseKitMode.All, 0, 0, false), new ZeusSettings(false, 0), "Pistol");

    private static readonly RoundTypeDefinition FullBuy = new(
        "FullBuy", ArmorKind.KevlarHelmet,
        new TeamWeapons(new[] { "weapon_ak47", "weapon_sg556" }, new[] { "weapon_m4a1_silencer", "weapon_aug" }, new[] { "weapon_awp" }),
        new TeamWeapons(new[] { "weapon_glock" }, new[] { "weapon_usp_silencer" }, new[] { "weapon_deagle" }),
        new TeamDefault("weapon_ak47", "weapon_glock"), new TeamDefault("weapon_m4a1_silencer", "weapon_usp_silencer"),
        new AwpSettings(true, 1, 0, 100), new DefuseKitSettings(DefuseKitMode.All, 0, 0, false), new ZeusSettings(false, 0), "FullBuy");

    private static JsonNode Fixture() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "catalog.v1.json")))!;

    [Fact]
    public void Build_MatchesTheContractFixture() =>
        Assert.True(JsonNode.DeepEquals(Fixture(), JsonNode.Parse(CatalogExport.Build(new[] { Pistol, FullBuy }))));

    [Fact]
    public void Build_NeverOffersTheAwpAsAPrimary() =>
        Assert.DoesNotContain("weapon_awp\"", CatalogExport.Build(new[] { FullBuy }).Replace("\"awp\"", string.Empty, StringComparison.Ordinal));

    [Fact]
    public void Build_WithoutRoundTypes_GivesAnEmptyList() =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"roundTypes":[]}"""), JsonNode.Parse(CatalogExport.Build(Array.Empty<RoundTypeDefinition>()))));

    [Fact]
    public void FormatVersion_IsOne() => Assert.Equal(1, CatalogExport.FormatVersion);
}
```

- [ ] **Step 4: Lancer les tests et vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --filter "FullyQualifiedName~CatalogExportTests"`
Expected: échec de compilation, `CatalogExport` introuvable.

- [ ] **Step 5: Implémenter `CatalogExport`**

`src/RetakeV4.Domain/Loadouts/CatalogExport.cs` :

```csharp
using System.Text;
using System.Text.Json;
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Loadouts;

// What the web panel may offer: computed with the in-game menu's rule so both always agree.
public static class CatalogExport
{
    public const int FormatVersion = 1;

    private static readonly TeamSide[] Sides = { TeamSide.T, TeamSide.CT };

    public static string Build(IReadOnlyList<RoundTypeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("roundTypes");
            foreach (var definition in definitions)
            {
                WriteRoundType(writer, definition);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteRoundType(Utf8JsonWriter writer, RoundTypeDefinition definition)
    {
        writer.WriteStartObject();
        writer.WriteString("name", definition.Name);
        writer.WriteStartObject("teams");
        foreach (var side in Sides)
        {
            var defaults = definition.DefaultFor(side);
            writer.WriteStartObject(side.ToString());
            WriteList(writer, "primaries", WeaponMenu.Options(definition, side, WeaponSlot.Primary));
            WriteList(writer, "secondaries", WeaponMenu.Options(definition, side, WeaponSlot.Secondary));
            writer.WriteString("defaultPrimary", defaults.Primary);
            writer.WriteString("defaultSecondary", defaults.Secondary);
            writer.WriteBoolean("awp", definition.Awp.Enabled);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteList(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }
}
```

- [ ] **Step 6: Lancer les tests et vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --filter "FullyQualifiedName~CatalogExportTests"`
Expected: 4 tests réussis.

- [ ] **Step 7: Commit**

```bash
git add contract src/RetakeV4.Domain/Loadouts/CatalogExport.cs tests/RetakeV4.Domain.Tests
git commit -m "feat: export du catalogue d'armes au format du panel (même règle que le menu en jeu) et fixture de contrat"
```

---

### Task 2: Synchronisation des préférences (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Preferences/PreferenceSync.cs`
- Create: `src/RetakeV4.Domain/Preferences/PlayerSnapshot.cs`
- Modify: `src/RetakeV4.Domain/Preferences/PreferenceBook.cs` (ajout de `Replace`)
- Create: `tests/RetakeV4.Domain.Tests/Preferences/PreferenceSyncTests.cs`
- Modify: `tests/RetakeV4.Domain.Tests/Preferences/PreferenceBookTests.cs`

**Interfaces:**
- Consumes: `PreferenceBook`, `StoredPreference`, `PreferenceKey`.
- Produces:
  - `record PlayerSnapshot(IReadOnlyList<StoredPreference> Preferences, string Stamp)`
  - `record PreferenceSync` : `Empty`, `const string NoRows = ""`, `EditOf(ulong) : long`, `Edited(ulong)`, `Flushed(ulong)`, `Stamped(ulong, string)`, `Forget(ulong)`, `Changed(IReadOnlyCollection<ulong> connected, IReadOnlyDictionary<ulong, string> current) : IReadOnlyList<ulong>`, `CanApply(ulong, long editAtRequest) : bool`
  - `PreferenceBook.Replace(ulong steamId, IEnumerable<StoredPreference> stored) : PreferenceBook`

- [ ] **Step 1: Écrire les tests (RED)**

`tests/RetakeV4.Domain.Tests/Preferences/PreferenceSyncTests.cs` :

```csharp
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Domain.Tests.Preferences;

public class PreferenceSyncTests
{
    private const ulong Alice = 76561198000000001UL;
    private const ulong Bob = 76561198000000002UL;

    private static Dictionary<ulong, string> Current(params (ulong Id, string Stamp)[] stamps) =>
        stamps.ToDictionary(s => s.Id, s => s.Stamp);

    [Fact]
    public void SameStamp_IsNotChanged()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "2026-10-01 12:00:00.000001");
        Assert.Empty(sync.Changed(new[] { Alice }, Current((Alice, "2026-10-01 12:00:00.000001"))));
    }

    [Fact]
    public void DifferentStamp_IsChanged()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "a").Stamped(Bob, "b");
        Assert.Equal(new[] { Alice }, sync.Changed(new[] { Alice, Bob }, Current((Alice, "a2"), (Bob, "b"))));
    }

    [Fact]
    public void RowsDeletedElsewhere_AreChanged()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "a");
        Assert.Equal(new[] { Alice }, sync.Changed(new[] { Alice }, Current()));
    }

    [Fact]
    public void PlayerWithoutRows_StaysUnchanged()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, PreferenceSync.NoRows);
        Assert.Empty(sync.Changed(new[] { Alice }, Current()));
    }

    // A player whose first load failed (database down) is retried at the next check.
    [Fact]
    public void UnstampedPlayer_IsChanged() =>
        Assert.Equal(new[] { Alice }, PreferenceSync.Empty.Changed(new[] { Alice }, Current()));

    [Fact]
    public void PendingWrite_IsNotReloaded()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "a").Edited(Alice);
        Assert.Empty(sync.Changed(new[] { Alice }, Current((Alice, "b"))));
        Assert.Equal(new[] { Alice }, sync.Flushed(Alice).Changed(new[] { Alice }, Current((Alice, "b"))));
    }

    [Fact]
    public void EditDuringReload_BlocksApply()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "a");
        var edit = sync.EditOf(Alice);
        Assert.True(sync.CanApply(Alice, edit));
        var edited = sync.Edited(Alice).Flushed(Alice);
        Assert.False(edited.CanApply(Alice, edit));
    }

    [Fact]
    public void PendingWrite_BlocksApply()
    {
        var sync = PreferenceSync.Empty.Edited(Alice);
        Assert.False(sync.CanApply(Alice, sync.EditOf(Alice)));
    }

    [Fact]
    public void Flushed_NeverGoesBelowZero()
    {
        var sync = PreferenceSync.Empty.Flushed(Alice);
        Assert.True(sync.CanApply(Alice, sync.EditOf(Alice)));
    }

    [Fact]
    public void Forget_RemovesEverything()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "a").Edited(Alice).Forget(Alice);
        Assert.Empty(sync.Stamps);
        Assert.Empty(sync.Edits);
        Assert.Empty(sync.Pending);
    }
}
```

Ajouter à la fin de `PreferenceBookTests` (avant la dernière accolade) :

```csharp
    [Fact]
    public void Replace_SwapsAllEntriesOfThePlayer()
    {
        var book = Loaded(Alice, Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1_silencer"), Weapons(Alice, TeamSide.T, "Mid", "weapon_mac10"));
        var replaced = book.Replace(Alice, new[] { Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_aug") });
        Assert.Equal("weapon_aug", replaced.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
        Assert.Null(replaced.RequestFor(Alice, TeamSide.T, "Mid"));
    }

    [Fact]
    public void Replace_KeepsOtherPlayers()
    {
        var (withBob, bobToken) = Loaded(Alice).StartSession(Bob);
        var book = withBob.WithPlayer(Bob, bobToken, new[] { Weapons(Bob, TeamSide.T, "Mid", "weapon_mp9") });
        Assert.Equal("weapon_mp9", book.Replace(Alice, Array.Empty<StoredPreference>()).RequestFor(Bob, TeamSide.T, "Mid")?.Primary);
    }

    [Fact]
    public void Replace_IgnoresDisconnectedPlayers()
    {
        var book = Loaded(Alice).WithoutPlayer(Alice);
        Assert.Same(book, book.Replace(Alice, new[] { Weapons(Alice, TeamSide.T, "Mid", "weapon_mp9") }));
    }

    [Fact]
    public void Replace_IgnoresRowsOfOtherPlayers()
    {
        var book = Loaded(Alice).Replace(Alice, new[] { Weapons(Bob, TeamSide.T, "Mid", "weapon_mp9") });
        Assert.Null(book.RequestFor(Bob, TeamSide.T, "Mid"));
    }
```

- [ ] **Step 2: Lancer les tests et vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --filter "FullyQualifiedName~Preferences"`
Expected: échec de compilation (`PreferenceSync`, `Replace` introuvables).

- [ ] **Step 3: Implémenter**

`src/RetakeV4.Domain/Preferences/PlayerSnapshot.cs` :

```csharp
namespace RetakeV4.Domain.Preferences;

// A player's stored rows with their stamp (latest updated_at, compared for equality only).
public sealed record PlayerSnapshot(IReadOnlyList<StoredPreference> Preferences, string Stamp);
```

`src/RetakeV4.Domain/Preferences/PreferenceSync.cs` :

```csharp
using System.Collections.Immutable;

namespace RetakeV4.Domain.Preferences;

// Detects preferences changed outside the game (web panel) without trusting any clock: a player's stamp is compared for
// equality only. A player with in-game writes not yet saved, or who edited since a reload was requested, keeps his choice.
public sealed record PreferenceSync(
    ImmutableDictionary<ulong, string> Stamps,
    ImmutableDictionary<ulong, long> Edits,
    ImmutableDictionary<ulong, int> Pending)
{
    public const string NoRows = "";

    public static PreferenceSync Empty { get; } = new(
        ImmutableDictionary<ulong, string>.Empty, ImmutableDictionary<ulong, long>.Empty, ImmutableDictionary<ulong, int>.Empty);

    public long EditOf(ulong steamId) => Edits.GetValueOrDefault(steamId);

    public PreferenceSync Edited(ulong steamId) => this with
    {
        Edits = Edits.SetItem(steamId, EditOf(steamId) + 1),
        Pending = Pending.SetItem(steamId, Pending.GetValueOrDefault(steamId) + 1),
    };

    public PreferenceSync Flushed(ulong steamId)
    {
        var left = Pending.GetValueOrDefault(steamId) - 1;
        return this with { Pending = left > 0 ? Pending.SetItem(steamId, left) : Pending.Remove(steamId) };
    }

    public PreferenceSync Stamped(ulong steamId, string stamp) => this with { Stamps = Stamps.SetItem(steamId, stamp) };

    public PreferenceSync Forget(ulong steamId) => this with
    {
        Stamps = Stamps.Remove(steamId),
        Edits = Edits.Remove(steamId),
        Pending = Pending.Remove(steamId),
    };

    public IReadOnlyList<ulong> Changed(IReadOnlyCollection<ulong> connected, IReadOnlyDictionary<ulong, string> current) =>
        connected
            .Where(id => !Pending.ContainsKey(id))
            .Where(id => !Stamps.TryGetValue(id, out var known) || known != current.GetValueOrDefault(id, NoRows))
            .ToList();

    public bool CanApply(ulong steamId, long editAtRequest) => !Pending.ContainsKey(steamId) && EditOf(steamId) == editAtRequest;
}
```

Dans `src/RetakeV4.Domain/Preferences/PreferenceBook.cs`, ajouter après `WithoutPlayer` :

```csharp
    // The database is the reference (changed outside the game): every entry of the player is replaced.
    public PreferenceBook Replace(ulong steamId, IEnumerable<StoredPreference> stored)
    {
        if (!Sessions.ContainsKey(steamId))
        {
            return this;
        }
        var kept = Entries.RemoveRange(Entries.Keys.Where(k => k.SteamId == steamId));
        var fresh = stored.Where(s => s.Key.SteamId == steamId).Select(s => KeyValuePair.Create(s.Key, s.Preference));
        return this with { Entries = kept.SetItems(fresh) };
    }
```

- [ ] **Step 4: Lancer les tests et vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --filter "FullyQualifiedName~Preferences"`
Expected: tous les tests de `PreferenceSyncTests` et `PreferenceBookTests` réussis.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Preferences tests/RetakeV4.Domain.Tests/Preferences
git commit -m "feat: détection des préférences modifiées hors du jeu par tampon, sans écraser un choix en jeu non enregistré"
```

---

### Task 3: Persistance — catalogue, tampons, instantanés

**Files:**
- Create: `src/RetakeV4/Persistence/PublishedCatalog.cs`
- Modify: `src/RetakeV4/Persistence/IPreferenceRepository.cs`
- Modify: `src/RetakeV4/Persistence/SqlitePreferenceRepository.cs`
- Modify: `src/RetakeV4/Persistence/MySqlPreferenceRepository.cs`
- Modify: `src/RetakeV4/Persistence/NoOpPreferenceRepository.cs`
- Modify: `src/RetakeV4/Persistence/ResilientPreferenceStore.cs`
- Modify: `tests/RetakeV4.Integration.Tests/Persistence/FakePreferenceRepository.cs`
- Modify: `tests/RetakeV4.Integration.Tests/Persistence/SqlitePreferenceRepositoryTests.cs`
- Modify: `tests/RetakeV4.Integration.Tests/Persistence/ResilientPreferenceStoreTests.cs`
- Modify: `tests/RetakeV4.Integration.Tests/Persistence/MySqlPreferenceRepositoryTests.cs`
- Create: `contract/player_loadout.json`
- Create: `tests/RetakeV4.Integration.Tests/Persistence/PanelContractTests.cs`
- Modify: `tests/RetakeV4.Integration.Tests/RetakeV4.Integration.Tests.csproj`

**Interfaces:**
- Consumes: `PlayerSnapshot`, `PreferenceSync.NoRows` (Task 2).
- Produces (sur `IPreferenceRepository`) :
  - `Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct)` — jamais `null` pour un dépôt réel ; `null` = indisponible (`ResilientPreferenceStore`).
  - `Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct)` — joueurs sans ligne absents du dictionnaire ; `null` = indisponible.
  - `Task PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct)`
  - `record PublishedCatalog(string ServerKey, int FormatVersion, string Json)`
- Fake : `Dictionary<ulong, string> StampValues`, `int SnapshotCalls`, `ConcurrentQueue<PublishedCatalog> Published`.

- [ ] **Step 1: Fixture `contract/player_loadout.json` et copie dans les tests d'intégration**

```json
{
  "table": "player_loadout",
  "teams": { "T": 0, "CT": 1 },
  "anyRoundType": "*",
  "rows": [
    { "steam_id": "76561198000000001", "team": 1, "round_type": "FullBuy", "primary_weapon": "weapon_m4a1_silencer", "secondary_weapon": null, "awp_opt_in": 0, "updated_at": "2026-10-01 12:00:00.000001" },
    { "steam_id": "76561198000000001", "team": 0, "round_type": "*", "primary_weapon": null, "secondary_weapon": null, "awp_opt_in": 1, "updated_at": "2026-10-01 12:00:00.000002" }
  ]
}
```

Dans `tests/RetakeV4.Integration.Tests/RetakeV4.Integration.Tests.csproj`, ajouter avant `</Project>` :

```xml
  <ItemGroup>
    <None Include="..\..\contract\**\*.json" LinkBase="contract" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Écrire les tests (RED)**

`tests/RetakeV4.Integration.Tests/Persistence/PanelContractTests.cs` :

```csharp
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

// Rows written the way the web panel writes them must load as the plugin expects.
public sealed class PanelContractTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private static JsonElement Contract() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "player_loadout.json"))).RootElement;

    [Fact]
    public void TeamValues_AndAwpKey_MatchThePlugin()
    {
        var contract = Contract();
        Assert.Equal((int)TeamSide.T, contract.GetProperty("teams").GetProperty("T").GetInt32());
        Assert.Equal((int)TeamSide.CT, contract.GetProperty("teams").GetProperty("CT").GetInt32());
        Assert.Equal(PreferenceKey.AnyRoundType, contract.GetProperty("anyRoundType").GetString());
    }

    [Fact]
    public async Task RowsWrittenByThePanel_LoadAsPreferences()
    {
        var file = Path.Combine(_dir.Path, "retakev4.db");
        var repository = new SqlitePreferenceRepository(file);
        await repository.InitializeAsync(CancellationToken.None);
        await using (var connection = new SqliteConnection($"Data Source={file}"))
        {
            await connection.OpenAsync();
            foreach (var row in Contract().GetProperty("rows").EnumerateArray())
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO player_loadout (steam_id, team, round_type, primary_weapon, secondary_weapon, awp_opt_in, updated_at)
                    VALUES (@steam_id, @team, @round_type, @primary, @secondary, @awp, @updated_at)
                    """;
                insert.Parameters.AddWithValue("@steam_id", unchecked((long)ulong.Parse(row.GetProperty("steam_id").GetString()!)));
                insert.Parameters.AddWithValue("@team", row.GetProperty("team").GetInt32());
                insert.Parameters.AddWithValue("@round_type", row.GetProperty("round_type").GetString());
                insert.Parameters.AddWithValue("@primary", (object?)row.GetProperty("primary_weapon").GetString() ?? DBNull.Value);
                insert.Parameters.AddWithValue("@secondary", (object?)row.GetProperty("secondary_weapon").GetString() ?? DBNull.Value);
                insert.Parameters.AddWithValue("@awp", row.GetProperty("awp_opt_in").GetInt32());
                insert.Parameters.AddWithValue("@updated_at", row.GetProperty("updated_at").GetString());
                await insert.ExecuteNonQueryAsync();
            }
        }
        var snapshot = await repository.SnapshotAsync(76561198000000001UL, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("2026-10-01 12:00:00.000002", snapshot.Stamp);
        Assert.Contains(new StoredPreference(new PreferenceKey(76561198000000001UL, TeamSide.CT, "FullBuy"), new LoadoutPreference("weapon_m4a1_silencer", null, false)), snapshot.Preferences);
        Assert.Contains(new StoredPreference(new PreferenceKey(76561198000000001UL, TeamSide.T, PreferenceKey.AnyRoundType), new LoadoutPreference(null, null, true)), snapshot.Preferences);
    }
}
```

Ajouter à `SqlitePreferenceRepositoryTests` (avant la dernière accolade) :

```csharp
    private async Task SetStamp(ulong steamId, string stamp)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_dir.Path, "data", "retakev4.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE player_loadout SET updated_at = @stamp WHERE steam_id = @steam_id";
        command.Parameters.AddWithValue("@stamp", stamp);
        command.Parameters.AddWithValue("@steam_id", unchecked((long)steamId));
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Stamps_ReturnTheLatestUpdate_OnlyForPlayersWithRows()
    {
        var repository = await Repository();
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, "Mid", "weapon_mac10"), CancellationToken.None);
        await SetStamp(Alice, "2026-10-01T12:00:00.0000000+00:00");
        var stamps = await repository.StampsAsync(new[] { Alice, Alice + 1 }, CancellationToken.None);
        Assert.Equal("2026-10-01T12:00:00.0000000+00:00", Assert.Single(stamps!).Value);
        Assert.True(stamps!.ContainsKey(Alice));
    }

    [Fact]
    public async Task Stamps_OfNobody_AreEmpty() =>
        Assert.Empty((await (await Repository()).StampsAsync(Array.Empty<ulong>(), CancellationToken.None))!);

    [Fact]
    public async Task Stamps_HandleLargeSteamIds()
    {
        var repository = await Repository();
        const ulong huge = ulong.MaxValue - 1;
        await repository.UpsertAsync(Preference(huge, TeamSide.CT, "Pistol", null), CancellationToken.None);
        Assert.True((await repository.StampsAsync(new[] { huge }, CancellationToken.None))!.ContainsKey(huge));
        Assert.Equal(huge, Assert.Single((await repository.SnapshotAsync(huge, CancellationToken.None))!.Preferences).Key.SteamId);
    }

    [Fact]
    public async Task Snapshot_OfAPlayerWithoutRows_HasNoRowsStamp()
    {
        var snapshot = await (await Repository()).SnapshotAsync(Alice, CancellationToken.None);
        Assert.Empty(snapshot!.Preferences);
        Assert.Equal(PreferenceSync.NoRows, snapshot.Stamp);
    }

    [Fact]
    public async Task PublishCatalog_StoresOneRowPerServer()
    {
        var repository = await Repository();
        await repository.PublishCatalogAsync(new PublishedCatalog("default", 1, """{"roundTypes":[]}"""), CancellationToken.None);
        await repository.PublishCatalogAsync(new PublishedCatalog("default", 1, """{"roundTypes":[{"name":"Pistol"}]}"""), CancellationToken.None);
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_dir.Path, "data", "retakev4.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MAX(catalog), MAX(format_version) FROM retake_catalog WHERE server_key = 'default'";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Contains("Pistol", reader.GetString(1), StringComparison.Ordinal);
        Assert.Equal(1, reader.GetInt32(2));
    }

    [Fact]
    public async Task ExistingV1Database_GetsTheCatalogTable()
    {
        var file = Path.Combine(_dir.Path, "data", "retakev4.db");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await using (var connection = new SqliteConnection($"Data Source={file}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE player_loadout (steam_id INTEGER NOT NULL, team INTEGER NOT NULL, round_type TEXT NOT NULL,
                    primary_weapon TEXT NULL, secondary_weapon TEXT NULL, awp_opt_in INTEGER NOT NULL DEFAULT 0,
                    updated_at TEXT NOT NULL, PRIMARY KEY (steam_id, team, round_type));
                CREATE TABLE schema_version (version INTEGER NOT NULL);
                INSERT INTO schema_version (version) VALUES (1);
                INSERT INTO player_loadout VALUES (1, 0, 'Mid', 'weapon_mac10', NULL, 0, 'x');
                """;
            await command.ExecuteNonQueryAsync();
        }
        var repository = await Repository();
        await repository.PublishCatalogAsync(new PublishedCatalog("default", 1, "{}"), CancellationToken.None);
        Assert.Single(await repository.LoadAsync(1, CancellationToken.None));
    }
```

Ajouter à `ResilientPreferenceStoreTests` (avant la dernière accolade) :

```csharp
    [Fact]
    public async Task Snapshot_AndStamps_AreNull_WhenTheDatabaseIsDown()
    {
        _inner.Fail = true;
        var store = Store();
        Assert.Null(await store.SnapshotAsync(1, CancellationToken.None));
        Assert.Null(await store.StampsAsync(new ulong[] { 1 }, CancellationToken.None));
        await store.PublishCatalogAsync(new PublishedCatalog("default", 1, "{}"), CancellationToken.None);
        Assert.Single(_logger.Entries);
    }

    [Fact]
    public async Task Snapshot_AndStamps_PassThrough_WhenHealthy()
    {
        _inner.Stored.Add(Preference(1));
        _inner.StampValues[1] = "s1";
        var store = Store();
        Assert.Equal("s1", (await store.SnapshotAsync(1, CancellationToken.None))!.Stamp);
        Assert.Equal("s1", (await store.StampsAsync(new ulong[] { 1 }, CancellationToken.None))![1]);
        await store.PublishCatalogAsync(new PublishedCatalog("default", 1, "{}"), CancellationToken.None);
        Assert.Single(_inner.Published);
    }
```

Ajouter à `MySqlPreferenceRepositoryTests` (avant la dernière accolade) :

```csharp
    [Fact]
    public async Task StampsSnapshotAndCatalog_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var repository = new MySqlPreferenceRepository(ConnectionString);
        await repository.InitializeAsync(CancellationToken.None);
        var steamId = (ulong)Random.Shared.NextInt64(1, long.MaxValue) + (ulong)long.MaxValue;
        var preference = new StoredPreference(new PreferenceKey(steamId, TeamSide.T, "Mid"), new LoadoutPreference("weapon_mac10", null, false));
        await repository.UpsertAsync(preference, CancellationToken.None);
        var stamps = await repository.StampsAsync(new[] { steamId }, CancellationToken.None);
        var snapshot = await repository.SnapshotAsync(steamId, CancellationToken.None);
        Assert.Equal(stamps![steamId], snapshot!.Stamp);
        Assert.Equal(preference, Assert.Single(snapshot.Preferences));
        await repository.PublishCatalogAsync(new PublishedCatalog("test", 1, """{"roundTypes":[]}"""), CancellationToken.None);
    }
```

- [ ] **Step 3: Lancer les tests et vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Integration.Tests --filter "FullyQualifiedName~Persistence"`
Expected: échec de compilation (`SnapshotAsync`, `StampsAsync`, `PublishCatalogAsync`, `PublishedCatalog` introuvables).

- [ ] **Step 4: Interface et record**

`src/RetakeV4/Persistence/PublishedCatalog.cs` :

```csharp
namespace RetakeV4.Persistence;

public sealed record PublishedCatalog(string ServerKey, int FormatVersion, string Json);
```

`src/RetakeV4/Persistence/IPreferenceRepository.cs` devient :

```csharp
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public interface IPreferenceRepository
{
    Task InitializeAsync(CancellationToken ct);

    Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct);

    // Null only when the store is unavailable: a player without rows gets an empty snapshot stamped PreferenceSync.NoRows.
    Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct);

    // Players without rows are absent. Null only when the store is unavailable.
    Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct);

    Task UpsertAsync(StoredPreference preference, CancellationToken ct);

    Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct);

    Task PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct);
}
```

- [ ] **Step 5: SQLite**

Dans `SqlitePreferenceRepository.cs` :

Ajouter un second élément au tableau `Migrations` (après la création de `player_loadout`) :

```csharp
        """
        CREATE TABLE IF NOT EXISTS retake_catalog (
            server_key TEXT NOT NULL PRIMARY KEY,
            format_version INTEGER NOT NULL,
            catalog TEXT NOT NULL,
            updated_at TEXT NOT NULL)
        """,
```

Ajouter la constante :

```csharp
    private const string PublishCatalogSql = """
        INSERT INTO retake_catalog (server_key, format_version, catalog, updated_at)
        VALUES (@server_key, @format_version, @catalog, @updated_at)
        ON CONFLICT (server_key) DO UPDATE SET
            format_version = excluded.format_version,
            catalog = excluded.catalog,
            updated_at = excluded.updated_at
        """;
```

Remplacer `LoadAsync` par une version qui partage la lecture avec `SnapshotAsync`, et ajouter les nouvelles méthodes :

```csharp
    public async Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await ReadRowsAsync(connection, steamId, ct).ConfigureAwait(false);
    }

    public async Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var rows = await ReadRowsAsync(connection, steamId, ct).ConfigureAwait(false);
        var stamps = await ReadStampsAsync(connection, new[] { steamId }, ct).ConfigureAwait(false);
        return new PlayerSnapshot(rows, stamps.GetValueOrDefault(steamId, PreferenceSync.NoRows));
    }

    public async Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct)
    {
        if (steamIds.Count == 0)
        {
            return new Dictionary<ulong, string>();
        }
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await ReadStampsAsync(connection, steamIds, ct).ConfigureAwait(false);
    }

    public async Task PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = PublishCatalogSql;
        command.Parameters.AddWithValue("@server_key", catalog.ServerKey);
        command.Parameters.AddWithValue("@format_version", catalog.FormatVersion);
        command.Parameters.AddWithValue("@catalog", catalog.Json);
        command.Parameters.AddWithValue("@updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<StoredPreference>> ReadRowsAsync(SqliteConnection connection, ulong steamId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT team, round_type, primary_weapon, secondary_weapon, awp_opt_in FROM player_loadout WHERE steam_id = @steam_id";
        command.Parameters.AddWithValue("@steam_id", unchecked((long)steamId));
        var result = new List<StoredPreference>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var team = reader.GetInt32(0) == (int)TeamSide.T ? TeamSide.T : TeamSide.CT;
            var preference = new LoadoutPreference(
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4) != 0);
            result.Add(new StoredPreference(new PreferenceKey(steamId, team, reader.GetString(1)), preference));
        }
        return result;
    }

    private static async Task<Dictionary<ulong, string>> ReadStampsAsync(SqliteConnection connection, IReadOnlyCollection<ulong> steamIds, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        var names = new List<string>();
        foreach (var steamId in steamIds)
        {
            var name = $"@s{names.Count}";
            command.Parameters.AddWithValue(name, unchecked((long)steamId));
            names.Add(name);
        }
        command.CommandText = $"SELECT steam_id, MAX(updated_at) FROM player_loadout WHERE steam_id IN ({string.Join(", ", names)}) GROUP BY steam_id";
        var result = new Dictionary<ulong, string>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result[unchecked((ulong)reader.GetInt64(0))] = reader.GetString(1);
        }
        return result;
    }
```

- [ ] **Step 6: MySQL**

Dans `MySqlPreferenceRepository.cs`, même structure :

Second élément de `Migrations` :

```csharp
        """
        CREATE TABLE IF NOT EXISTS retake_catalog (
            server_key VARCHAR(64) NOT NULL PRIMARY KEY,
            format_version INT NOT NULL,
            catalog MEDIUMTEXT NOT NULL,
            updated_at DATETIME(6) NOT NULL)
        """,
```

Constante :

```csharp
    private const string PublishCatalogSql = """
        INSERT INTO retake_catalog (server_key, format_version, catalog, updated_at)
        VALUES (@server_key, @format_version, @catalog, @updated_at)
        ON DUPLICATE KEY UPDATE
            format_version = VALUES(format_version),
            catalog = VALUES(catalog),
            updated_at = VALUES(updated_at)
        """;
```

Méthodes :

```csharp
    public async Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await ReadRowsAsync(connection, steamId, ct).ConfigureAwait(false);
    }

    public async Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var rows = await ReadRowsAsync(connection, steamId, ct).ConfigureAwait(false);
        var stamps = await ReadStampsAsync(connection, new[] { steamId }, ct).ConfigureAwait(false);
        return new PlayerSnapshot(rows, stamps.GetValueOrDefault(steamId, PreferenceSync.NoRows));
    }

    public async Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct)
    {
        if (steamIds.Count == 0)
        {
            return new Dictionary<ulong, string>();
        }
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await ReadStampsAsync(connection, steamIds, ct).ConfigureAwait(false);
    }

    public async Task PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = PublishCatalogSql;
        command.Parameters.AddWithValue("@server_key", catalog.ServerKey);
        command.Parameters.AddWithValue("@format_version", catalog.FormatVersion);
        command.Parameters.AddWithValue("@catalog", catalog.Json);
        command.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<StoredPreference>> ReadRowsAsync(MySqlConnection connection, ulong steamId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT team, round_type, primary_weapon, secondary_weapon, awp_opt_in FROM player_loadout WHERE steam_id = @steam_id";
        command.Parameters.AddWithValue("@steam_id", steamId);
        var result = new List<StoredPreference>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var team = reader.GetInt32(0) == (int)TeamSide.T ? TeamSide.T : TeamSide.CT;
            var preference = new LoadoutPreference(
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4));
            result.Add(new StoredPreference(new PreferenceKey(steamId, team, reader.GetString(1)), preference));
        }
        return result;
    }

    private static async Task<Dictionary<ulong, string>> ReadStampsAsync(MySqlConnection connection, IReadOnlyCollection<ulong> steamIds, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        var names = new List<string>();
        foreach (var steamId in steamIds)
        {
            var name = $"@s{names.Count}";
            command.Parameters.AddWithValue(name, steamId);
            names.Add(name);
        }
        command.CommandText = $"""
            SELECT steam_id, DATE_FORMAT(MAX(updated_at), '%Y-%m-%d %H:%i:%s.%f')
            FROM player_loadout WHERE steam_id IN ({string.Join(", ", names)}) GROUP BY steam_id
            """;
        var result = new Dictionary<ulong, string>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result[reader.GetUInt64(0)] = reader.GetString(1);
        }
        return result;
    }
```

- [ ] **Step 7: NoOp, Resilient, Fake**

`NoOpPreferenceRepository` — ajouter :

```csharp
    public Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct) =>
        Task.FromResult<PlayerSnapshot?>(new PlayerSnapshot(Array.Empty<StoredPreference>(), PreferenceSync.NoRows));

    public Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<ulong, string>?>(new Dictionary<ulong, string>());

    public Task PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct) => Task.CompletedTask;
```

`ResilientPreferenceStore` — ajouter après `LoadAsync` :

```csharp
    public Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct) =>
        GuardedAsync("load", () => _inner.SnapshotAsync(steamId, ct), ct);

    public Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct) =>
        GuardedAsync("check", () => _inner.StampsAsync(steamIds, ct), ct);

    public async Task PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct) =>
        await GuardedAsync<object>("publish catalog", async () =>
        {
            await _inner.PublishCatalogAsync(catalog, ct).ConfigureAwait(false);
            return new object();
        }, ct).ConfigureAwait(false);

    private async Task<T?> GuardedAsync<T>(string operation, Func<Task<T?>> action, CancellationToken ct) where T : class
    {
        if (!IsAvailable)
        {
            return null;
        }
        try
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            var result = await action().ConfigureAwait(false);
            MarkHealthy();
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MarkFailed(ex, operation);
            return null;
        }
    }
```

`FakePreferenceRepository` (tests) — ajouter `using System.Collections.Concurrent;` et :

```csharp
    public Dictionary<ulong, string> StampValues { get; } = new();

    public int SnapshotCalls { get; private set; }

    public ConcurrentQueue<PublishedCatalog> Published { get; } = new();

    public Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct)
    {
        SnapshotCalls++;
        ThrowIfFailing();
        var rows = Stored.Where(s => s.Key.SteamId == steamId).ToList();
        return Task.FromResult<PlayerSnapshot?>(new PlayerSnapshot(rows, StampValues.GetValueOrDefault(steamId, PreferenceSync.NoRows)));
    }

    public Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct)
    {
        ThrowIfFailing();
        IReadOnlyDictionary<ulong, string> stamps = StampValues.Where(s => steamIds.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value);
        return Task.FromResult<IReadOnlyDictionary<ulong, string>?>(stamps);
    }

    public Task PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct)
    {
        ThrowIfFailing();
        Published.Enqueue(catalog);
        return Task.CompletedTask;
    }
```

- [ ] **Step 8: Lancer les tests et vérifier le succès**

Run: `dotnet test tests/RetakeV4.Integration.Tests --filter "FullyQualifiedName~Persistence"`
Expected: tous réussis (les tests MySQL retournent tout de suite sans `RETAKEV4_MYSQL_TEST`).

- [ ] **Step 9: Commit**

```bash
git add contract src/RetakeV4/Persistence tests/RetakeV4.Integration.Tests
git commit -m "feat: table retake_catalog, instantanés et tampons de préférences (SQLite, MySQL), contrat de player_loadout avec le panel"
```

---

### Task 4: Clé de serveur dans la config

**Files:**
- Modify: `src/RetakeV4/Modules/Allocation/AllocationConfig.cs`
- Modify: `src/RetakeV4/Modules/Allocation/AllocationConfigValidator.cs`
- Modify: `tests/RetakeV4.Integration.Tests/Modules/Allocation/AllocationConfigValidatorTests.cs`

**Interfaces:**
- Produces: `DatabaseConfig.ServerKey : string` (défaut `"default"`, validé).

- [ ] **Step 1: Écrire les tests (RED)**

Ajouter à `AllocationConfigValidatorTests` :

```csharp
    [Fact]
    public void ServerKey_DefaultsToDefault() => Assert.Equal("default", Defaults.Database.ServerKey);

    [Theory]
    [InlineData("")]
    [InlineData("my server")]
    [InlineData("a'; DROP TABLE x;--")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public void InvalidServerKey_FallsBackToDefault(string key)
    {
        var config = Validate(new DatabaseConfig { ServerKey = key }, out var issues);
        Assert.Equal("default", config.Database.ServerKey);
        Assert.Equal(1, issues);
    }

    [Theory]
    [InlineData("agora-1")]
    [InlineData("Retake_EU")]
    public void ValidServerKey_IsKept(string key)
    {
        var config = Validate(new DatabaseConfig { ServerKey = key }, out var issues);
        Assert.Equal(key, config.Database.ServerKey);
        Assert.Equal(0, issues);
    }
```

- [ ] **Step 2: Lancer les tests et vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Integration.Tests --filter "FullyQualifiedName~AllocationConfigValidatorTests"`
Expected: échec de compilation (`ServerKey` introuvable).

- [ ] **Step 3: Implémenter**

`DatabaseConfig` — ajouter :

```csharp
    // Identifies this server's row in retake_catalog, read by the web panel.
    public string ServerKey { get; init; } = "default";
```

`AllocationConfigValidator` — ajouter la regex et la vérification (après celle de `MySqlConnectionString`) :

```csharp
    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,64}\z")]
    private static partial Regex SafeServerKey();
```

```csharp
        if (!SafeServerKey().IsMatch(database.ServerKey ?? string.Empty))
        {
            issues.Add(new ConfigIssue(file, "Database.ServerKey", "must be 1 to 64 letters, digits, _ or -; using default"));
            database = database with { ServerKey = defaults.Database.ServerKey };
        }
```

- [ ] **Step 4: Lancer les tests et vérifier le succès**

Run: `dotnet test tests/RetakeV4.Integration.Tests --filter "FullyQualifiedName~AllocationConfigValidatorTests"`
Expected: tous réussis.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4/Modules/Allocation tests/RetakeV4.Integration.Tests/Modules/Allocation
git commit -m "feat: clé de serveur Database.ServerKey pour le catalogue du panel"
```

---

### Task 5: PreferenceService — rechargement et publication

**Files:**
- Modify: `src/RetakeV4/Persistence/PreferenceWriteQueue.cs`
- Modify: `src/RetakeV4/Modules/Allocation/PreferenceService.cs`
- Modify: `tests/RetakeV4.Integration.Tests/Persistence/PreferenceWriteQueueTests.cs`
- Create: `tests/RetakeV4.Integration.Tests/Modules/Allocation/PreferenceServiceTests.cs`

**Interfaces:**
- Consumes: `PreferenceSync`, `PlayerSnapshot`, `PreferenceBook.Replace` (Task 2) ; `SnapshotAsync`, `StampsAsync`, `PublishCatalogAsync`, `PublishedCatalog` (Task 3).
- Produces:
  - `PreferenceWriteQueue.Enqueue(StoredPreference preference, Action? written = null)` — `written` appelé après la tentative d'écriture (succès ou échec), sur le thread de la file.
  - `PreferenceService(IPreferenceRepository store, ILogger logger, Action<Action> onGameThread)`
  - `PreferenceService.PlayerConnected(ulong steamId)` (le callback passe au constructeur)
  - `PreferenceService.CheckForExternalChanges()`
  - `PreferenceService.PublishCatalog(string serverKey, string json)`

- [ ] **Step 1: Écrire les tests (RED)**

Ajouter à `PreferenceWriteQueueTests` :

```csharp
    [Fact]
    public async Task WrittenCallback_RunsAfterEachAttempt_EvenOnFailure()
    {
        var repository = new FakePreferenceRepository { FailUpsertFor = p => p.Key.SteamId == 1 };
        var queue = new PreferenceWriteQueue(repository, new ListLogger());
        var written = new System.Collections.Concurrent.ConcurrentQueue<ulong>();
        queue.Enqueue(Preference(1, "weapon_mac10"), () => written.Enqueue(1));
        queue.Enqueue(Preference(2, "weapon_galilar"), () => written.Enqueue(2));
        await queue.DisposeAsync();
        Assert.Equal(new ulong[] { 1, 2 }, written);
    }
```

`tests/RetakeV4.Integration.Tests/Modules/Allocation/PreferenceServiceTests.cs` :

```csharp
using System.Collections.Concurrent;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Integration.Tests.Persistence;
using RetakeV4.Modules.Allocation;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Modules.Allocation;

public sealed class PreferenceServiceTests : IAsyncDisposable
{
    private const ulong Alice = 76561198000000001UL;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly FakePreferenceRepository _store = new();
    private readonly BlockingCollection<Action> _gameThread = new();
    private readonly PreferenceService _service;

    public PreferenceServiceTests() => _service = new PreferenceService(_store, new ListLogger(), _gameThread.Add);

    public ValueTask DisposeAsync() => _service.DisposeAsync();

    private void RunGameThread()
    {
        Assert.True(_gameThread.TryTake(out var action, Wait), "nothing reached the game thread");
        action!();
    }

    private void AssertNothingForTheGameThread() => Assert.False(_gameThread.TryTake(out _, TimeSpan.FromMilliseconds(200)));

    private static StoredPreference FullBuy(string primary) =>
        new(new PreferenceKey(Alice, TeamSide.CT, "FullBuy"), new LoadoutPreference(primary, null, false));

    private void Connect(string stamp, string primary)
    {
        _store.Stored.Add(FullBuy(primary));
        _store.StampValues[Alice] = stamp;
        _service.PlayerConnected(Alice);
        RunGameThread();
    }

    private void ChangeFromThePanel(string stamp, string primary)
    {
        _store.Stored.Clear();
        _store.Stored.Add(FullBuy(primary));
        _store.StampValues[Alice] = stamp;
    }

    [Fact]
    public void Connect_LoadsThePreferences()
    {
        Connect("s1", "weapon_m4a1_silencer");
        Assert.Equal("weapon_m4a1_silencer", _service.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
    }

    [Fact]
    public void ChangeFromThePanel_IsReloaded()
    {
        Connect("s1", "weapon_m4a1_silencer");
        ChangeFromThePanel("s2", "weapon_aug");
        _service.CheckForExternalChanges();
        RunGameThread();
        RunGameThread();
        Assert.Equal("weapon_aug", _service.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
    }

    [Fact]
    public void UnchangedStamp_DoesNotReload()
    {
        Connect("s1", "weapon_m4a1_silencer");
        _service.CheckForExternalChanges();
        RunGameThread();
        AssertNothingForTheGameThread();
        Assert.Equal(1, _store.SnapshotCalls);
    }

    [Fact]
    public void DatabaseDown_DuringCheck_KeepsPreferences()
    {
        Connect("s1", "weapon_m4a1_silencer");
        _store.Fail = true;
        _service.CheckForExternalChanges();
        AssertNothingForTheGameThread();
        Assert.Equal("weapon_m4a1_silencer", _service.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
    }

    [Fact]
    public void DisconnectDuringReload_LeavesNoTrace()
    {
        Connect("s1", "weapon_m4a1_silencer");
        ChangeFromThePanel("s2", "weapon_aug");
        _service.CheckForExternalChanges();
        RunGameThread();
        _service.PlayerDisconnected(Alice);
        RunGameThread();
        Assert.Null(_service.RequestFor(Alice, TeamSide.CT, "FullBuy"));
        _service.CheckForExternalChanges();
        AssertNothingForTheGameThread();
    }

    [Fact]
    public void PublishCatalog_ReachesTheStore()
    {
        _service.PublishCatalog("agora-1", """{"roundTypes":[]}""");
        Assert.True(SpinWait.SpinUntil(() => !_store.Published.IsEmpty, Wait));
        Assert.Equal(new PublishedCatalog("agora-1", CatalogExport.FormatVersion, """{"roundTypes":[]}"""), Assert.Single(_store.Published));
    }
}
```

- [ ] **Step 2: Lancer les tests et vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Integration.Tests --filter "FullyQualifiedName~PreferenceServiceTests|FullyQualifiedName~PreferenceWriteQueueTests"`
Expected: échec de compilation (constructeur à 3 paramètres, `CheckForExternalChanges`, `PublishCatalog`, `Enqueue` à 2 paramètres introuvables).

- [ ] **Step 3: File d'écriture avec rappel**

`src/RetakeV4/Persistence/PreferenceWriteQueue.cs` : le canal transporte un couple.

```csharp
    private readonly Channel<(StoredPreference Preference, Action? Written)> _channel =
        Channel.CreateUnbounded<(StoredPreference, Action?)>(new UnboundedChannelOptions { SingleReader = true });
```

```csharp
    public void Enqueue(StoredPreference preference, Action? written = null) => _channel.Writer.TryWrite((preference, written));
```

```csharp
    private async Task RunAsync()
    {
        await foreach (var (preference, written) in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await _store.UpsertAsync(preference, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save a preference for {SteamId}", preference.Key.SteamId);
            }
            written?.Invoke();
        }
    }
```

- [ ] **Step 4: PreferenceService**

Remplacer `src/RetakeV4/Modules/Allocation/PreferenceService.cs` par :

```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Modules.Allocation;

// Game-thread state: every mutation of _book and _sync happens on the game thread (directly or via _onGameThread).
public sealed class PreferenceService : IAsyncDisposable
{
    private readonly IPreferenceRepository _store;
    private readonly ILogger _logger;
    private readonly Action<Action> _onGameThread;
    private readonly PreferenceWriteQueue _writes;
    private PreferenceBook _book = PreferenceBook.Empty;
    private PreferenceSync _sync = PreferenceSync.Empty;

    public PreferenceService(IPreferenceRepository store, ILogger logger, Action<Action> onGameThread)
    {
        _store = store;
        _logger = logger;
        _onGameThread = onGameThread;
        _writes = new PreferenceWriteQueue(store, logger);
    }

    public void PlayerConnected(ulong steamId)
    {
        var (book, token) = _book.StartSession(steamId);
        _book = book;
        InBackground("load", steamId, async () =>
        {
            var snapshot = await _store.SnapshotAsync(steamId, CancellationToken.None).ConfigureAwait(false);
            if (snapshot is null)
            {
                return;
            }
            _onGameThread(() =>
            {
                _book = _book.WithPlayer(steamId, token, snapshot.Preferences);
                if (_book.Sessions.GetValueOrDefault(steamId) == token)
                {
                    _sync = _sync.Stamped(steamId, snapshot.Stamp);
                }
            });
        });
    }

    public void PlayerDisconnected(ulong steamId)
    {
        _book = _book.WithoutPlayer(steamId);
        _sync = _sync.Forget(steamId);
    }

    public LoadoutPreference? RequestFor(ulong steamId, TeamSide team, string roundType) =>
        _book.RequestFor(steamId, team, roundType);

    public bool IsAwpVolunteer(ulong steamId) => _book.IsAwpVolunteer(steamId);

    public void SetWeapon(ulong steamId, TeamSide team, string roundType, WeaponSlot slot, string weapon)
    {
        var (book, change) = _book.SetWeapon(steamId, team, roundType, slot, weapon);
        _book = book;
        Save(change);
    }

    public bool ToggleAwp(ulong steamId)
    {
        var (book, changes, optIn) = _book.ToggleAwp(steamId);
        _book = book;
        foreach (var change in changes)
        {
            Save(change);
        }
        return optIn;
    }

    // Preferences edited on the web panel apply from the next round, without reconnecting.
    public void CheckForExternalChanges()
    {
        var connected = _book.Sessions.Keys.ToList();
        if (connected.Count == 0)
        {
            return;
        }
        InBackground("check", 0, async () =>
        {
            var current = await _store.StampsAsync(connected, CancellationToken.None).ConfigureAwait(false);
            if (current is not null)
            {
                _onGameThread(() => ReloadChanged(connected, current));
            }
        });
    }

    public void PublishCatalog(string serverKey, string json) =>
        InBackground("publish catalog", 0, () =>
            _store.PublishCatalogAsync(new PublishedCatalog(serverKey, CatalogExport.FormatVersion, json), CancellationToken.None));

    public async Task<int> ImportV3Async(string databaseFile, CancellationToken ct)
    {
        var rows = await V3SqliteReader.ReadAsync(databaseFile, ct).ConfigureAwait(false);
        var preferences = V3PreferenceImport.Convert(rows);
        await _store.ImportAsync(preferences, ct).ConfigureAwait(false);
        _logger.LogInformation("Imported {Count} V3 preference(s) from {File}", preferences.Count, databaseFile);
        return preferences.Count;
    }

    public ValueTask DisposeAsync() => _writes.DisposeAsync();

    private void Save(StoredPreference change)
    {
        var steamId = change.Key.SteamId;
        _sync = _sync.Edited(steamId);
        _writes.Enqueue(change, () => _onGameThread(() => _sync = _sync.Flushed(steamId)));
    }

    private void ReloadChanged(IReadOnlyCollection<ulong> connected, IReadOnlyDictionary<ulong, string> current)
    {
        foreach (var steamId in _sync.Changed(connected.Where(_book.Sessions.ContainsKey).ToList(), current))
        {
            var edit = _sync.EditOf(steamId);
            InBackground("reload", steamId, async () =>
            {
                var snapshot = await _store.SnapshotAsync(steamId, CancellationToken.None).ConfigureAwait(false);
                if (snapshot is not null)
                {
                    _onGameThread(() => ApplyReload(steamId, edit, snapshot));
                }
            });
        }
    }

    private void ApplyReload(ulong steamId, long edit, PlayerSnapshot snapshot)
    {
        if (!_book.Sessions.ContainsKey(steamId) || !_sync.CanApply(steamId, edit))
        {
            return;
        }
        _book = _book.Replace(steamId, snapshot.Preferences);
        _sync = _sync.Stamped(steamId, snapshot.Stamp);
    }

    private void InBackground(string operation, ulong steamId, Func<Task> work) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Preference {Operation} failed for {SteamId}", operation, steamId);
            }
        });
}
```

- [ ] **Step 5: Adapter l'appel dans `AllocationModule` pour que la solution compile**

Dans `AllocationModule.Load`, remplacer la création du service :

```csharp
        _preferences = new PreferenceService(CreateStore(context), context.Logger,
            apply => Server.NextFrame(() => _context?.Guard.Run(Name, "preferences", apply)));
```

Et `OnConnected` devient :

```csharp
    private void OnConnected(CCSPlayerController? player)
    {
        if (player is { IsValid: true, IsBot: false, IsHLTV: false } && player.SteamID != 0)
        {
            _preferences?.PlayerConnected(player.SteamID);
        }
    }
```

- [ ] **Step 6: Lancer les tests et vérifier le succès**

Run: `dotnet test RetakeV4.sln`
Expected: tous réussis, dont les 6 de `PreferenceServiceTests` et le nouveau test de la file.

- [ ] **Step 7: Commit**

```bash
git add src/RetakeV4/Persistence/PreferenceWriteQueue.cs src/RetakeV4/Modules/Allocation tests/RetakeV4.Integration.Tests
git commit -m "feat: rechargement des préférences modifiées hors du jeu et publication du catalogue par PreferenceService"
```

---

### Task 6: Branchement dans le module, docs et version 4.1.0

**Files:**
- Modify: `src/RetakeV4/Modules/Allocation/AllocationModule.cs`
- Modify: `src/RetakeV4/RetakeV4.csproj` (Version)
- Modify: `src/RetakeV4/RetakeV4Plugin.cs` (ModuleVersion)
- Modify: `README.md`
- Modify: `docs/CHECKLIST-INGAME.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `CatalogExport.Build` (Task 1), `DatabaseConfig.ServerKey` (Task 4), `PreferenceService.CheckForExternalChanges` / `PublishCatalog` (Task 5), événements `RoundTypesLoaded`, `EventRoundStart`.

- [ ] **Step 1: Brancher les événements**

Dans `AllocationModule.Load`, remplacer `hooks.OnBus<RoundTypesLoaded>(e => _definitions = e.Definitions);` par :

```csharp
        hooks.OnBus<RoundTypesLoaded>(e =>
        {
            _definitions = e.Definitions;
            _preferences?.PublishCatalog(_config.Database.ServerKey, CatalogExport.Build(e.Definitions));
        });
        hooks.OnEvent<EventRoundStart>("preferences_check", _ => _preferences?.CheckForExternalChanges());
```

- [ ] **Step 2: Version 4.1.0**

`src/RetakeV4/RetakeV4.csproj` : `<Version>4.1.0</Version>`. `src/RetakeV4/RetakeV4Plugin.cs` : `public override string ModuleVersion => "4.1.0";`

- [ ] **Step 3: Build et tests**

Run: `dotnet build RetakeV4.sln -c Release` puis `dotnet test RetakeV4.sln`
Expected: 0 warning, 0 erreur ; tous les tests réussis.

- [ ] **Step 4: Couverture Domain**

Run: `dotnet test tests/RetakeV4.Domain.Tests -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`
Expected: seuil de 80 % atteint.

- [ ] **Step 5: Documentation**

`README.md`, nouvelle section avant `## Développement` :

```markdown
## Panel web

Le panel web [CS2-RetakeV4-Panel](https://github.com/NeuTroNBZh/CS2-RetakeV4-Panel) permet aux joueurs de régler leurs armes depuis un navigateur (connexion Steam). Il partage la base MySQL du plugin :

- `Database.Type` doit valoir `MySql` dans `allocation.json` (le panel ne lit pas SQLite) ;
- `Database.ServerKey` (défaut `default`) identifie ce serveur dans la table `retake_catalog`, où le plugin publie les armes proposées à chaque chargement des types de round ;
- un choix fait sur le panel s'applique au round suivant, sans reconnexion.

Le format partagé est figé dans `contract/` (voir `contract/README.md`).
```

`docs/CHECKLIST-INGAME.md`, nouvelle section à la fin :

```markdown
## Phase panel (4.1.0)

- [ ] Avec `Database.Type = MySql`, au démarrage, la table `retake_catalog` contient une ligne `server_key = default` dont le JSON liste les types de round de `roundtypes.json`.
- [ ] Modifier `Database.ServerKey` en `test-1`, redémarrer : une ligne `test-1` apparaît.
- [ ] Joueur connecté : changer en base `primary_weapon` d'une de ses lignes (et `updated_at`), attendre le round suivant : l'arme reçue est la nouvelle, sans reconnexion.
- [ ] Choisir une arme dans `!guns` puis lancer un round : le choix reste (pas d'écrasement par l'ancienne valeur).
- [ ] Couper MySQL pendant la partie : les joueurs gardent leurs armes choisies, un seul avertissement par période de retry dans la console.
```

`CLAUDE.md`, ajouter dans `## Règles` :

```markdown
- Panel web : le format partagé (`player_loadout`, `retake_catalog`) est figé par `contract/` et ses tests ; toute modification se fait aussi dans `CS2-RetakeV4-Panel`. Le catalogue vient de `CatalogExport` (même règle que `WeaponMenu.Options`), la recharge des préférences de `PreferenceSync` (tampons comparés par égalité, jamais par horloge).
```

- [ ] **Step 6: Commit**

```bash
git add src README.md docs/CHECKLIST-INGAME.md CLAUDE.md
git commit -m "feat: publication du catalogue et contrôle des préférences au début de chaque round ; docs du panel ; version 4.1.0"
```
