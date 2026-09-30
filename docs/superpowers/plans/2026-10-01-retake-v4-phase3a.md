# RetakeV4 — Phase 3a Implementation Plan (préférences joueurs et persistance)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mémoriser les préférences d'équipement de chaque joueur (arme principale, secondaire par type de round et par camp, volontariat AWP), les persister en SQLite ou MySQL avec repli automatique en mémoire, les charger en arrière-plan à la connexion, les utiliser dans la distribution de l'équipement, permettre de s'inscrire à l'AWP avec `!awp`, et importer les préférences d'un serveur V3.

**Architecture:** Le Domain porte le carnet de préférences immuable (`PreferenceBook`) et la conversion des lignes V3. Le plugin porte l'infrastructure : dépôts SQLite / MySQL / NoOp derrière `IPreferenceRepository`, migrations versionnées, un magasin résilient (repli + réessai avec délai croissant) et une file d'écriture en arrière-plan. Le module `Allocation` relie le tout au jeu (connexion, déconnexion, distribution, commandes). Le menu d'armes HUD qui modifiera les préférences arrive en phase 3b.

**Tech Stack:** C# / .NET 10, CounterStrikeSharp.API 1.0.370, Microsoft.Data.Sqlite 10.0.9 + SQLitePCLRaw.bundle_e_sqlite3 3.0.3 (combinaison validée par V3), MySqlConnector 2.3.6, System.Threading.Channels, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-30-retake-v4-design.md` (§5.2 préférences, §9 persistance, §12 commandes)

## Global Constraints

- CounterStrikeSharp.API **1.0.370**, `net10.0`, `Nullable` + `TreatWarningsAsErrors`.
- `RetakeV4.Domain` ne référence **jamais** CounterStrikeSharp ni un pilote de base de données.
- Fonctions < 50 lignes, fichiers < 800 lignes, Domain immuable.
- Couverture Domain ≥ **80 %** (`-p:` et non `/p:` sous Git Bash).
- **Aucune** requête SQL construite par concaténation de valeurs : paramètres uniquement. Aucune chaîne de connexion dans le code source (elle vit dans `allocation.json`).
- Aucun accès base de données sur le thread de jeu : chargement via `Task.Run`, application via `Server.NextFrame` + `ModuleGuard` ; écritures via la file de fond.
- Textes joueurs via `lang/*.json` (en + fr) ; logs en anglais avec templates constants.
- Fichiers avec apostrophes : outil Write. Script de package : outil PowerShell.

## Décisions de conception

1. **Volontariat AWP** : stocké par joueur et par camp sous le type de round spécial `*` (`PreferenceKey.AnyRoundType`), fusionné avec la préférence du type de round au moment de la distribution. Équivalent V3 (table `FullBuyAWPChance` par joueur/camp) sans dépendre du nom des types de round.
2. **`!awp`** bascule le volontariat pour les deux camps d'un coup (un joueur change de camp à chaque rotation).
3. **Import V3** : lignes `Team = 0` (bug historique V3) ignorées, `AWPChance > 0` ⇒ volontaire, table `Pistol` ⇒ secondaire du type `Pistol`. Les armes inconnues sont conservées telles quelles et filtrées par le planificateur au moment de la distribution.
4. **Panne de base** : le magasin résilient renvoie « aucune préférence » et abandonne les écritures pendant la fenêtre de réessai (5 s, doublée à chaque échec, plafonnée à 5 min) ; les préférences déjà en cache restent actives.

## Review Focus

1. **Base indisponible au démarrage ou en cours de partie** (fichier verrouillé, MySQL coupé) : aucune exception remontée au jeu, distribution par défaut, réessai ultérieur, un seul avertissement par échec. Tests : `ResilientPreferenceStoreTests` (Task 3).
2. **Joueur qui se déconnecte avant la fin du chargement asynchrone** : ses préférences ne sont pas réinjectées dans le cache après son départ. Test : `PreferenceBookTests.WithPlayer_ForUnknownSession_IsIgnored` via le jeton de session (Task 1).
3. **Base V3 absente, corrompue ou sans certaines tables** : import partiel ou message d'échec, jamais de plantage. Tests : `V3SqliteReaderTests` (Task 4).
4. **SteamID64 proches de la limite** (> 2^63 impossible en pratique, mais ulong) : stockés et relus sans perte. Test : `SqlitePreferenceRepositoryTests.LargeSteamId_RoundTrips` (Task 4).
5. **Chemin SQLite ou chaîne MySQL invalide dans `allocation.json`** : repli sur une valeur sûre avec avertissement. Tests : `AllocationConfigValidatorTests` (Task 6).

---

## File Structure

```
src/RetakeV4.Domain/Preferences/PreferenceBook.cs, V3PreferenceImport.cs            (Tasks 1-2)
src/RetakeV4/Persistence/IPreferenceRepository.cs, NoOpPreferenceRepository.cs,
                         ResilientPreferenceStore.cs, PreferenceWriteQueue.cs        (Task 3)
src/RetakeV4/Persistence/SqlMigrator.cs, SqlitePreferenceRepository.cs, V3SqliteReader.cs (Task 4)
src/RetakeV4/Persistence/MySqlPreferenceRepository.cs                                (Task 5)
src/RetakeV4/Modules/Allocation/AllocationConfig.cs (+ Database), AllocationConfigValidator.cs,
                                PreferenceService.cs, AllocationModule.cs             (Task 6)
tests/RetakeV4.Domain.Tests/Preferences/…
tests/RetakeV4.Integration.Tests/Persistence/…, Modules/Allocation/AllocationConfigValidatorTests.cs
```

---

### Task 1: Carnet de préférences (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Preferences/PreferenceBook.cs`
- Test: `tests/RetakeV4.Domain.Tests/Preferences/PreferenceBookTests.cs`

**Interfaces:**
- Consumes: `LoadoutPreference` (phase 2b), `TeamSide`.
- Produces:
  - `sealed record PreferenceKey(ulong SteamId, TeamSide Team, string RoundType)` avec `const string AnyRoundType = "*"`.
  - `sealed record StoredPreference(PreferenceKey Key, LoadoutPreference Preference)`
  - `sealed record PreferenceBook` (immuable) avec :
    - `static PreferenceBook Empty`
    - `PreferenceBook StartSession(ulong steamId)` : ouvre une session de chargement pour ce joueur (remplace une éventuelle session précédente) et renvoie `(PreferenceBook, long SessionToken)` via `(PreferenceBook Book, long Token) StartSession(ulong steamId)`.
    - `PreferenceBook WithPlayer(ulong steamId, long sessionToken, IEnumerable<StoredPreference> stored)` : ignoré si la session n'est plus active (joueur parti ou reconnecté entre-temps).
    - `PreferenceBook WithoutPlayer(ulong steamId)` : retire préférences et session.
    - `PreferenceBook With(StoredPreference preference)`
    - `LoadoutPreference? RequestFor(ulong steamId, TeamSide team, string roundType)` : préférence du type de round (armes) fusionnée avec le volontariat AWP de la clé `*` ; `null` si rien n'existe.
    - `bool IsAwpVolunteer(ulong steamId)` (vrai si l'une des deux clés `*` a `AwpOptIn`).
    - `(PreferenceBook Book, IReadOnlyList<StoredPreference> Changes, bool OptIn) ToggleAwp(ulong steamId)` : inverse le volontariat et l'écrit pour T **et** CT sous `*`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Preferences/PreferenceBookTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Domain.Tests.Preferences;

public class PreferenceBookTests
{
    private const ulong Alice = 76561198000000001UL;
    private const ulong Bob = 76561198000000002UL;

    private static StoredPreference Weapons(ulong steamId, TeamSide team, string roundType, string? primary, string? secondary = null) =>
        new(new PreferenceKey(steamId, team, roundType), new LoadoutPreference(primary, secondary, false));

    private static PreferenceBook Loaded(ulong steamId, params StoredPreference[] stored)
    {
        var (book, token) = PreferenceBook.Empty.StartSession(steamId);
        return book.WithPlayer(steamId, token, stored);
    }

    [Fact]
    public void RequestFor_ReturnsTheRoundTypePreference()
    {
        var book = Loaded(Alice, Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1_silencer"));
        Assert.Equal("weapon_m4a1_silencer", book.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
        Assert.Null(book.RequestFor(Alice, TeamSide.T, "FullBuy"));
        Assert.Null(book.RequestFor(Bob, TeamSide.CT, "FullBuy"));
    }

    [Fact]
    public void RequestFor_MergesAwpVolunteering()
    {
        var (book, _, optIn) = Loaded(Alice, Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1")).ToggleAwp(Alice);
        Assert.True(optIn);
        var request = book.RequestFor(Alice, TeamSide.CT, "FullBuy");
        Assert.Equal("weapon_m4a1", request?.Primary);
        Assert.True(request?.AwpOptIn);
        Assert.True(book.RequestFor(Alice, TeamSide.T, "Mid")?.AwpOptIn);
    }

    [Fact]
    public void ToggleAwp_WritesBothTeams_AndTogglesBack()
    {
        var (book, changes, optIn) = PreferenceBook.Empty.ToggleAwp(Alice);
        Assert.True(optIn);
        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal(PreferenceKey.AnyRoundType, c.Key.RoundType));
        Assert.True(book.IsAwpVolunteer(Alice));
        var (after, _, secondOptIn) = book.ToggleAwp(Alice);
        Assert.False(secondOptIn);
        Assert.False(after.IsAwpVolunteer(Alice));
    }

    [Fact]
    public void WithoutPlayer_ForgetsEverything()
    {
        var book = Loaded(Alice, Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1")).WithoutPlayer(Alice);
        Assert.Null(book.RequestFor(Alice, TeamSide.CT, "FullBuy"));
    }

    [Fact]
    public void WithPlayer_ForUnknownSession_IsIgnored()
    {
        var (book, token) = PreferenceBook.Empty.StartSession(Alice);
        var afterLeave = book.WithoutPlayer(Alice).WithPlayer(Alice, token, new[] { Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1") });
        Assert.Null(afterLeave.RequestFor(Alice, TeamSide.CT, "FullBuy"));
    }

    [Fact]
    public void WithPlayer_FromAnOlderSession_IsIgnored()
    {
        var (first, oldToken) = PreferenceBook.Empty.StartSession(Alice);
        var (second, _) = first.StartSession(Alice);
        var result = second.WithPlayer(Alice, oldToken, new[] { Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1") });
        Assert.Null(result.RequestFor(Alice, TeamSide.CT, "FullBuy"));
    }

    [Fact]
    public void WithPlayer_KeepsChangesMadeWhileLoading()
    {
        var (book, token) = PreferenceBook.Empty.StartSession(Alice);
        var (toggled, _, _) = book.ToggleAwp(Alice);
        var loaded = toggled.WithPlayer(Alice, token, new[] { Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1") });
        Assert.True(loaded.IsAwpVolunteer(Alice));
        Assert.Equal("weapon_m4a1", loaded.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
    }

    [Fact]
    public void With_ReplacesASingleEntry()
    {
        var book = PreferenceBook.Empty
            .With(Weapons(Alice, TeamSide.T, "Mid", "weapon_mac10"))
            .With(Weapons(Alice, TeamSide.T, "Mid", "weapon_galilar"));
        Assert.Equal("weapon_galilar", book.RequestFor(Alice, TeamSide.T, "Mid")?.Primary);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Domain.Preferences` introuvable).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Preferences/PreferenceBook.cs`
```csharp
using System.Collections.Immutable;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Preferences;

public sealed record PreferenceKey(ulong SteamId, TeamSide Team, string RoundType)
{
    public const string AnyRoundType = "*";
}

public sealed record StoredPreference(PreferenceKey Key, LoadoutPreference Preference);

public sealed record PreferenceBook(
    ImmutableDictionary<PreferenceKey, LoadoutPreference> Entries,
    ImmutableDictionary<ulong, long> Sessions,
    long NextToken)
{
    private static readonly TeamSide[] BothSides = { TeamSide.T, TeamSide.CT };
    private static readonly LoadoutPreference Nothing = new(null, null, false);

    public static PreferenceBook Empty { get; } =
        new(ImmutableDictionary<PreferenceKey, LoadoutPreference>.Empty, ImmutableDictionary<ulong, long>.Empty, 1);

    public (PreferenceBook Book, long Token) StartSession(ulong steamId) =>
        (this with { Sessions = Sessions.SetItem(steamId, NextToken), NextToken = NextToken + 1 }, NextToken);

    public PreferenceBook WithPlayer(ulong steamId, long sessionToken, IEnumerable<StoredPreference> stored)
    {
        if (Sessions.GetValueOrDefault(steamId) != sessionToken)
        {
            return this;
        }
        var fresh = stored.Where(s => s.Key.SteamId == steamId && !Entries.ContainsKey(s.Key));
        return this with { Entries = Entries.SetItems(fresh.Select(s => KeyValuePair.Create(s.Key, s.Preference))) };
    }

    public PreferenceBook WithoutPlayer(ulong steamId) => this with
    {
        Entries = Entries.RemoveRange(Entries.Keys.Where(k => k.SteamId == steamId)),
        Sessions = Sessions.Remove(steamId),
    };

    public PreferenceBook With(StoredPreference preference) =>
        this with { Entries = Entries.SetItem(preference.Key, preference.Preference) };

    public LoadoutPreference? RequestFor(ulong steamId, TeamSide team, string roundType)
    {
        var weapons = Entries.GetValueOrDefault(new PreferenceKey(steamId, team, roundType));
        var awp = Entries.GetValueOrDefault(new PreferenceKey(steamId, team, PreferenceKey.AnyRoundType));
        if (weapons is null && awp is null)
        {
            return null;
        }
        return (weapons ?? Nothing) with { AwpOptIn = awp?.AwpOptIn ?? false };
    }

    public bool IsAwpVolunteer(ulong steamId) =>
        BothSides.Any(side => Entries.GetValueOrDefault(new PreferenceKey(steamId, side, PreferenceKey.AnyRoundType))?.AwpOptIn == true);

    public (PreferenceBook Book, IReadOnlyList<StoredPreference> Changes, bool OptIn) ToggleAwp(ulong steamId)
    {
        var optIn = !IsAwpVolunteer(steamId);
        var changes = BothSides
            .Select(side => new PreferenceKey(steamId, side, PreferenceKey.AnyRoundType))
            .Select(key => new StoredPreference(key, (Entries.GetValueOrDefault(key) ?? Nothing) with { AwpOptIn = optIn }))
            .ToList();
        return (changes.Aggregate(this, (book, change) => book.With(change)), changes, optIn);
    }
}
```

`WithPlayer` n'écrase pas une clé déjà présente : une modification faite pendant le chargement (ex. `!awp`) est plus récente que la base.

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Preferences tests/RetakeV4.Domain.Tests/Preferences
git commit -m "feat: carnet de préférences joueurs avec sessions de chargement (Domain)"
```

---

### Task 2: Conversion des préférences V3 (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Preferences/V3PreferenceImport.cs`
- Test: `tests/RetakeV4.Domain.Tests/Preferences/V3PreferenceImportTests.cs`

**Interfaces:**
- Consumes: `PreferenceKey`, `StoredPreference` (Task 1).
- Produces:
  - `sealed record V3PreferenceRow(string Table, ulong UserId, int Team, string? WeaponString, int? AwpChance)`
  - `static class V3PreferenceImport { IReadOnlyList<StoredPreference> Convert(IEnumerable<V3PreferenceRow> rows); }`
  - Correspondance : `FullBuyPrimary` → FullBuy.Primary ; `FullBuySecondary` → FullBuy.Secondary ; `MidPrimary` → Mid.Primary ; `MidSecondary` → Mid.Secondary ; `Pistol` → Pistol.Secondary ; `FullBuyAWPChance` → `*`.AwpOptIn (`> 0`). Team 2 → T, 3 → CT, autre → ignoré ; table inconnue ignorée ; arme vide/blanche → `null`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Preferences/V3PreferenceImportTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Domain.Tests.Preferences;

public class V3PreferenceImportTests
{
    private const ulong Alice = 76561198000000001UL;

    [Fact]
    public void WeaponTables_AreMergedPerPlayerTeamAndRoundType()
    {
        var rows = new[]
        {
            new V3PreferenceRow("FullBuyPrimary", Alice, 3, "weapon_m4a1_silencer", null),
            new V3PreferenceRow("FullBuySecondary", Alice, 3, "weapon_usp_silencer", null),
            new V3PreferenceRow("MidPrimary", Alice, 2, "weapon_galilar", null),
            new V3PreferenceRow("Pistol", Alice, 2, "weapon_tec9", null),
        };
        var imported = V3PreferenceImport.Convert(rows);
        var fullBuy = Assert.Single(imported, p => p.Key == new PreferenceKey(Alice, TeamSide.CT, "FullBuy"));
        Assert.Equal("weapon_m4a1_silencer", fullBuy.Preference.Primary);
        Assert.Equal("weapon_usp_silencer", fullBuy.Preference.Secondary);
        Assert.Equal("weapon_galilar", imported.Single(p => p.Key.RoundType == "Mid").Preference.Primary);
        var pistol = imported.Single(p => p.Key.RoundType == "Pistol");
        Assert.Null(pistol.Preference.Primary);
        Assert.Equal("weapon_tec9", pistol.Preference.Secondary);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(0, false)]
    public void AwpChance_BecomesAwpVolunteering(int chance, bool expected)
    {
        var imported = V3PreferenceImport.Convert(new[] { new V3PreferenceRow("FullBuyAWPChance", Alice, 2, null, chance) });
        var awp = Assert.Single(imported);
        Assert.Equal(new PreferenceKey(Alice, TeamSide.T, PreferenceKey.AnyRoundType), awp.Key);
        Assert.Equal(expected, awp.Preference.AwpOptIn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void RowsWithoutAPlayableTeam_AreIgnored(int team) =>
        Assert.Empty(V3PreferenceImport.Convert(new[] { new V3PreferenceRow("MidPrimary", Alice, team, "weapon_mp9", null) }));

    [Fact]
    public void UnknownTables_AreIgnored() =>
        Assert.Empty(V3PreferenceImport.Convert(new[] { new V3PreferenceRow("Something", Alice, 2, "weapon_mp9", null) }));

    [Fact]
    public void BlankWeapons_BecomeNull()
    {
        var imported = V3PreferenceImport.Convert(new[] { new V3PreferenceRow("MidPrimary", Alice, 2, "  ", null) });
        Assert.Null(Assert.Single(imported).Preference.Primary);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`V3PreferenceRow`, `V3PreferenceImport` introuvables).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Preferences/V3PreferenceImport.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Preferences;

public sealed record V3PreferenceRow(string Table, ulong UserId, int Team, string? WeaponString, int? AwpChance);

public static class V3PreferenceImport
{
    private enum Field
    {
        Primary,
        Secondary,
        Awp,
    }

    private static readonly IReadOnlyDictionary<string, (string RoundType, Field Field)> Targets =
        new Dictionary<string, (string, Field)>(StringComparer.Ordinal)
        {
            ["FullBuyPrimary"] = ("FullBuy", Field.Primary),
            ["FullBuySecondary"] = ("FullBuy", Field.Secondary),
            ["MidPrimary"] = ("Mid", Field.Primary),
            ["MidSecondary"] = ("Mid", Field.Secondary),
            ["Pistol"] = ("Pistol", Field.Secondary),
            ["FullBuyAWPChance"] = (PreferenceKey.AnyRoundType, Field.Awp),
        };

    public static IReadOnlyList<StoredPreference> Convert(IEnumerable<V3PreferenceRow> rows)
    {
        var merged = new Dictionary<PreferenceKey, LoadoutPreference>();
        foreach (var row in rows)
        {
            TeamSide? team = row.Team switch { 2 => TeamSide.T, 3 => TeamSide.CT, _ => null };
            if (team is null || !Targets.TryGetValue(row.Table, out var target))
            {
                continue;
            }
            var key = new PreferenceKey(row.UserId, team.Value, target.RoundType);
            var current = merged.GetValueOrDefault(key) ?? new LoadoutPreference(null, null, false);
            merged[key] = Apply(current, target.Field, row);
        }
        return merged.Select(entry => new StoredPreference(entry.Key, entry.Value)).ToList();
    }

    private static LoadoutPreference Apply(LoadoutPreference current, Field field, V3PreferenceRow row) => field switch
    {
        Field.Primary => current with { Primary = Weapon(row.WeaponString) },
        Field.Secondary => current with { Secondary = Weapon(row.WeaponString) },
        _ => current with { AwpOptIn = row.AwpChance > 0 },
    };

    private static string? Weapon(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Preferences/V3PreferenceImport.cs tests/RetakeV4.Domain.Tests/Preferences/V3PreferenceImportTests.cs
git commit -m "feat: conversion des préférences d'un serveur V3 (Domain)"
```

---

### Task 3: Contrat de dépôt, magasin résilient et file d'écriture

**Files:**
- Create: `src/RetakeV4/Persistence/IPreferenceRepository.cs`, `NoOpPreferenceRepository.cs`, `ResilientPreferenceStore.cs`, `PreferenceWriteQueue.cs`
- Test: `tests/RetakeV4.Integration.Tests/Persistence/ResilientPreferenceStoreTests.cs`, `tests/RetakeV4.Integration.Tests/Persistence/PreferenceWriteQueueTests.cs`, `tests/RetakeV4.Integration.Tests/Persistence/FakePreferenceRepository.cs`

**Interfaces:**
- Consumes: `StoredPreference` (Task 1).
- Produces:
  - `interface IPreferenceRepository { Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct); Task UpsertAsync(StoredPreference preference, CancellationToken ct); Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct); }`
  - `sealed class NoOpPreferenceRepository : IPreferenceRepository` (ne stocke rien, renvoie une liste vide).
  - `sealed class ResilientPreferenceStore(IPreferenceRepository inner, ILogger logger, Func<DateTimeOffset> clock) : IPreferenceRepository` — sur exception : log d'avertissement, entrée en panne jusqu'à `now + délai` (5 s, doublé à chaque échec consécutif, plafond 300 s) ; pendant la panne, `LoadAsync` renvoie une liste vide et `UpsertAsync` n'appelle pas le dépôt ; un succès remet le délai à 5 s ; `ImportAsync` **propage** l'exception (commande admin : l'échec doit être visible) ; `bool IsAvailable`.
  - `sealed class PreferenceWriteQueue(IPreferenceRepository store, ILogger logger) : IAsyncDisposable` avec `void Enqueue(StoredPreference preference)` — un seul lecteur de fond, ordre conservé, une exception d'écriture est journalisée sans arrêter la boucle ; `DisposeAsync` vide la file (délai max 5 s).

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/Persistence/FakePreferenceRepository.cs`
```csharp
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public sealed class FakePreferenceRepository : IPreferenceRepository
{
    public bool Fail { get; set; }

    public int LoadCalls { get; private set; }

    public List<StoredPreference> Upserts { get; } = new();

    public List<StoredPreference> Stored { get; } = new();

    public Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        LoadCalls++;
        ThrowIfFailing();
        return Task.FromResult<IReadOnlyList<StoredPreference>>(Stored.Where(s => s.Key.SteamId == steamId).ToList());
    }

    public Task UpsertAsync(StoredPreference preference, CancellationToken ct)
    {
        ThrowIfFailing();
        Upserts.Add(preference);
        return Task.CompletedTask;
    }

    public Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct)
    {
        ThrowIfFailing();
        Stored.AddRange(preferences);
        return Task.CompletedTask;
    }

    private void ThrowIfFailing()
    {
        if (Fail)
        {
            throw new InvalidOperationException("database down");
        }
    }
}
```

`tests/RetakeV4.Integration.Tests/Persistence/ResilientPreferenceStoreTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public class ResilientPreferenceStoreTests
{
    private readonly FakePreferenceRepository _inner = new();
    private readonly ListLogger _logger = new();
    private DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private ResilientPreferenceStore Store() => new(_inner, _logger, () => _now);

    private static StoredPreference Preference(ulong steamId) =>
        new(new PreferenceKey(steamId, TeamSide.CT, "FullBuy"), new LoadoutPreference("weapon_m4a1", null, false));

    [Fact]
    public async Task HealthyRepository_IsUsedDirectly()
    {
        _inner.Stored.Add(Preference(1));
        var store = Store();
        Assert.Single(await store.LoadAsync(1, CancellationToken.None));
        Assert.True(store.IsAvailable);
    }

    [Fact]
    public async Task Failure_ReturnsEmpty_LogsOnce_AndSkipsCallsDuringBackoff()
    {
        _inner.Fail = true;
        var store = Store();
        Assert.Empty(await store.LoadAsync(1, CancellationToken.None));
        Assert.Empty(await store.LoadAsync(1, CancellationToken.None));
        await store.UpsertAsync(Preference(1), CancellationToken.None);
        Assert.Equal(1, _inner.LoadCalls);
        Assert.Single(_logger.Entries);
        Assert.False(store.IsAvailable);
    }

    [Fact]
    public async Task AfterBackoff_RepositoryIsRetried_AndSuccessRecovers()
    {
        _inner.Fail = true;
        var store = Store();
        await store.LoadAsync(1, CancellationToken.None);
        _inner.Fail = false;
        _now = _now.AddSeconds(6);
        await store.LoadAsync(1, CancellationToken.None);
        Assert.Equal(2, _inner.LoadCalls);
        Assert.True(store.IsAvailable);
    }

    [Fact]
    public async Task ConsecutiveFailures_DoubleTheBackoff()
    {
        _inner.Fail = true;
        var store = Store();
        await store.LoadAsync(1, CancellationToken.None);
        _now = _now.AddSeconds(6);
        await store.LoadAsync(1, CancellationToken.None);
        _now = _now.AddSeconds(6);
        await store.LoadAsync(1, CancellationToken.None);
        Assert.Equal(2, _inner.LoadCalls);
        _now = _now.AddSeconds(5);
        await store.LoadAsync(1, CancellationToken.None);
        Assert.Equal(3, _inner.LoadCalls);
    }

    [Fact]
    public async Task Import_PropagatesFailures()
    {
        _inner.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().ImportAsync(new[] { Preference(1) }, CancellationToken.None));
    }

    [Fact]
    public async Task NoOpRepository_StoresNothing()
    {
        var repository = new NoOpPreferenceRepository();
        await repository.UpsertAsync(Preference(1), CancellationToken.None);
        await repository.ImportAsync(new[] { Preference(1) }, CancellationToken.None);
        Assert.Empty(await repository.LoadAsync(1, CancellationToken.None));
    }
}
```

`tests/RetakeV4.Integration.Tests/Persistence/PreferenceWriteQueueTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public class PreferenceWriteQueueTests
{
    private static StoredPreference Preference(ulong steamId, string weapon) =>
        new(new PreferenceKey(steamId, TeamSide.T, "Mid"), new LoadoutPreference(weapon, null, false));

    [Fact]
    public async Task EnqueuedWrites_ReachTheStoreInOrder()
    {
        var repository = new FakePreferenceRepository();
        var queue = new PreferenceWriteQueue(repository, new ListLogger());
        queue.Enqueue(Preference(1, "weapon_mac10"));
        queue.Enqueue(Preference(1, "weapon_galilar"));
        await queue.DisposeAsync();
        Assert.Equal(new[] { "weapon_mac10", "weapon_galilar" }, repository.Upserts.Select(u => u.Preference.Primary));
    }

    [Fact]
    public async Task FailingWrite_IsLogged_AndDoesNotStopTheQueue()
    {
        var repository = new FakePreferenceRepository { Fail = true };
        var logger = new ListLogger();
        var queue = new PreferenceWriteQueue(repository, logger);
        queue.Enqueue(Preference(1, "weapon_mac10"));
        await Task.Delay(200);
        repository.Fail = false;
        queue.Enqueue(Preference(2, "weapon_galilar"));
        await queue.DisposeAsync();
        Assert.Equal("weapon_galilar", Assert.Single(repository.Upserts).Preference.Primary);
        Assert.NotEmpty(logger.Entries);
    }
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Persistence` introuvable).

- [ ] **Step 2: Implémentation**

`src/RetakeV4/Persistence/IPreferenceRepository.cs`
```csharp
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public interface IPreferenceRepository
{
    Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct);

    Task UpsertAsync(StoredPreference preference, CancellationToken ct);

    Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct);
}
```

`src/RetakeV4/Persistence/NoOpPreferenceRepository.cs`
```csharp
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class NoOpPreferenceRepository : IPreferenceRepository
{
    public Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredPreference>>(Array.Empty<StoredPreference>());

    public Task UpsertAsync(StoredPreference preference, CancellationToken ct) => Task.CompletedTask;

    public Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct) => Task.CompletedTask;
}
```

`src/RetakeV4/Persistence/ResilientPreferenceStore.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class ResilientPreferenceStore : IPreferenceRepository
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly IPreferenceRepository _inner;
    private readonly ILogger _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private TimeSpan _backoff = InitialBackoff;

    public ResilientPreferenceStore(IPreferenceRepository inner, ILogger logger, Func<DateTimeOffset> clock)
    {
        _inner = inner;
        _logger = logger;
        _clock = clock;
    }

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _clock() >= _retryAt;
            }
        }
    }

    public async Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return Array.Empty<StoredPreference>();
        }
        try
        {
            var result = await _inner.LoadAsync(steamId, ct).ConfigureAwait(false);
            MarkHealthy();
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MarkFailed(ex, "load");
            return Array.Empty<StoredPreference>();
        }
    }

    public async Task UpsertAsync(StoredPreference preference, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return;
        }
        try
        {
            await _inner.UpsertAsync(preference, ct).ConfigureAwait(false);
            MarkHealthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MarkFailed(ex, "save");
        }
    }

    public Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct) =>
        _inner.ImportAsync(preferences, ct);

    private void MarkHealthy()
    {
        lock (_gate)
        {
            _backoff = InitialBackoff;
        }
    }

    private void MarkFailed(Exception ex, string operation)
    {
        TimeSpan wait;
        lock (_gate)
        {
            wait = _backoff;
            _retryAt = _clock() + wait;
            _backoff = TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, MaxBackoff.Ticks));
        }
        _logger.LogWarning(ex, "Preference database unavailable ({Operation}); using defaults, retrying in {Seconds}s", operation, wait.TotalSeconds);
    }
}
```

`src/RetakeV4/Persistence/PreferenceWriteQueue.cs`
```csharp
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class PreferenceWriteQueue : IAsyncDisposable
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<StoredPreference> _channel = Channel.CreateUnbounded<StoredPreference>(new UnboundedChannelOptions { SingleReader = true });
    private readonly IPreferenceRepository _store;
    private readonly ILogger _logger;
    private readonly Task _worker;

    public PreferenceWriteQueue(IPreferenceRepository store, ILogger logger)
    {
        _store = store;
        _logger = logger;
        _worker = Task.Run(RunAsync);
    }

    public void Enqueue(StoredPreference preference) => _channel.Writer.TryWrite(preference);

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await Task.WhenAny(_worker, Task.Delay(DrainTimeout)).ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        await foreach (var preference in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await _store.UpsertAsync(preference, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save a preference for {SteamId}", preference.Key.SteamId);
            }
        }
    }
}
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Persistence tests/RetakeV4.Integration.Tests/Persistence
git commit -m "feat: contrat de dépôt de préférences, magasin résilient et file d'écriture"
```

---

### Task 4: Dépôt SQLite, migrations et lecteur de base V3

**Files:**
- Modify: `src/RetakeV4/RetakeV4.csproj` (paquets SQLite + MySQL, `CopyLocalLockFileAssemblies`)
- Create: `src/RetakeV4/Persistence/SqlMigrator.cs`, `SqlitePreferenceRepository.cs`, `V3SqliteReader.cs`
- Test: `tests/RetakeV4.Integration.Tests/Persistence/SqlitePreferenceRepositoryTests.cs`, `tests/RetakeV4.Integration.Tests/Persistence/V3SqliteReaderTests.cs`

**Interfaces:**
- Consumes: `IPreferenceRepository`, `StoredPreference`, `PreferenceKey`, `V3PreferenceRow` (Tasks 1-3).
- Produces:
  - `static class SqlMigrator { Task<int> ApplyAsync(DbConnection connection, IReadOnlyList<string> migrations, CancellationToken ct); }` — crée `schema_version(version INTEGER NOT NULL)` si besoin, applique dans une transaction chaque migration d'index ≥ version courante, renvoie la version finale.
  - `sealed class SqlitePreferenceRepository(string databaseFile) : IPreferenceRepository` avec `Task InitializeAsync(CancellationToken ct)` (crée le dossier et applique les migrations). Table `player_loadout(steam_id INTEGER, team INTEGER, round_type TEXT, primary_weapon TEXT NULL, secondary_weapon TEXT NULL, awp_opt_in INTEGER, updated_at TEXT, PRIMARY KEY(steam_id, team, round_type))`. SteamID stocké en `long` via `unchecked`.
  - `static class V3SqliteReader { Task<IReadOnlyList<V3PreferenceRow>> ReadAsync(string databaseFile, CancellationToken ct); }` — lecture seule, tables absentes ignorées, lève `FileNotFoundException` si le fichier n'existe pas.

- [ ] **Step 1: Paquets**

Dans `src/RetakeV4/RetakeV4.csproj`, ajouter `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` au `PropertyGroup` et à l'`ItemGroup` des paquets :
```xml
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.9" />
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.3" />
    <PackageReference Include="MySqlConnector" Version="2.3.6" />
```

- [ ] **Step 2: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/Persistence/SqlitePreferenceRepositoryTests.cs`
```csharp
using Microsoft.Data.Sqlite;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public sealed class SqlitePreferenceRepositoryTests : IDisposable
{
    private const ulong Alice = 76561198000000001UL;
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private async Task<SqlitePreferenceRepository> Repository()
    {
        var repository = new SqlitePreferenceRepository(Path.Combine(_dir.Path, "data", "retakev4.db"));
        await repository.InitializeAsync(CancellationToken.None);
        return repository;
    }

    private static StoredPreference Preference(ulong steamId, TeamSide team, string roundType, string? primary, bool awp = false) =>
        new(new PreferenceKey(steamId, team, roundType), new LoadoutPreference(primary, "weapon_deagle", awp));

    [Fact]
    public async Task Upsert_ThenLoad_RoundTrips()
    {
        var repository = await Repository();
        await repository.UpsertAsync(Preference(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1_silencer"), CancellationToken.None);
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, PreferenceKey.AnyRoundType, null, awp: true), CancellationToken.None);
        var loaded = await repository.LoadAsync(Alice, CancellationToken.None);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(Preference(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1_silencer"), loaded);
        Assert.Contains(Preference(Alice, TeamSide.T, PreferenceKey.AnyRoundType, null, awp: true), loaded);
    }

    [Fact]
    public async Task Upsert_ReplacesTheSameKey()
    {
        var repository = await Repository();
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, "Mid", "weapon_mac10"), CancellationToken.None);
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, "Mid", "weapon_galilar"), CancellationToken.None);
        Assert.Equal("weapon_galilar", Assert.Single(await repository.LoadAsync(Alice, CancellationToken.None)).Preference.Primary);
    }

    [Fact]
    public async Task Load_OnlyReturnsTheRequestedPlayer()
    {
        var repository = await Repository();
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, "Mid", "weapon_mac10"), CancellationToken.None);
        Assert.Empty(await repository.LoadAsync(Alice + 1, CancellationToken.None));
    }

    [Fact]
    public async Task Import_StoresEverything_AndOverwrites()
    {
        var repository = await Repository();
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, "Mid", "weapon_mac10"), CancellationToken.None);
        await repository.ImportAsync(new[] { Preference(Alice, TeamSide.T, "Mid", "weapon_sg556"), Preference(Alice, TeamSide.CT, "Mid", "weapon_mp9") }, CancellationToken.None);
        var loaded = await repository.LoadAsync(Alice, CancellationToken.None);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, p => p.Preference.Primary == "weapon_sg556");
    }

    [Fact]
    public async Task Initialize_IsIdempotent()
    {
        await Repository();
        var again = await Repository();
        Assert.Empty(await again.LoadAsync(Alice, CancellationToken.None));
    }

    [Fact]
    public async Task LargeSteamId_RoundTrips()
    {
        var repository = await Repository();
        const ulong huge = ulong.MaxValue - 1;
        await repository.UpsertAsync(Preference(huge, TeamSide.CT, "Pistol", null), CancellationToken.None);
        Assert.Equal(huge, Assert.Single(await repository.LoadAsync(huge, CancellationToken.None)).Key.SteamId);
    }
}
```

`tests/RetakeV4.Integration.Tests/Persistence/V3SqliteReaderTests.cs`
```csharp
using Microsoft.Data.Sqlite;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public sealed class V3SqliteReaderTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private string CreateV3Database(bool withAwpTable)
    {
        var file = _dir.File("cs2retake.db");
        using var connection = new SqliteConnection($"Data Source={file}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE FullBuyPrimary (UserId INTEGER, WeaponString TEXT, Team INT);
            CREATE TABLE Pistol (UserId INTEGER, WeaponString TEXT, Team INT);
            INSERT INTO FullBuyPrimary VALUES (76561198000000001, 'weapon_m4a1_silencer', 3);
            INSERT INTO FullBuyPrimary VALUES (76561198000000001, 'weapon_ak47', 0);
            INSERT INTO Pistol VALUES (76561198000000002, 'weapon_tec9', 2);
            """;
        if (withAwpTable)
        {
            command.CommandText += "CREATE TABLE FullBuyAWPChance (UserId INTEGER, AWPChance INT, Team INT); INSERT INTO FullBuyAWPChance VALUES (76561198000000001, 30, 3);";
        }
        command.ExecuteNonQuery();
        return file;
    }

    [Fact]
    public async Task ReadsExistingTables_AndSkipsMissingOnes()
    {
        var rows = await V3SqliteReader.ReadAsync(CreateV3Database(withAwpTable: false), CancellationToken.None);
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r is { Table: "FullBuyPrimary", UserId: 76561198000000001UL, Team: 3, WeaponString: "weapon_m4a1_silencer" });
        Assert.Contains(rows, r => r is { Table: "Pistol", WeaponString: "weapon_tec9" });
    }

    [Fact]
    public async Task ReadsAwpChances()
    {
        var rows = await V3SqliteReader.ReadAsync(CreateV3Database(withAwpTable: true), CancellationToken.None);
        Assert.Contains(rows, r => r is { Table: "FullBuyAWPChance", AwpChance: 30, Team: 3 });
    }

    [Fact]
    public async Task MissingFile_Throws() =>
        await Assert.ThrowsAsync<FileNotFoundException>(() => V3SqliteReader.ReadAsync(_dir.File("nope.db"), CancellationToken.None));

    [Fact]
    public async Task NotADatabase_Throws()
    {
        var file = _dir.File("garbage.db");
        await File.WriteAllTextAsync(file, "this is not sqlite");
        await Assert.ThrowsAsync<SqliteException>(() => V3SqliteReader.ReadAsync(file, CancellationToken.None));
    }
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`SqlitePreferenceRepository`, `V3SqliteReader` introuvables).

- [ ] **Step 3: Implémentation**

`src/RetakeV4/Persistence/SqlMigrator.cs`
```csharp
using System.Data.Common;

namespace RetakeV4.Persistence;

public static class SqlMigrator
{
    public static async Task<int> ApplyAsync(DbConnection connection, IReadOnlyList<string> migrations, CancellationToken ct)
    {
        await ExecuteAsync(connection, null, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)", ct).ConfigureAwait(false);
        var current = await CurrentVersionAsync(connection, ct).ConfigureAwait(false);
        for (var index = current; index < migrations.Count; index++)
        {
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, migrations[index], ct).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "DELETE FROM schema_version", ct).ConfigureAwait(false);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO schema_version (version) VALUES (@version)";
            var parameter = insert.CreateParameter();
            parameter.ParameterName = "@version";
            parameter.Value = index + 1;
            insert.Parameters.Add(parameter);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        return migrations.Count;
    }

    private static async Task<int> CurrentVersionAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(value);
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
```

`src/RetakeV4/Persistence/SqlitePreferenceRepository.cs`
```csharp
using Microsoft.Data.Sqlite;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class SqlitePreferenceRepository : IPreferenceRepository
{
    private static readonly string[] Migrations =
    {
        """
        CREATE TABLE IF NOT EXISTS player_loadout (
            steam_id INTEGER NOT NULL,
            team INTEGER NOT NULL,
            round_type TEXT NOT NULL,
            primary_weapon TEXT NULL,
            secondary_weapon TEXT NULL,
            awp_opt_in INTEGER NOT NULL DEFAULT 0,
            updated_at TEXT NOT NULL,
            PRIMARY KEY (steam_id, team, round_type))
        """,
    };

    private const string UpsertSql = """
        INSERT INTO player_loadout (steam_id, team, round_type, primary_weapon, secondary_weapon, awp_opt_in, updated_at)
        VALUES (@steam_id, @team, @round_type, @primary, @secondary, @awp, @updated_at)
        ON CONFLICT (steam_id, team, round_type) DO UPDATE SET
            primary_weapon = excluded.primary_weapon,
            secondary_weapon = excluded.secondary_weapon,
            awp_opt_in = excluded.awp_opt_in,
            updated_at = excluded.updated_at
        """;

    private readonly string _databaseFile;
    private readonly string _connectionString;

    public SqlitePreferenceRepository(string databaseFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        _databaseFile = databaseFile;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databaseFile }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_databaseFile))!);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await SqlMigrator.ApplyAsync(connection, Migrations, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
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

    public async Task UpsertAsync(StoredPreference preference, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = CreateUpsert(connection, preference);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var preference in preferences)
        {
            await using var command = CreateUpsert(connection, preference);
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static SqliteCommand CreateUpsert(SqliteConnection connection, StoredPreference preference)
    {
        var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        command.Parameters.AddWithValue("@steam_id", unchecked((long)preference.Key.SteamId));
        command.Parameters.AddWithValue("@team", (int)preference.Key.Team);
        command.Parameters.AddWithValue("@round_type", preference.Key.RoundType);
        command.Parameters.AddWithValue("@primary", (object?)preference.Preference.Primary ?? DBNull.Value);
        command.Parameters.AddWithValue("@secondary", (object?)preference.Preference.Secondary ?? DBNull.Value);
        command.Parameters.AddWithValue("@awp", preference.Preference.AwpOptIn ? 1 : 0);
        command.Parameters.AddWithValue("@updated_at", DateTimeOffset.UtcNow.ToString("O"));
        return command;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
```

`src/RetakeV4/Persistence/V3SqliteReader.cs`
```csharp
using Microsoft.Data.Sqlite;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public static class V3SqliteReader
{
    private static readonly string[] WeaponTables = { "FullBuyPrimary", "FullBuySecondary", "MidPrimary", "MidSecondary", "Pistol" };
    private const string AwpTable = "FullBuyAWPChance";

    public static async Task<IReadOnlyList<V3PreferenceRow>> ReadAsync(string databaseFile, CancellationToken ct)
    {
        if (!File.Exists(databaseFile))
        {
            throw new FileNotFoundException("V3 database not found", databaseFile);
        }
        var builder = new SqliteConnectionStringBuilder { DataSource = databaseFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(ct).ConfigureAwait(false);
        var existing = await ExistingTablesAsync(connection, ct).ConfigureAwait(false);
        var rows = new List<V3PreferenceRow>();
        foreach (var table in WeaponTables.Where(existing.Contains))
        {
            rows.AddRange(await ReadTableAsync(connection, table, "WeaponString", ct).ConfigureAwait(false));
        }
        if (existing.Contains(AwpTable))
        {
            rows.AddRange(await ReadTableAsync(connection, AwpTable, "AWPChance", ct).ConfigureAwait(false));
        }
        return rows;
    }

    private static async Task<HashSet<string>> ExistingTablesAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    // Table and column names come from the fixed lists above, never from user input.
    private static async Task<List<V3PreferenceRow>> ReadTableAsync(SqliteConnection connection, string table, string valueColumn, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT UserId, Team, {valueColumn} FROM {table}";
        var rows = new List<V3PreferenceRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var userId = unchecked((ulong)reader.GetInt64(0));
            var team = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            rows.Add(valueColumn == "AWPChance"
                ? new V3PreferenceRow(table, userId, team, null, reader.IsDBNull(2) ? null : reader.GetInt32(2))
                : new V3PreferenceRow(table, userId, team, reader.IsDBNull(2) ? null : reader.GetString(2), null));
        }
        return rows;
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`. Si `SqliteException` n'est pas levée pour `NotADatabase_Throws` à l'ouverture mais à la première requête, le test reste valide (l'exception sort de `ReadAsync`).

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4/RetakeV4.csproj src/RetakeV4/Persistence tests/RetakeV4.Integration.Tests/Persistence
git commit -m "feat: dépôt SQLite des préférences avec migrations et lecteur de base V3"
```

---

### Task 5: Dépôt MySQL

**Files:**
- Create: `src/RetakeV4/Persistence/MySqlPreferenceRepository.cs`
- Test: `tests/RetakeV4.Integration.Tests/Persistence/MySqlPreferenceRepositoryTests.cs`

**Interfaces:**
- Consumes: `IPreferenceRepository`, `SqlMigrator` (Tasks 3-4).
- Produces: `sealed class MySqlPreferenceRepository(string connectionString) : IPreferenceRepository` avec `Task InitializeAsync(CancellationToken ct)` ; table `player_loadout(steam_id BIGINT UNSIGNED, team TINYINT, round_type VARCHAR(64), primary_weapon VARCHAR(64) NULL, secondary_weapon VARCHAR(64) NULL, awp_opt_in TINYINT(1), updated_at DATETIME(6), PRIMARY KEY(steam_id, team, round_type))`, UPSERT `ON DUPLICATE KEY UPDATE`.
- Tests exécutés seulement si la variable d'environnement `RETAKEV4_MYSQL_TEST` contient une chaîne de connexion (sinon le test sort immédiatement).

- [ ] **Step 1: Écrire le test**

`tests/RetakeV4.Integration.Tests/Persistence/MySqlPreferenceRepositoryTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public class MySqlPreferenceRepositoryTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RETAKEV4_MYSQL_TEST");

    [Fact]
    public async Task RoundTrip_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var repository = new MySqlPreferenceRepository(ConnectionString);
        await repository.InitializeAsync(CancellationToken.None);
        var steamId = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        var preference = new StoredPreference(new PreferenceKey(steamId, TeamSide.CT, "FullBuy"), new LoadoutPreference("weapon_m4a1", "weapon_deagle", true));
        await repository.UpsertAsync(preference, CancellationToken.None);
        await repository.UpsertAsync(preference with { Preference = preference.Preference with { Primary = "weapon_aug" } }, CancellationToken.None);
        var loaded = Assert.Single(await repository.LoadAsync(steamId, CancellationToken.None));
        Assert.Equal("weapon_aug", loaded.Preference.Primary);
        Assert.True(loaded.Preference.AwpOptIn);
    }

    [Fact]
    public void Constructor_RejectsBlankConnectionString() =>
        Assert.Throws<ArgumentException>(() => new MySqlPreferenceRepository(" "));
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`MySqlPreferenceRepository` introuvable).

- [ ] **Step 2: Implémentation**

`src/RetakeV4/Persistence/MySqlPreferenceRepository.cs`
```csharp
using MySqlConnector;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class MySqlPreferenceRepository : IPreferenceRepository
{
    private static readonly string[] Migrations =
    {
        """
        CREATE TABLE IF NOT EXISTS player_loadout (
            steam_id BIGINT UNSIGNED NOT NULL,
            team TINYINT NOT NULL,
            round_type VARCHAR(64) NOT NULL,
            primary_weapon VARCHAR(64) NULL,
            secondary_weapon VARCHAR(64) NULL,
            awp_opt_in TINYINT(1) NOT NULL DEFAULT 0,
            updated_at DATETIME(6) NOT NULL,
            PRIMARY KEY (steam_id, team, round_type))
        """,
    };

    private const string UpsertSql = """
        INSERT INTO player_loadout (steam_id, team, round_type, primary_weapon, secondary_weapon, awp_opt_in, updated_at)
        VALUES (@steam_id, @team, @round_type, @primary, @secondary, @awp, @updated_at)
        ON DUPLICATE KEY UPDATE
            primary_weapon = VALUES(primary_weapon),
            secondary_weapon = VALUES(secondary_weapon),
            awp_opt_in = VALUES(awp_opt_in),
            updated_at = VALUES(updated_at)
        """;

    private readonly string _connectionString;

    public MySqlPreferenceRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await SqlMigrator.ApplyAsync(connection, Migrations, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
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

    public async Task UpsertAsync(StoredPreference preference, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = CreateUpsert(connection, preference);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var preference in preferences)
        {
            await using var command = CreateUpsert(connection, preference);
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static MySqlCommand CreateUpsert(MySqlConnection connection, StoredPreference preference)
    {
        var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        command.Parameters.AddWithValue("@steam_id", preference.Key.SteamId);
        command.Parameters.AddWithValue("@team", (int)preference.Key.Team);
        command.Parameters.AddWithValue("@round_type", preference.Key.RoundType);
        command.Parameters.AddWithValue("@primary", (object?)preference.Preference.Primary ?? DBNull.Value);
        command.Parameters.AddWithValue("@secondary", (object?)preference.Preference.Secondary ?? DBNull.Value);
        command.Parameters.AddWithValue("@awp", preference.Preference.AwpOptIn);
        command.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
        return command;
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0` (le test MySQL sort immédiatement sans variable d'environnement).

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Persistence/MySqlPreferenceRepository.cs tests/RetakeV4.Integration.Tests/Persistence/MySqlPreferenceRepositoryTests.cs
git commit -m "feat: dépôt MySQL des préférences"
```

---

### Task 6: Préférences dans le module Allocation (connexion, distribution, `!awp`, import V3)

**Files:**
- Modify: `src/RetakeV4/Modules/Allocation/AllocationConfig.cs`, `src/RetakeV4/Modules/Allocation/AllocationModule.cs`, `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`, `scripts/package-dev.ps1`
- Create: `src/RetakeV4/Modules/Allocation/AllocationConfigValidator.cs`, `src/RetakeV4/Modules/Allocation/PreferenceService.cs`
- Test: `tests/RetakeV4.Integration.Tests/Modules/Allocation/AllocationConfigValidatorTests.cs`, `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs`

**Interfaces:**
- Consumes: `PreferenceBook`, `V3PreferenceImport` (Tasks 1-2) ; `IPreferenceRepository`, `ResilientPreferenceStore`, `PreferenceWriteQueue`, `SqlitePreferenceRepository`, `MySqlPreferenceRepository`, `NoOpPreferenceRepository`, `V3SqliteReader` (Tasks 3-5) ; `LoadoutPlanner` ; `ModuleHooks`.
- Produces:
  - `enum DatabaseType { Sqlite, MySql, None }`, `sealed record DatabaseConfig { DatabaseType Type = Sqlite; string SqliteFile = "data/retakev4.db"; string MySqlConnectionString = ""; }`, `AllocationConfig.Database`.
  - `sealed class AllocationConfigValidator : IConfigValidator<AllocationConfig>` — `SqliteFile` doit être un chemin relatif sans `..` se terminant par `.db` ; MySQL sans chaîne ⇒ SQLite ; section `null` ⇒ défauts.
  - `sealed class PreferenceService(IPreferenceRepository store, ILogger logger) : IAsyncDisposable` (thread de jeu pour tout sauf les tâches de fond) :
    `void PlayerConnected(ulong steamId, Action<Action> onGameThread)`, `void PlayerDisconnected(ulong steamId)`, `LoadoutPreference? RequestFor(ulong steamId, TeamSide team, string roundType)`, `bool ToggleAwp(ulong steamId)`, `Task<int> ImportV3Async(string databaseFile, CancellationToken ct)`, `DisposeAsync`.
  - Commandes : `css_awp` / `awp` (bascule le volontariat), `css_retake_import_v3 <chemin>` (`@retakev4/root`).
  - Clés lang : `allocation.awp.enabled`, `allocation.awp.disabled`, `allocation.import.done` (`{0}` nombre), `allocation.import.failed` (`{0}` raison), `allocation.no_permission`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/Modules/Allocation/AllocationConfigValidatorTests.cs`
```csharp
using RetakeV4.Modules.Allocation;

namespace RetakeV4.Integration.Tests.Modules.Allocation;

public class AllocationConfigValidatorTests
{
    private static readonly AllocationConfig Defaults = new();
    private readonly AllocationConfigValidator _validator = new();

    private AllocationConfig Validate(DatabaseConfig database, out int issues)
    {
        var result = _validator.Validate(Defaults with { Database = database }, Defaults, "allocation.json");
        issues = result.Issues.Count;
        return result.Config;
    }

    [Fact]
    public void Defaults_UseSqliteInTheDataFolder()
    {
        var result = _validator.Validate(Defaults, Defaults, "allocation.json");
        Assert.Empty(result.Issues);
        Assert.Equal(DatabaseType.Sqlite, result.Config.Database.Type);
        Assert.Equal("data/retakev4.db", result.Config.Database.SqliteFile);
    }

    [Theory]
    [InlineData("../../server.db")]
    [InlineData("C:/retake.db")]
    [InlineData("/var/retake.db")]
    [InlineData("data/retake.txt")]
    [InlineData("")]
    public void UnsafeSqliteFile_FallsBackToDefault(string file)
    {
        var config = Validate(new DatabaseConfig { SqliteFile = file }, out var issues);
        Assert.Equal("data/retakev4.db", config.Database.SqliteFile);
        Assert.Equal(1, issues);
    }

    [Fact]
    public void MySqlWithoutConnectionString_FallsBackToSqlite()
    {
        var config = Validate(new DatabaseConfig { Type = DatabaseType.MySql }, out var issues);
        Assert.Equal(DatabaseType.Sqlite, config.Database.Type);
        Assert.Equal(1, issues);
    }

    [Fact]
    public void MySqlWithConnectionString_IsKept()
    {
        var config = Validate(new DatabaseConfig { Type = DatabaseType.MySql, MySqlConnectionString = "Server=db;Database=retake;User ID=retake;Password=x" }, out var issues);
        Assert.Equal(DatabaseType.MySql, config.Database.Type);
        Assert.Equal(0, issues);
    }

    [Fact]
    public void MissingDatabaseSection_FallsBackToDefaults()
    {
        var result = _validator.Validate(Defaults with { Database = null! }, Defaults, "allocation.json");
        Assert.Equal(DatabaseType.Sqlite, result.Config.Database.Type);
        Assert.Single(result.Issues);
    }
}
```

Ajouter à `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs` :
```csharp
    [Theory]
    [InlineData("allocation.awp.enabled")]
    [InlineData("allocation.awp.disabled")]
    [InlineData("allocation.import.done")]
    [InlineData("allocation.import.failed")]
    [InlineData("allocation.no_permission")]
    public void Phase3aKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`DatabaseConfig`, `AllocationConfigValidator` introuvables).

- [ ] **Step 2: Config et validateur**

`src/RetakeV4/Modules/Allocation/AllocationConfig.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Allocation;

public enum DatabaseType
{
    Sqlite,
    MySql,
    None,
}

public sealed record DatabaseConfig
{
    public DatabaseType Type { get; init; } = DatabaseType.Sqlite;

    public string SqliteFile { get; init; } = "data/retakev4.db";

    public string MySqlConnectionString { get; init; } = string.Empty;
}

public sealed record AllocationConfig : ModuleConfig
{
    public AllocationConfig() => Version = 2;

    public DatabaseConfig Database { get; init; } = new();
}
```

`src/RetakeV4/Modules/Allocation/AllocationConfigValidator.cs`
```csharp
using System.Text.RegularExpressions;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Allocation;

public sealed partial class AllocationConfigValidator : IConfigValidator<AllocationConfig>
{
    [GeneratedRegex(@"^[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)*\.db\z")]
    private static partial Regex SafeDatabaseFile();

    public ValidationResult<AllocationConfig> Validate(AllocationConfig config, AllocationConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var database = config.Database ?? Missing(defaults.Database, file, issues);
        if (!SafeDatabaseFile().IsMatch(database.SqliteFile ?? string.Empty))
        {
            issues.Add(new ConfigIssue(file, "Database.SqliteFile", "must be a relative .db path (letters, digits, _ - /); using default"));
            database = database with { SqliteFile = defaults.Database.SqliteFile };
        }
        if (database.Type == DatabaseType.MySql && string.IsNullOrWhiteSpace(database.MySqlConnectionString))
        {
            issues.Add(new ConfigIssue(file, "Database.MySqlConnectionString", "MySql selected without a connection string; using Sqlite"));
            database = database with { Type = DatabaseType.Sqlite };
        }
        return new ValidationResult<AllocationConfig>(config with { Database = database }, issues);
    }

    private static DatabaseConfig Missing(DatabaseConfig defaults, string file, List<ConfigIssue> issues)
    {
        issues.Add(new ConfigIssue(file, nameof(AllocationConfig.Database), "missing; using defaults"));
        return defaults;
    }
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo --filter "FullyQualifiedName~AllocationConfigValidatorTests"` → Expected: `Failed: 0`.

- [ ] **Step 3: Service de préférences**

`src/RetakeV4/Modules/Allocation/PreferenceService.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Modules.Allocation;

// Game-thread state: every mutation of _book happens on the game thread (directly or via onGameThread).
public sealed class PreferenceService : IAsyncDisposable
{
    private readonly IPreferenceRepository _store;
    private readonly ILogger _logger;
    private readonly PreferenceWriteQueue _writes;
    private PreferenceBook _book = PreferenceBook.Empty;

    public PreferenceService(IPreferenceRepository store, ILogger logger)
    {
        _store = store;
        _logger = logger;
        _writes = new PreferenceWriteQueue(store, logger);
    }

    public void PlayerConnected(ulong steamId, Action<Action> onGameThread)
    {
        var (book, token) = _book.StartSession(steamId);
        _book = book;
        _ = Task.Run(async () =>
        {
            var stored = await _store.LoadAsync(steamId, CancellationToken.None).ConfigureAwait(false);
            onGameThread(() => _book = _book.WithPlayer(steamId, token, stored));
        });
    }

    public void PlayerDisconnected(ulong steamId) => _book = _book.WithoutPlayer(steamId);

    public LoadoutPreference? RequestFor(ulong steamId, TeamSide team, string roundType) =>
        _book.RequestFor(steamId, team, roundType);

    public bool ToggleAwp(ulong steamId)
    {
        var (book, changes, optIn) = _book.ToggleAwp(steamId);
        _book = book;
        foreach (var change in changes)
        {
            _writes.Enqueue(change);
        }
        return optIn;
    }

    public async Task<int> ImportV3Async(string databaseFile, CancellationToken ct)
    {
        var rows = await V3SqliteReader.ReadAsync(databaseFile, ct).ConfigureAwait(false);
        var preferences = V3PreferenceImport.Convert(rows);
        await _store.ImportAsync(preferences, ct).ConfigureAwait(false);
        _logger.LogInformation("Imported {Count} V3 preference(s) from {File}", preferences.Count, databaseFile);
        return preferences.Count;
    }

    public ValueTask DisposeAsync() => _writes.DisposeAsync();
}
```

- [ ] **Step 4: Branchement dans le module**

Dans `src/RetakeV4/Modules/Allocation/AllocationModule.cs` :
- ajouter les `using` `CounterStrikeSharp.API`, `CounterStrikeSharp.API.Core`, `CounterStrikeSharp.API.Modules.Admin`, `CounterStrikeSharp.API.Modules.Commands`, `RetakeV4.Persistence` ;
- ajouter les constantes et le champ :
```csharp
    private const string RootFlag = "@retakev4/root";
    private PreferenceService? _preferences;
```
- dans `LoadConfig`, remplacer le chargement de `allocation.json` par `store.Load("allocation.json", new AllocationConfig(), new AllocationConfigValidator())` ;
- remplacer `Load` et `Unload` par :
```csharp
    public void Load(ModuleContext context)
    {
        _context = context;
        _preferences = new PreferenceService(CreateStore(context), context.Logger);
        var hooks = context.Hooks;
        hooks.PreparationStep(new DelegatePreparationStep("loadout", PreparationOrder.Loadout, AssignLoadouts));
        hooks.OnEvent<EventPlayerConnectFull>("player_connect_full", e => OnConnected(e.Userid));
        hooks.OnEvent<EventPlayerDisconnect>("player_disconnect", e =>
        {
            if (e.Userid is { IsValid: true, IsBot: false } player)
            {
                _preferences?.PlayerDisconnected(player.SteamID);
            }
        });
        hooks.Command("css_awp", "Toggles AWP volunteering", OnAwpCommand);
        hooks.Command("css_retake_import_v3", "Imports V3 weapon preferences: css_retake_import_v3 <path to cs2retake.db>", OnImportCommand);
        foreach (var player in PlayerQueries.Humans())
        {
            OnConnected(player);
        }
    }

    public void Unload()
    {
        _preferences?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        _preferences = null;
        _context = null;
    }
```
- ajouter les méthodes :
```csharp
    private IPreferenceRepository CreateStore(ModuleContext context)
    {
        var database = _config.Database;
        IPreferenceRepository repository = database.Type switch
        {
            DatabaseType.MySql => new MySqlPreferenceRepository(database.MySqlConnectionString),
            DatabaseType.Sqlite => new SqlitePreferenceRepository(Path.Combine(context.Plugin.ModuleDirectory, database.SqliteFile)),
            _ => new NoOpPreferenceRepository(),
        };
        var store = new ResilientPreferenceStore(repository, context.Logger, () => DateTimeOffset.UtcNow);
        _ = Task.Run(() => InitializeAsync(repository, context.Logger));
        return store;
    }

    private static async Task InitializeAsync(IPreferenceRepository repository, ILogger logger)
    {
        try
        {
            switch (repository)
            {
                case SqlitePreferenceRepository sqlite:
                    await sqlite.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
                case MySqlPreferenceRepository mySql:
                    await mySql.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Preference database could not be initialized; preferences will not be saved until it becomes available");
        }
    }

    private void OnConnected(CCSPlayerController? player)
    {
        if (player is { IsValid: true, IsBot: false, IsHLTV: false } && player.SteamID != 0)
        {
            _preferences?.PlayerConnected(player.SteamID, apply => Server.NextFrame(() => _context?.Guard.Run(Name, "preferences_loaded", apply)));
        }
    }

    private void OnAwpCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true } || player.SteamID == 0 || _preferences is null)
        {
            return;
        }
        var optIn = _preferences.ToggleAwp(player.SteamID);
        Context.Text.Chat(player, optIn ? "allocation.awp.enabled" : "allocation.awp.disabled");
    }

    private void OnImportCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null && !AdminManager.PlayerHasPermissions(player, RootFlag))
        {
            Context.Text.Chat(player, "allocation.no_permission");
            return;
        }
        var file = command.GetArg(1);
        var preferences = _preferences;
        if (preferences is null || string.IsNullOrWhiteSpace(file))
        {
            command.ReplyToCommand("usage: css_retake_import_v3 <path to cs2retake.db>");
            return;
        }
        _ = Task.Run(async () =>
        {
            string reply;
            try
            {
                reply = Context.Text.Server("allocation.import.done", await preferences.ImportV3Async(file, CancellationToken.None).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Context.Logger.LogWarning(ex, "V3 preference import from {File} failed", file);
                reply = Context.Text.Server("allocation.import.failed", ex.Message);
            }
            Server.NextFrame(() => _context?.Guard.Run(Name, "import_reply", () => Reply(player, reply)));
        });
    }

    // CommandInfo is only valid during the command callback: the delayed reply targets the player or the server console directly.
    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player is { IsValid: true })
        {
            player.PrintToChat(message);
            return;
        }
        Server.PrintToConsole(message);
    }
```
- dans `AssignLoadouts`, construire les requêtes avec les préférences :
```csharp
        var requests = players
            .Select(p => new LoadoutRequest(
                new PlayerId(p.Controller.Slot),
                p.Side!.Value,
                _preferences?.RequestFor(p.Controller.SteamID, p.Side.Value, definition.Name)))
            .ToList();
```

Lang : ajouter à `src/RetakeV4/lang/en.json`
```json
  "allocation.awp.enabled": "You now volunteer for the AWP on full-buy rounds.",
  "allocation.awp.disabled": "You no longer volunteer for the AWP.",
  "allocation.import.done": "{0} V3 preference(s) imported.",
  "allocation.import.failed": "V3 import failed: {0}",
  "allocation.no_permission": "You do not have permission to use this command."
```
et à `src/RetakeV4/lang/fr.json`
```json
  "allocation.awp.enabled": "Tu es maintenant volontaire pour l'AWP sur les rounds full-buy.",
  "allocation.awp.disabled": "Tu n'es plus volontaire pour l'AWP.",
  "allocation.import.done": "{0} préférence(s) V3 importée(s).",
  "allocation.import.failed": "Échec de l'import V3 : {0}",
  "allocation.no_permission": "Tu n'as pas la permission d'utiliser cette commande."
```

- [ ] **Step 5: Package**

Dans `scripts/package-dev.ps1`, après la boucle de copie des fichiers RetakeV4, ajouter :
```powershell
foreach ($dependency in @("Microsoft.Data.Sqlite.dll", "SQLitePCLRaw.core.dll", "SQLitePCLRaw.batteries_v2.dll", "SQLitePCLRaw.provider.e_sqlite3.dll", "MySqlConnector.dll")) {
    $source = Join-Path $bin $dependency
    if (-not (Test-Path $source)) { throw "Missing dependency: $dependency" }
    Copy-Item $source $pluginDir
}
Copy-Item (Join-Path $bin "runtimes") $pluginDir -Recurse
```

- [ ] **Step 6: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
puis (outil PowerShell) `pwsh -NoProfile -File scripts/package-dev.ps1` et vérifier la présence de `artifacts/dev/addons/counterstrikesharp/plugins/RetakeV4/runtimes/linux-x64/native/libe_sqlite3.so` et `win-x64/native/e_sqlite3.dll`.
Expected: `0 Avertissement(s)`, `Failed: 0`, package complet. Si un nom de DLL SQLitePCLRaw diffère en 3.0.3 (vérifier `ls src/RetakeV4/bin/Release/net10.0/SQLitePCLRaw*`), ajuster la liste et ledger la ruling.

- [ ] **Step 7: Commit**

```bash
git add src/RetakeV4/Modules/Allocation src/RetakeV4/lang scripts/package-dev.ps1 tests/RetakeV4.Integration.Tests/Modules/Allocation tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs
git commit -m "feat: préférences joueurs dans l'allocation (chargement à la connexion, !awp, import V3)"
```

---

### Task 7: Checklist en jeu phase 3a, documentation et vérification finale

**Files:**
- Modify: `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`

- [ ] **Step 1: Checklist**

Ajouter à `docs/CHECKLIST-INGAME.md` :
```markdown

## Phase 3a — Préférences et persistance
- [ ] Premier démarrage : `plugins/RetakeV4/data/retakev4.db` est créé, aucun avertissement de base de données dans les logs.
- [ ] `!awp` : message « Tu es maintenant volontaire pour l'AWP… » ; avec au moins 5 joueurs, un volontaire de chaque camp reçoit parfois l'AWP en FullBuy (≈ 30 %).
- [ ] `!awp` à nouveau : message « Tu n'es plus volontaire » et plus d'AWP.
- [ ] Déconnexion puis reconnexion (ou changement de map) : le volontariat AWP est conservé.
- [ ] `css_retake_import_v3 <chemin>/CS2Retake/data/CommandAllocator/cs2retake.db` (console serveur ou admin root) : message « N préférence(s) V3 importée(s) » ; un joueur importé retrouve ses armes V3 (ex. M4A1-S en FullBuy CT) après reconnexion.
- [ ] Import avec un chemin faux : message « Échec de l'import V3 : … », aucun crash.
- [ ] `allocation.json` en `MySql` avec une chaîne valide : table `player_loadout` créée, préférences conservées entre deux redémarrages.
- [ ] Base indisponible (fichier en lecture seule ou MySQL arrêté) : un avertissement « Preference database unavailable… », les joueurs reçoivent l'équipement par défaut, aucune erreur en boucle.
```

- [ ] **Step 2: CLAUDE.md**

Dans la section Structure de `CLAUDE.md`, ajouter :
```markdown
- `src/RetakeV4/Persistence` : dépôts de préférences (SQLite, MySQL, NoOp), migrations, magasin résilient, file d'écriture, lecteur de base V3.
```
et dans la section Règles :
```markdown
- Jamais d'accès base de données sur le thread de jeu : `Task.Run` pour lire, `PreferenceWriteQueue` pour écrire, retour au jeu via `Server.NextFrame` + `ModuleGuard`. Requêtes SQL paramétrées uniquement.
```

- [ ] **Step 3: Vérification finale**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo && dotnet test tests/RetakeV4.Domain.Tests --nologo -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`
puis (outil PowerShell) `pwsh -NoProfile -File scripts/package-dev.ps1`
Expected: 0 warning, `Failed: 0`, couverture Domain ≥ 80 %, `Package ready`.

- [ ] **Step 4: Commit**

```bash
git add docs/CHECKLIST-INGAME.md CLAUDE.md
git commit -m "docs: checklist en jeu phase 3a et guide du dépôt"
```
