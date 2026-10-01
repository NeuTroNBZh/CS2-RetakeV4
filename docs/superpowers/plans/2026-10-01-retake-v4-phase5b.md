# RetakeV4 phase 5b — API publique, release et documentation (4.0.0) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Livrer RetakeV4 4.0.0 : une API publique `RetakeV4.Contracts` pour les autres plugins, l'export des configs par défaut, des zips de release produits par GitHub Actions, et la documentation (README, migration depuis V3).

**Architecture:** `RetakeV4.Contracts` est une petite DLL autonome (types + `IRetakeApi` + capacité `retakev4:api`) installée dans `shared/`. Le module `Api` l'implémente (`RetakeApiService`, testable sans CounterStrikeSharp) en traduisant les événements du bus ; la détection du dernier joueur vivant et des entrées en file est dans le Domain. `ConfigExport` écrit les JSON par défaut de tous les modules (même chemin que le premier démarrage) ; `tools/RetakeV4.ConfigExporter` l'expose en ligne de commande pour le script de release.

**Tech Stack:** C# / .NET 10, CounterStrikeSharp.API 1.0.370, xUnit 2.9.3, PowerShell 7, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-30-retake-v4-design.md` (sections 11, 15, 16)

## Global Constraints

- CounterStrikeSharp.API 1.0.370, .NET 10 (SDK 10.0.300, `global.json`), `dotnet build RetakeV4.sln -c Release` : 0 warning.
- `RetakeV4.Domain` sans CounterStrikeSharp, couverture de lignes ≥ 80 %.
- Spec 11 : capacité `PluginCapability<IRetakeApi>("retakev4:api")`, DLL dans `addons/counterstrikesharp/shared/RetakeV4.Contracts/` ; événements immuables sur le thread de jeu ; exception d'un abonné interceptée et journalisée.
- Spec 15 : arborescence `addons/counterstrikesharp/plugins/RetakeV4/`, `addons/counterstrikesharp/shared/RetakeV4.Contracts/`, `addons/counterstrikesharp/configs/plugins/RetakeV4/`, `cfg/RetakeV4/retake.cfg` ; zips `RetakeV4-x.y.z.zip` et `RetakeV4-x.y.z-no-configs.zip` ; CI build + tests + couverture à chaque push, release sur tag `v*`.
- Spec 16 : contenu de `docs/MIGRATION-V3.md`.

## Décisions de conception

1. **Types de l'API** : `RetakeV4.Contracts` a ses propres enums (`BombSite`, `ForceSiteMode`, `RetakeState`, `RetakeTeam`) et records ; le plugin traduit depuis le Domain. Les joueurs sont décrits par `RetakePlayer(Slot, SteamId)`. Nom de l'événement de loadout conforme à la spec : `LoadoutAssigned`.
2. **Capacité** : `PluginCapability<IRetakeApi?>` (nullable comme CS2-SimpleAdmin) : après déchargement du module, `Get()` renvoie null. Enregistrée au chargement du module `Api` ; un échec d'enregistrement (rechargement à chaud) est journalisé sans bloquer.
3. **Dernier vivant** : signalé une fois par équipe et par round quand une équipe d'au moins deux joueurs (humains) passe à un seul vivant, pendant le round live uniquement.
4. **`ForceSite` / `RequestScramble` de l'API** : publiés sur le bus avec un demandeur `null` (comme la console serveur) — mêmes règles que les commandes.
5. **Liste des modules** : extraite dans `ModuleCatalog.CreateAll()` (sans dépendance à `BasePlugin`) pour être partagée par le plugin et l'export des configs.
6. **Version** : 4.0.0 dans le csproj et `ModuleVersion`. Le tag `v4.0.0` est poussé par le mainteneur, pas par ce plan.
7. **Base de données V3** : V3 proposait PostgreSQL ; V4 ne gère que SQLite et MySQL (documenté dans la migration).

## Review Focus

1. Un abonné de l'API qui lève une exception → journalisé, les autres abonnés et le retake continuent (Task 2 : `FaultySubscriber_IsLoggedAndOthersStillRun`).
2. Une équipe qui commence le round avec un seul joueur → pas de « dernier vivant » ; une équipe signalée ne l'est pas deux fois (Task 1 : `SoloTeam_IsNeverReported`, `ATeam_IsReportedOncePerRound`).
3. Un `TeamStateChanged` répété sans nouvelle entrée en file → aucun `PlayerQueued` (Task 1 : `OnlyNewEntries_AreReported` ; Task 2).
4. Export des configs relancé sur un dossier déjà rempli → aucun fichier écrasé (Task 4 : `Export_IsIdempotent`).
5. Le zip « sans configs » ne contient aucun fichier de `configs/` ; le zip complet contient un JSON par module (Task 5 : vérification scriptée).

---

## File Structure

```
src/RetakeV4.Domain/Teams/LastAliveTracker.cs, QueueChanges.cs, Events/RoundEvents.cs   (Task 1)
src/RetakeV4/Modules/Allocation/AllocationModule.cs                                    (Task 1, publication)
src/RetakeV4.Contracts/*, src/RetakeV4/Modules/Api/RetakeApiService.cs                 (Task 2)
src/RetakeV4/Modules/Api/ApiConfig.cs, ApiModule.cs, Modules/ModuleCatalog.cs,
    RetakeV4Plugin.cs, scripts/package-dev.ps1                                         (Task 3)
src/RetakeV4/Configuration/ConfigExport.cs, tools/RetakeV4.ConfigExporter/*            (Task 4)
scripts/package-release.ps1, .github/workflows/ci.yml, release.yml                     (Task 5)
README.md, docs/MIGRATION-V3.md, docs/CHECKLIST-INGAME.md, CLAUDE.md                   (Task 6)
```

---

### Task 1: Dernier vivant, nouvelles entrées en file et loadouts publiés (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Teams/LastAliveTracker.cs`, `src/RetakeV4.Domain/Teams/QueueChanges.cs`
- Modify: `src/RetakeV4.Domain/Events/RoundEvents.cs`, `src/RetakeV4/Modules/Allocation/AllocationModule.cs`
- Test: `tests/RetakeV4.Domain.Tests/Teams/LastAliveTrackerTests.cs`, `tests/RetakeV4.Domain.Tests/Teams/QueueChangesTests.cs`

**Interfaces:**
- Consumes: `TeamState`, `QueuedPlayer`, `PlayerId`, `TeamSide`, `Loadout`.
- Produces:
  - `sealed record TeamCount(int Alive, int Playing)`, `sealed record LastAliveTracker(ImmutableHashSet<TeamSide> Reported)` : `Empty`, `Update(TeamCount t, TeamCount ct)` → `(LastAliveTracker Tracker, IReadOnlyList<TeamSide> NewlyLast)`.
  - `static QueueChanges.NewlyQueued(TeamState before, TeamState after)` → `IReadOnlyList<(PlayerId Player, int Position)>`.
  - `sealed record LoadoutsAssigned(int RoundNumber, IReadOnlyDictionary<PlayerId, Loadout> Loadouts)` publié par Allocation après la distribution.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Teams/LastAliveTrackerTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Teams;

public class LastAliveTrackerTests
{
    [Fact]
    public void ATeam_IsReportedOncePerRound()
    {
        var (tracker, first) = LastAliveTracker.Empty.Update(new TeamCount(1, 3), new TeamCount(2, 2));
        Assert.Equal(new[] { TeamSide.T }, first);
        var (_, again) = tracker.Update(new TeamCount(1, 3), new TeamCount(1, 2));
        Assert.Equal(new[] { TeamSide.CT }, again);
    }

    [Fact]
    public void SoloTeam_IsNeverReported()
    {
        var (_, newly) = LastAliveTracker.Empty.Update(new TeamCount(1, 1), new TeamCount(3, 3));
        Assert.Empty(newly);
    }

    [Fact]
    public void BothTeams_CanBeReportedTogether()
    {
        var (_, newly) = LastAliveTracker.Empty.Update(new TeamCount(1, 2), new TeamCount(1, 4));
        Assert.Equal(new[] { TeamSide.T, TeamSide.CT }, newly);
    }

    [Fact]
    public void NoOneOrSeveralAlive_IsNotReported()
    {
        var (tracker, newly) = LastAliveTracker.Empty.Update(new TeamCount(0, 3), new TeamCount(2, 3));
        Assert.Empty(newly);
        Assert.Same(LastAliveTracker.Empty, tracker);
    }
}
```

`tests/RetakeV4.Domain.Tests/Teams/QueueChangesTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Teams;

public class QueueChangesTests
{
    private static TeamState Queue(params QueuedPlayer[] queued) => TeamState.Empty with { Queue = queued };

    [Fact]
    public void NewEntries_AreReportedWithTheirPosition()
    {
        var before = Queue(new QueuedPlayer(new PlayerId(1), 0, 1));
        var after = Queue(new QueuedPlayer(new PlayerId(1), 0, 1), new QueuedPlayer(new PlayerId(2), 1, 2));
        Assert.Equal(new[] { (new PlayerId(2), 1) }, QueueChanges.NewlyQueued(before, after));
    }

    [Fact]
    public void OnlyNewEntries_AreReported()
    {
        var state = Queue(new QueuedPlayer(new PlayerId(1), 0, 1));
        Assert.Empty(QueueChanges.NewlyQueued(state, state));
        Assert.Empty(QueueChanges.NewlyQueued(state, TeamState.Empty));
    }
}
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`LastAliveTracker`, `QueueChanges` introuvables).

- [ ] **Step 2: Implémentation**

`src/RetakeV4.Domain/Teams/LastAliveTracker.cs`
```csharp
using System.Collections.Immutable;
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public sealed record TeamCount(int Alive, int Playing);

// A team down to exactly one living player (a clutch) is reported once per round, and only if it had at least two players.
public sealed record LastAliveTracker(ImmutableHashSet<TeamSide> Reported)
{
    public static LastAliveTracker Empty { get; } = new(ImmutableHashSet<TeamSide>.Empty);

    public (LastAliveTracker Tracker, IReadOnlyList<TeamSide> NewlyLast) Update(TeamCount t, TeamCount ct)
    {
        var newly = new[] { (Side: TeamSide.T, Count: t), (Side: TeamSide.CT, Count: ct) }
            .Where(c => c.Count.Alive == 1 && c.Count.Playing >= 2 && !Reported.Contains(c.Side))
            .Select(c => c.Side)
            .ToList();
        return (newly.Count == 0 ? this : new LastAliveTracker(Reported.Union(newly)), newly);
    }
}
```

`src/RetakeV4.Domain/Teams/QueueChanges.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public static class QueueChanges
{
    public static IReadOnlyList<(PlayerId Player, int Position)> NewlyQueued(TeamState before, TeamState after) =>
        after.OrderedQueue()
            .Select((queued, index) => (queued.Player, Position: index + 1))
            .Where(entry => !before.IsQueued(entry.Player))
            .ToList();
}
```

Dans `src/RetakeV4.Domain/Events/RoundEvents.cs`, ajouter :
```csharp
public sealed record LoadoutsAssigned(int RoundNumber, IReadOnlyDictionary<PlayerId, Loadout> Loadouts);
```

Dans `src/RetakeV4/Modules/Allocation/AllocationModule.cs`, dans `AssignLoadouts`, juste après `_lastPlan = plan.ToImmutableDictionary();`, ajouter :
```csharp
        Context.Bus.Publish(new LoadoutsAssigned(context.RoundNumber, plan));
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4.Domain/Teams src/RetakeV4.Domain/Events/RoundEvents.cs src/RetakeV4/Modules/Allocation/AllocationModule.cs tests/RetakeV4.Domain.Tests/Teams
git commit -m "feat: dernier joueur vivant, nouvelles entrées en file et publication des loadouts (Domain)"
```

---

### Task 2: Contrats publics et service d'API

**Files:**
- Create: `src/RetakeV4.Contracts/RetakeV4.Contracts.csproj`, `src/RetakeV4.Contracts/IRetakeApi.cs`, `src/RetakeV4/Modules/Api/RetakeApiService.cs`
- Modify: `RetakeV4.sln`, `src/RetakeV4/RetakeV4.csproj`, `tests/RetakeV4.Integration.Tests/RetakeV4.Integration.Tests.csproj`
- Test: `tests/RetakeV4.Integration.Tests/Modules/Api/RetakeApiServiceTests.cs`

**Interfaces:**
- Consumes: `IEventBus`, `RoundPhaseChanged`, `RoundPrepared`, `BombPlanted`, `LoadoutsAssigned`, `TeamStateChanged`, `ForceSiteRequested`, `ScrambleRequested`, `QueueChanges` (Task 1).
- Produces:
  - Contrats (namespace `RetakeV4.Contracts`) : enums `RetakeState`, `BombSite`, `ForceSiteMode`, `RetakeTeam` ; records `RetakePlayer(int Slot, ulong SteamId)`, `RoundPreparedEvent`, `BombPlantedEvent`, `PlayerLoadout`, `LoadoutAssignedEvent`, `LastPlayerAliveEvent`, `RoundEndedEvent`, `PlayerQueuedEvent` ; `interface IRetakeApi` ; `static class RetakeApi { const int Version = 1; PluginCapability<IRetakeApi?> Capability }`.
  - `internal sealed class RetakeApiService(IEventBus bus, ILogger logger, Func<ulong, int?> slotOf, Func<int, ulong> steamIdOf) : IRetakeApi` avec `OnPhase`, `OnRoundPrepared`, `OnBombPlanted`, `OnLoadouts`, `OnTeams`, `OnLastAlive(TeamSide, int slot)`, `OnRoundEnded(int roundNumber, TeamSide? winner)`.

- [ ] **Step 1: Projet Contracts**

`src/RetakeV4.Contracts/RetakeV4.Contracts.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>RetakeV4.Contracts</RootNamespace>
    <AssemblyName>RetakeV4.Contracts</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CounterStrikeSharp.API" Version="1.0.370">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

Run: `dotnet sln RetakeV4.sln add src/RetakeV4.Contracts/RetakeV4.Contracts.csproj --solution-folder src`

Dans `src/RetakeV4/RetakeV4.csproj`, ajouter dans l'`ItemGroup` des `ProjectReference` :
```xml
    <ProjectReference Include="..\RetakeV4.Contracts\RetakeV4.Contracts.csproj">
      <Private>false</Private>
    </ProjectReference>
```
(la DLL vient du dossier `shared/` du serveur, elle n'est pas copiée à côté du plugin).

Dans `tests/RetakeV4.Integration.Tests/RetakeV4.Integration.Tests.csproj`, ajouter :
```xml
    <ProjectReference Include="..\..\src\RetakeV4.Contracts\RetakeV4.Contracts.csproj" />
```

- [ ] **Step 2: Écrire le test qui échoue**

`tests/RetakeV4.Integration.Tests/Modules/Api/RetakeApiServiceTests.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Contracts;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Spawns;
using RetakeV4.Domain.Teams;
using RetakeV4.Modules.Api;
using ContractSite = RetakeV4.Contracts.BombSite;
using ContractForceMode = RetakeV4.Contracts.ForceSiteMode;
using DomainSite = RetakeV4.Domain.Common.BombSite;

namespace RetakeV4.Integration.Tests.Modules.Api;

public class RetakeApiServiceTests
{
    private readonly EventBus _bus = new(_ => { });
    private readonly ListLogger _logger = new();
    private readonly RetakeApiService _api;

    public RetakeApiServiceTests() =>
        _api = new RetakeApiService(_bus, _logger, steamId => steamId == 76561198000000005UL ? 5 : null, slot => 76561198000000000UL + (ulong)slot);

    [Fact]
    public void RoundPrepared_UpdatesTheState_AndIsRaised()
    {
        var raised = new List<RoundPreparedEvent>();
        _api.RoundPrepared += raised.Add;
        _api.OnPhase(new RoundPhaseChanged(RoundPhase.Preparing, RoundPhase.FreezeTime, 3));
        _api.OnRoundPrepared(new RoundPrepared(new PreparationContext(3) { RoundType = "FullBuy", Site = DomainSite.B, Planter = new PlayerId(5) }));
        Assert.Equal(RetakeState.FreezeTime, _api.State);
        Assert.Equal(ContractSite.B, _api.CurrentSite);
        Assert.Equal("FullBuy", _api.CurrentRoundType);
        Assert.Equal(new RoundPreparedEvent(3, "FullBuy", ContractSite.B, new RetakePlayer(5, 76561198000000005UL)), Assert.Single(raised));
        Assert.Equal(1, _api.ApiVersion);
    }

    [Fact]
    public void FaultySubscriber_IsLoggedAndOthersStillRun()
    {
        var reached = false;
        _api.BombPlanted += _ => throw new InvalidOperationException("broken plugin");
        _api.BombPlanted += _ => reached = true;
        _api.OnBombPlanted(new BombPlanted(DomainSite.A, null));
        Assert.True(reached);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void ForceSiteAndScramble_AreRequestedLikeTheConsole()
    {
        var forces = new List<ForceSiteRequested>();
        var scrambles = new List<ScrambleRequested>();
        _bus.Subscribe<ForceSiteRequested>("test", forces.Add);
        _bus.Subscribe<ScrambleRequested>("test", scrambles.Add);
        _api.ForceSite(ContractSite.B, ContractForceMode.Sticky);
        _api.RequestScramble();
        Assert.Equal(new ForceSiteRequested(null, new ForceSiteRequest(new SiteForce(DomainSite.B, Domain.Spawns.ForceSiteMode.Sticky))), Assert.Single(forces));
        Assert.Null(Assert.Single(scrambles).Requester);
    }

    [Fact]
    public void QueuePositions_AndNewEntries()
    {
        var queued = new List<PlayerQueuedEvent>();
        _api.PlayerQueued += queued.Add;
        var state = TeamState.Empty with { Queue = new[] { new QueuedPlayer(new PlayerId(5), 0, 1) } };
        _api.OnTeams(new TeamStateChanged(state));
        _api.OnTeams(new TeamStateChanged(state));
        Assert.Equal(new PlayerQueuedEvent(new RetakePlayer(5, 76561198000000005UL), 1), Assert.Single(queued));
        Assert.Equal(1, _api.GetQueuePosition(76561198000000005UL));
        Assert.Null(_api.GetQueuePosition(1UL));
    }

    [Fact]
    public void Loadouts_LastAliveAndRoundEnd_AreTranslated()
    {
        var loadouts = new List<LoadoutAssignedEvent>();
        var lastAlive = new List<LastPlayerAliveEvent>();
        var ended = new List<RoundEndedEvent>();
        _api.LoadoutAssigned += loadouts.Add;
        _api.LastPlayerAlive += lastAlive.Add;
        _api.RoundEnded += ended.Add;
        var loadout = new Loadout("weapon_ak47", "weapon_glock", ArmorKind.KevlarHelmet, false, true, new[] { "weapon_flashbang" });
        _api.OnLoadouts(new LoadoutsAssigned(4, new Dictionary<PlayerId, Loadout> { [new PlayerId(2)] = loadout }));
        _api.OnLastAlive(TeamSide.CT, 2);
        _api.OnRoundEnded(4, TeamSide.T);
        var player = new RetakePlayer(2, 76561198000000002UL);
        var assigned = Assert.Single(Assert.Single(loadouts).Loadouts);
        Assert.Equal(player, assigned.Player);
        Assert.Equal("weapon_ak47", assigned.Primary);
        Assert.True(assigned.Zeus);
        Assert.Equal(new[] { "weapon_flashbang" }, assigned.Grenades);
        Assert.Equal(new LastPlayerAliveEvent(RetakeTeam.CT, player), Assert.Single(lastAlive));
        Assert.Equal(new RoundEndedEvent(4, RetakeTeam.T), Assert.Single(ended));
    }
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Contracts` / `RetakeApiService` introuvables).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Contracts/IRetakeApi.cs`
```csharp
using CounterStrikeSharp.API.Core.Capabilities;

namespace RetakeV4.Contracts;

public enum RetakeState
{
    Warmup,
    Preparing,
    FreezeTime,
    Live,
    PostRound,
}

public enum BombSite
{
    A,
    B,
}

public enum ForceSiteMode
{
    Once,
    Sticky,
}

public enum RetakeTeam
{
    T,
    CT,
}

public sealed record RetakePlayer(int Slot, ulong SteamId);

public sealed record RoundPreparedEvent(int RoundNumber, string? RoundType, BombSite? Site, RetakePlayer? Planter);

public sealed record BombPlantedEvent(BombSite? Site, RetakePlayer? Planter);

public sealed record PlayerLoadout(RetakePlayer Player, string? Primary, string Secondary, bool DefuseKit, bool Zeus, IReadOnlyList<string> Grenades);

public sealed record LoadoutAssignedEvent(int RoundNumber, IReadOnlyList<PlayerLoadout> Loadouts);

public sealed record LastPlayerAliveEvent(RetakeTeam Team, RetakePlayer Player);

public sealed record RoundEndedEvent(int RoundNumber, RetakeTeam? Winner);

public sealed record PlayerQueuedEvent(RetakePlayer Player, int Position);

// Events are raised on the game thread. An exception thrown by a subscriber is logged by RetakeV4 and never stops the retake.
public interface IRetakeApi
{
    int ApiVersion { get; }

    RetakeState State { get; }

    BombSite? CurrentSite { get; }

    string? CurrentRoundType { get; }

    int? GetQueuePosition(ulong steamId);

    void ForceSite(BombSite site, ForceSiteMode mode);

    void RequestScramble();

    event Action<RoundPreparedEvent>? RoundPrepared;

    event Action<BombPlantedEvent>? BombPlanted;

    event Action<LoadoutAssignedEvent>? LoadoutAssigned;

    event Action<LastPlayerAliveEvent>? LastPlayerAlive;

    event Action<RoundEndedEvent>? RoundEnded;

    event Action<PlayerQueuedEvent>? PlayerQueued;
}

public static class RetakeApi
{
    public const int Version = 1;

    // Get() returns null while RetakeV4 (or its Api module) is not loaded.
    public static PluginCapability<IRetakeApi?> Capability { get; } = new("retakev4:api");
}
```

`src/RetakeV4/Modules/Api/RetakeApiService.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Contracts;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Spawns;
using RetakeV4.Domain.Teams;
using ContractForceMode = RetakeV4.Contracts.ForceSiteMode;
using ContractSite = RetakeV4.Contracts.BombSite;
using DomainBombPlanted = RetakeV4.Domain.Events.BombPlanted;
using DomainRoundPrepared = RetakeV4.Domain.Events.RoundPrepared;
using DomainSite = RetakeV4.Domain.Common.BombSite;
using DomainForceMode = RetakeV4.Domain.Spawns.ForceSiteMode;

namespace RetakeV4.Modules.Api;

// Translates bus events into the public contract. Game thread only.
internal sealed class RetakeApiService : IRetakeApi
{
    private readonly IEventBus _bus;
    private readonly ILogger _logger;
    private readonly Func<ulong, int?> _slotOf;
    private readonly Func<int, ulong> _steamIdOf;
    private TeamState _teams = TeamState.Empty;

    public RetakeApiService(IEventBus bus, ILogger logger, Func<ulong, int?> slotOf, Func<int, ulong> steamIdOf)
    {
        _bus = bus;
        _logger = logger;
        _slotOf = slotOf;
        _steamIdOf = steamIdOf;
    }

    public event Action<RoundPreparedEvent>? RoundPrepared;

    public event Action<BombPlantedEvent>? BombPlanted;

    public event Action<LoadoutAssignedEvent>? LoadoutAssigned;

    public event Action<LastPlayerAliveEvent>? LastPlayerAlive;

    public event Action<RoundEndedEvent>? RoundEnded;

    public event Action<PlayerQueuedEvent>? PlayerQueued;

    public int ApiVersion => RetakeApi.Version;

    public RetakeState State { get; private set; } = RetakeState.Warmup;

    public ContractSite? CurrentSite { get; private set; }

    public string? CurrentRoundType { get; private set; }

    public int? GetQueuePosition(ulong steamId) => _slotOf(steamId) is { } slot ? _teams.QueuePosition(new PlayerId(slot)) : null;

    public void ForceSite(ContractSite site, ContractForceMode mode)
    {
        var force = new SiteForce(Map(site), mode == ContractForceMode.Sticky ? DomainForceMode.Sticky : DomainForceMode.Once);
        _bus.Publish(new ForceSiteRequested(null, new ForceSiteRequest(force)));
    }

    public void RequestScramble() => _bus.Publish(new ScrambleRequested(null));

    public void OnPhase(RoundPhaseChanged e) => State = e.To switch
    {
        RoundPhase.Warmup => RetakeState.Warmup,
        RoundPhase.Preparing => RetakeState.Preparing,
        RoundPhase.FreezeTime => RetakeState.FreezeTime,
        RoundPhase.Live => RetakeState.Live,
        _ => RetakeState.PostRound,
    };

    public void OnRoundPrepared(DomainRoundPrepared e)
    {
        CurrentSite = Map(e.Context.Site);
        CurrentRoundType = e.Context.RoundType;
        Raise(RoundPrepared, new RoundPreparedEvent(e.Context.RoundNumber, e.Context.RoundType, CurrentSite, PlayerOf(e.Context.Planter)));
    }

    public void OnBombPlanted(DomainBombPlanted e) => Raise(BombPlanted, new BombPlantedEvent(Map(e.Site), PlayerOf(e.Planter)));

    public void OnLoadouts(LoadoutsAssigned e)
    {
        var loadouts = e.Loadouts
            .OrderBy(entry => entry.Key.Slot)
            .Select(entry => new PlayerLoadout(
                Player(entry.Key.Slot), entry.Value.Primary, entry.Value.Secondary, entry.Value.DefuseKit, entry.Value.Zeus, entry.Value.Grenades))
            .ToList();
        Raise(LoadoutAssigned, new LoadoutAssignedEvent(e.RoundNumber, loadouts));
    }

    public void OnTeams(TeamStateChanged e)
    {
        var added = QueueChanges.NewlyQueued(_teams, e.State);
        _teams = e.State;
        foreach (var (player, position) in added)
        {
            Raise(PlayerQueued, new PlayerQueuedEvent(Player(player.Slot), position));
        }
    }

    public void OnLastAlive(TeamSide team, int slot) => Raise(LastPlayerAlive, new LastPlayerAliveEvent(Map(team), Player(slot)));

    public void OnRoundEnded(int roundNumber, TeamSide? winner) =>
        Raise(RoundEnded, new RoundEndedEvent(roundNumber, winner is { } side ? Map(side) : null));

    private void Raise<T>(Action<T>? handlers, T e)
    {
        if (handlers is null)
        {
            return;
        }
        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(e);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A RetakeV4 API subscriber failed on {Event}", typeof(T).Name);
            }
        }
    }

    private RetakePlayer Player(int slot) => new(slot, _steamIdOf(slot));

    private RetakePlayer? PlayerOf(PlayerId? id) => id is { } player ? Player(player.Slot) : null;

    private static RetakeTeam Map(TeamSide side) => side == TeamSide.T ? RetakeTeam.T : RetakeTeam.CT;

    private static ContractSite? Map(DomainSite? site) => site switch
    {
        DomainSite.A => ContractSite.A,
        DomainSite.B => ContractSite.B,
        _ => null,
    };

    private static DomainSite Map(ContractSite site) => site == ContractSite.A ? DomainSite.A : DomainSite.B;
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add RetakeV4.sln src/RetakeV4.Contracts src/RetakeV4/RetakeV4.csproj src/RetakeV4/Modules/Api tests/RetakeV4.Integration.Tests
git commit -m "feat: contrats publics RetakeV4.Contracts et service d'API"
```

---

### Task 3: Module Api, capacité et liste des modules

**Files:**
- Create: `src/RetakeV4/Modules/Api/ApiConfig.cs`, `src/RetakeV4/Modules/Api/ApiModule.cs`, `src/RetakeV4/Modules/ModuleCatalog.cs`
- Modify: `src/RetakeV4/RetakeV4Plugin.cs`, `scripts/package-dev.ps1`
- Test: `tests/RetakeV4.Integration.Tests/Modules/ModuleCatalogTests.cs`

**Interfaces:**
- Consumes: `RetakeApiService`, `RetakeApi.Capability` (Task 2) ; `LastAliveTracker`, `TeamCount` (Task 1).
- Produces:
  - `public static class ModuleCatalog { IReadOnlyList<IRetakeModule> CreateAll() }` (ordre : Core, Hud, RoundTypes, Teams, Spawns, Allocation, Plant, InstaDefuse, Admin, Links, Api).
  - `ApiConfig : ModuleConfig { Version 1 }`, `ApiModule` (`Name = "Api"`, dépend de `Core`, `api.json`).
  - Package dev : `addons/counterstrikesharp/shared/RetakeV4.Contracts/RetakeV4.Contracts.dll`.

- [ ] **Step 1: Écrire le test qui échoue**

`tests/RetakeV4.Integration.Tests/Modules/ModuleCatalogTests.cs`
```csharp
using RetakeV4.Modules;

namespace RetakeV4.Integration.Tests.Modules;

public class ModuleCatalogTests
{
    [Fact]
    public void Catalog_ListsEveryModuleOnce()
    {
        var names = ModuleCatalog.CreateAll().Select(m => m.Name).ToList();
        Assert.Equal(
            new[] { "Core", "Hud", "RoundTypes", "Teams", "Spawns", "Allocation", "Plant", "InstaDefuse", "Admin", "Links", "Api" },
            names);
    }

    [Fact]
    public void Catalog_CreatesFreshInstances() =>
        Assert.NotSame(ModuleCatalog.CreateAll()[0], ModuleCatalog.CreateAll()[0]);
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`ModuleCatalog` introuvable).

- [ ] **Step 2: Implémentation**

`src/RetakeV4/Modules/Api/ApiConfig.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Api;

public sealed record ApiConfig : ModuleConfig
{
    public ApiConfig() => Version = 1;
}
```

`src/RetakeV4/Modules/Api/ApiModule.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Contracts;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Modules.Api;

// Exposes IRetakeApi to other plugins through the "retakev4:api" capability.
public sealed class ApiModule : IRetakeModule
{
    private ApiConfig _config = new();
    private ModuleContext? _context;
    private RetakeApiService? _service;
    private LastAliveTracker _lastAlive = LastAliveTracker.Empty;

    public string Name => "Api";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("api.json", new ApiConfig());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var service = new RetakeApiService(context.Bus, context.Logger, SlotOf, SteamIdOf);
        _service = service;
        Register(context.Logger);
        var hooks = context.Hooks;
        hooks.OnBus<RoundPhaseChanged>(service.OnPhase);
        hooks.OnBus<RoundPrepared>(service.OnRoundPrepared);
        hooks.OnBus<BombPlanted>(service.OnBombPlanted);
        hooks.OnBus<LoadoutsAssigned>(service.OnLoadouts);
        hooks.OnBus<TeamStateChanged>(service.OnTeams);
        hooks.OnEvent<EventRoundStart>("api_round_start", _ => _lastAlive = LastAliveTracker.Empty);
        hooks.OnEvent<EventPlayerDeath>("api_player_death", _ =>
            Server.NextFrame(() => _context?.Guard.Run(Name, "api_last_alive", CheckLastAlive)));
        hooks.OnEvent<EventRoundEnd>("api_round_end", e => service.OnRoundEnded(context.Rounds.State.RoundNumber, WinnerOf(e.Winner)));
    }

    public void Unload()
    {
        _service = null;
        _context = null;
    }

    private void Register(ILogger logger)
    {
        try
        {
            Capabilities.RegisterPluginCapability(RetakeApi.Capability, () => _service);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(ex, "The retakev4:api capability could not be registered");
        }
    }

    // The death is processed on the next frame, once the victim is no longer counted as alive.
    private void CheckLastAlive()
    {
        if (_service is not { } service || _context?.Rounds.State.Phase != RoundPhase.Live)
        {
            return;
        }
        var players = PlayerQueries.Humans()
            .Select(p => (Player: p, Side: PlayerQueries.SideOf(p)))
            .Where(p => p.Side is not null)
            .ToList();
        TeamCount Count(TeamSide side) =>
            new(players.Count(p => p.Side == side && p.Player.PawnIsAlive), players.Count(p => p.Side == side));
        var (tracker, newly) = _lastAlive.Update(Count(TeamSide.T), Count(TeamSide.CT));
        _lastAlive = tracker;
        foreach (var side in newly)
        {
            service.OnLastAlive(side, players.First(p => p.Side == side && p.Player.PawnIsAlive).Player.Slot);
        }
    }

    private static TeamSide? WinnerOf(int winner) => (CsTeam)winner switch
    {
        CsTeam.Terrorist => TeamSide.T,
        CsTeam.CounterTerrorist => TeamSide.CT,
        _ => null,
    };

    private static int? SlotOf(ulong steamId) => PlayerQueries.Humans().FirstOrDefault(p => p.SteamID == steamId)?.Slot;

    private static ulong SteamIdOf(int slot) => Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player ? player.SteamID : 0UL;
}
```

`src/RetakeV4/Modules/ModuleCatalog.cs`
```csharp
using RetakeV4.Modules.Admin;
using RetakeV4.Modules.Allocation;
using RetakeV4.Modules.Api;
using RetakeV4.Modules.Core;
using RetakeV4.Modules.Hud;
using RetakeV4.Modules.InstaDefuse;
using RetakeV4.Modules.Links;
using RetakeV4.Modules.Plant;
using RetakeV4.Modules.RoundTypes;
using RetakeV4.Modules.Spawns;
using RetakeV4.Modules.Teams;

namespace RetakeV4.Modules;

// Every module of the plugin, shared by the plugin itself and the default-config export.
public static class ModuleCatalog
{
    public static IReadOnlyList<IRetakeModule> CreateAll() => new IRetakeModule[]
    {
        new CoreModule(),
        new HudModule(),
        new RoundTypesModule(),
        new TeamsModule(),
        new SpawnsModule(),
        new AllocationModule(),
        new PlantModule(),
        new InstaDefuseModule(),
        new AdminModule(),
        new LinksModule(),
        new ApiModule(),
    };
}
```

Dans `src/RetakeV4/RetakeV4Plugin.cs` : remplacer `CreateModules()` par `ModuleCatalog.CreateAll()` dans `Load`, supprimer la méthode `CreateModules` et les `using RetakeV4.Modules.<Module>;` devenus inutiles (garder `using RetakeV4.Modules;`).

Dans `scripts/package-dev.ps1`, avant `$cfgDir = ...`, ajouter :
```powershell
$sharedDir = Join-Path $out "addons/counterstrikesharp/shared/RetakeV4.Contracts"
New-Item -ItemType Directory -Force $sharedDir | Out-Null
$contracts = Join-Path $root "src/RetakeV4.Contracts/bin/$Configuration/net10.0/RetakeV4.Contracts.dll"
if (-not (Test-Path $contracts)) { throw "Missing build output: RetakeV4.Contracts.dll" }
Copy-Item $contracts $sharedDir
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
puis (outil PowerShell) `pwsh -NoProfile -File scripts/package-dev.ps1` et vérifier `artifacts/dev/addons/counterstrikesharp/shared/RetakeV4.Contracts/RetakeV4.Contracts.dll` présent et `artifacts/dev/addons/counterstrikesharp/plugins/RetakeV4/RetakeV4.Contracts.dll` absent.
Expected: `0 Avertissement(s)`, `Failed: 0`, package correct.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Modules src/RetakeV4/RetakeV4Plugin.cs scripts/package-dev.ps1 tests/RetakeV4.Integration.Tests/Modules/ModuleCatalogTests.cs
git commit -m "feat: module Api (capacité retakev4:api, dernier vivant, fin de round) et catalogue des modules"
```

---

### Task 4: Export des configs par défaut et version 4.0.0

**Files:**
- Create: `src/RetakeV4/Configuration/ConfigExport.cs`, `tools/RetakeV4.ConfigExporter/RetakeV4.ConfigExporter.csproj`, `tools/RetakeV4.ConfigExporter/Program.cs`
- Modify: `RetakeV4.sln`, `src/RetakeV4/RetakeV4.csproj`, `src/RetakeV4/RetakeV4Plugin.cs`
- Test: `tests/RetakeV4.Integration.Tests/Configuration/ConfigExportTests.cs`

**Interfaces:**
- Consumes: `ModuleCatalog.CreateAll` (Task 3) ; `JsonConfigStore`.
- Produces: `public static class ConfigExport { IReadOnlyList<string> Run(string directory, ILogger logger) }` (noms de fichiers triés) ; outil `dotnet run --project tools/RetakeV4.ConfigExporter -- <dossier>`.

- [ ] **Step 1: Écrire le test qui échoue**

`tests/RetakeV4.Integration.Tests/Configuration/ConfigExportTests.cs`
```csharp
using System.Text.Json;
using RetakeV4.Configuration;

namespace RetakeV4.Integration.Tests.Configuration;

public sealed class ConfigExportTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Export_WritesOneValidJsonPerModuleConfig()
    {
        var files = ConfigExport.Run(_dir.Path, new ListLogger());
        Assert.Equal(
            new[]
            {
                "admin.json", "allocation.json", "api.json", "core.json", "grenades.json", "hud.json", "instadefuse.json",
                "links.json", "plant.json", "roundtypes.json", "spawns.json", "teams.json",
            },
            files);
        Assert.All(files, file => JsonDocument.Parse(File.ReadAllText(_dir.File(file))).Dispose());
    }

    [Fact]
    public void Export_IsIdempotent()
    {
        ConfigExport.Run(_dir.Path, new ListLogger());
        File.WriteAllText(_dir.File("core.json"), "{ \"Version\": 1, \"Debug\": true }");
        ConfigExport.Run(_dir.Path, new ListLogger());
        Assert.Contains("\"Debug\": true", File.ReadAllText(_dir.File("core.json")));
    }
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`ConfigExport` introuvable).

- [ ] **Step 2: Implémentation**

`src/RetakeV4/Configuration/ConfigExport.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Modules;

namespace RetakeV4.Configuration;

// Writes each module's default config exactly as the plugin does on first start (existing files are kept).
public static class ConfigExport
{
    public static IReadOnlyList<string> Run(string directory, ILogger logger)
    {
        Directory.CreateDirectory(directory);
        var store = new JsonConfigStore(directory);
        foreach (var module in ModuleCatalog.CreateAll())
        {
            module.LoadConfig(store, logger);
        }
        return Directory.GetFiles(directory, "*.json")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
```

`tools/RetakeV4.ConfigExporter/RetakeV4.ConfigExporter.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CounterStrikeSharp.API" Version="1.0.370" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RetakeV4\RetakeV4.csproj" />
    <ProjectReference Include="..\..\src\RetakeV4.Contracts\RetakeV4.Contracts.csproj" />
  </ItemGroup>
</Project>
```

`tools/RetakeV4.ConfigExporter/Program.cs`
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using RetakeV4.Configuration;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: RetakeV4.ConfigExporter <output directory>");
    return 1;
}
foreach (var file in ConfigExport.Run(args[0], NullLogger.Instance))
{
    Console.WriteLine(file);
}
return 0;
```

Run: `dotnet sln RetakeV4.sln add tools/RetakeV4.ConfigExporter/RetakeV4.ConfigExporter.csproj --solution-folder tools`

Version : dans `src/RetakeV4/RetakeV4.csproj`, `<Version>4.0.0-alpha.1</Version>` → `<Version>4.0.0</Version>` ; dans `src/RetakeV4/RetakeV4Plugin.cs`, `ModuleVersion => "4.0.0-alpha.1"` → `ModuleVersion => "4.0.0"`.

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo && dotnet run --project tools/RetakeV4.ConfigExporter -c Release -- artifacts/configs-check`
Expected: `0 Avertissement(s)`, `Failed: 0`, l'outil liste les 12 fichiers. Supprimer ensuite `artifacts/configs-check`.

- [ ] **Step 4: Commit**

```bash
git add RetakeV4.sln src/RetakeV4/Configuration/ConfigExport.cs src/RetakeV4/RetakeV4.csproj src/RetakeV4/RetakeV4Plugin.cs tools/RetakeV4.ConfigExporter tests/RetakeV4.Integration.Tests/Configuration/ConfigExportTests.cs
git commit -m "feat: export des configs par défaut (ConfigExporter) et version 4.0.0"
```

---

### Task 5: Zips de release et GitHub Actions

**Files:**
- Create: `scripts/package-release.ps1`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `scripts/package-dev.ps1` (Task 3), `tools/RetakeV4.ConfigExporter` (Task 4).
- Produces: `artifacts/release/RetakeV4-<version>.zip` et `RetakeV4-<version>-no-configs.zip` ; CI sur push/PR ; release sur tag `v*`.

- [ ] **Step 1: Script de release**

`scripts/package-release.ps1`
```powershell
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "package-dev.ps1") -Configuration Release
if ($LASTEXITCODE -ne 0) { throw "Packaging failed" }

$staging = Join-Path $root "artifacts/dev"
$release = Join-Path $root "artifacts/release"
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Force $release | Out-Null

$noConfigs = Join-Path $release "RetakeV4-$Version-no-configs.zip"
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $noConfigs

$configDir = Join-Path $staging "addons/counterstrikesharp/configs/plugins/RetakeV4"
dotnet run --project (Join-Path $root "tools/RetakeV4.ConfigExporter") -c Release -- $configDir
if ($LASTEXITCODE -ne 0) { throw "Config export failed" }
$full = Join-Path $release "RetakeV4-$Version.zip"
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $full

Add-Type -AssemblyName System.IO.Compression.FileSystem
$noConfigEntries = [System.IO.Compression.ZipFile]::OpenRead($noConfigs).Entries.FullName -replace '\\', '/'
$fullEntries = [System.IO.Compression.ZipFile]::OpenRead($full).Entries.FullName -replace '\\', '/'
if ($noConfigEntries | Where-Object { $_ -like "addons/counterstrikesharp/configs/*" }) { throw "The no-configs zip contains configs" }
if (-not ($fullEntries -contains "addons/counterstrikesharp/configs/plugins/RetakeV4/core.json")) { throw "The full zip has no configs" }
if (-not ($fullEntries -contains "addons/counterstrikesharp/shared/RetakeV4.Contracts/RetakeV4.Contracts.dll")) { throw "The zip has no RetakeV4.Contracts" }

Write-Host "Release ready: $full, $noConfigs"
```

- [ ] **Step 2: Workflows**

`.github/workflows/ci.yml`
```yaml
name: CI

on:
  push:
  pull_request:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet build RetakeV4.sln -c Release --nologo
      - run: dotnet test RetakeV4.sln -c Release --nologo --no-build
      - run: dotnet test tests/RetakeV4.Domain.Tests -c Release --nologo -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line
```

`.github/workflows/release.yml`
```yaml
name: Release

on:
  push:
    tags:
      - "v*"

permissions:
  contents: write

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet test RetakeV4.sln -c Release --nologo
      - name: Package
        shell: pwsh
        run: ./scripts/package-release.ps1 -Version ("${{ github.ref_name }}".TrimStart("v"))
      - uses: softprops/action-gh-release@v2
        with:
          files: artifacts/release/*.zip
          generate_release_notes: true
```

- [ ] **Step 3: Vérifier**

Run (outil PowerShell) : `pwsh -NoProfile -File scripts/package-release.ps1 -Version 4.0.0`
Expected: `Release ready: ...RetakeV4-4.0.0.zip, ...RetakeV4-4.0.0-no-configs.zip`, aucune exception de vérification.

- [ ] **Step 4: Commit**

```bash
git add scripts/package-release.ps1 .github/workflows
git commit -m "ci: build, tests et couverture à chaque push, zips de release sur tag v*"
```

---

### Task 6: README, migration depuis V3 et vérification finale

**Files:**
- Create: `README.md`, `docs/MIGRATION-V3.md`
- Modify: `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`

- [ ] **Step 1: README**

`README.md`
~~~markdown
# RetakeV4

Plugin de retake pour Counter-Strike 2 (CounterStrikeSharp), réécriture complète de CS2RetakeV3 : modulaire, configurable en JSON, avec un HUD en jeu.

## Installation
1. Prérequis : [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) **1.0.370** ou plus récent (API 370).
2. Télécharger `RetakeV4-x.y.z.zip` (configs par défaut incluses) ou `RetakeV4-x.y.z-no-configs.zip` (pour une mise à jour sans toucher vos configs) depuis les releases.
3. Décompresser à la racine du serveur (`game/csgo/`) : `addons/counterstrikesharp/plugins/RetakeV4/`, `addons/counterstrikesharp/shared/RetakeV4.Contracts/`, `addons/counterstrikesharp/configs/plugins/RetakeV4/`, `cfg/RetakeV4/retake.cfg`.
4. Redémarrer le serveur. Les configs manquantes sont créées au premier démarrage.

Depuis V3 : voir [docs/MIGRATION-V3.md](docs/MIGRATION-V3.md).

## Fonctionnalités
- Rounds : types de round (Pistol, Mid, FullBuy…) définis dans `roundtypes.json`, en séquence, aléatoires ou fixes ; site choisi au hasard sans longue série, forçage admin.
- Équipes : file d'attente avec priorités VIP, ratio T/CT, rotation après une victoire CT, scramble après une série de victoires T, blocage du changement d'équipe.
- Armes : menu HUD `!guns` (au viseur ou au clavier) ou menu d'achat CS2, préférences sauvegardées (SQLite ou MySQL), AWP pour les volontaires, kits, Zeus, kits de grenades par camp.
- Plant : AutoPlant ou FastPlant. InstaDefuse avec blocages (HE, molotov, feu) et explosion forcée.
- HUD : bloc d'informations (round, file d'attente, alertes) et menus `point_worldtext` configurables (`hud.json`).
- Administration : menu `!retake`, éditeur de spawns en jeu, forçage du site, scramble, intégration CS2-SimpleAdmin, commandes communautaires (`links.json`).
- API publique pour les autres plugins (`RetakeV4.Contracts`).

## Configuration
Un fichier par module dans `addons/counterstrikesharp/configs/plugins/RetakeV4/` (chacun a `Enabled` et `Debug`) :

| Fichier | Contenu |
|---|---|
| `core.json` | cfg exécutée, correctif du warmup infini |
| `roundtypes.json` | types de round, armes, AWP, kits, Zeus, grenades, séquence |
| `teams.json` | joueurs max, ratio, scramble, rotation, priorités VIP |
| `spawns.json` | `MaxSameSiteInRow` |
| `allocation.json` | base de données, mode (`Menu`/`NativeBuy`/`Both`), rappel, ouverture auto du menu |
| `grenades.json` | kits de grenades par pool |
| `plant.json` | `AutoPlant`/`FastPlant` |
| `instadefuse.json` | règles de l'InstaDefuse |
| `hud.json` | thème, widgets, menus (orientation, distance, entrée) |
| `admin.json` | pont CS2-SimpleAdmin |
| `links.json` | commandes communautaires |
| `api.json` | API publique |

Une valeur invalide est remplacée par sa valeur par défaut avec un avertissement dans les logs. Textes : `plugins/RetakeV4/lang/en.json` et `fr.json`.

## Commandes
| Commande | Permission | Rôle |
|---|---|---|
| `!guns` (et alias V3 : `!gun`, `!g`, `!weapons`…) | — | Menu d'armes |
| `!awp` | — | Volontaire AWP oui/non |
| `css_retake_info` | — | Version |
| `!retake`, `!retake edit` | `@retakev4/admin` | Menu admin, éditeur de spawns |
| `css_retake_edit [save\|discard\|exit]` | `@retakev4/admin` | Éditeur de spawns |
| `css_retake_forcesite <A\|B\|off> [once\|sticky]` | `@retakev4/admin` | Forçage du site |
| `css_retake_scramble` | `@retakev4/admin` | Scramble à la fin du round |
| `css_retake_addspawn`, `_delspawn`, `_tpspawn`, `_teleport`, `_savespawns`, `_reloadspawns` | `@retakev4/admin` | Spawns en console |
| `css_retake_import_v3 <chemin>` | `@retakev4/root` | Import des préférences V3 |
| commandes de `links.json` | — | Liens communautaires |

## API pour les autres plugins
Référencer `RetakeV4.Contracts.dll` (sans la copier : elle est dans `shared/`) :
```csharp
using RetakeV4.Contracts;

public override void OnAllPluginsLoaded(bool hotReload)
{
    var retake = RetakeApi.Capability.Get();
    if (retake is null) return;
    retake.LastPlayerAlive += e => Logger.LogInformation("Clutch for {Team}: slot {Slot}", e.Team, e.Player.Slot);
    retake.RoundPrepared += e => Logger.LogInformation("Round {Round}: {Type} on {Site}", e.RoundNumber, e.RoundType, e.Site);
}
```
Événements : `RoundPrepared`, `BombPlanted`, `LoadoutAssigned`, `LastPlayerAlive`, `RoundEnded`, `PlayerQueued`. Actions : `ForceSite`, `RequestScramble`. Les événements arrivent sur le thread de jeu ; une exception dans un abonné est journalisée et n'arrête pas le retake.

## Développement
- Build : `dotnet build RetakeV4.sln -c Release` ; tests : `dotnet test RetakeV4.sln`.
- Package de test : `pwsh scripts/package-dev.ps1` ; release : `pwsh scripts/package-release.ps1 -Version x.y.z` (fait par GitHub Actions sur un tag `vx.y.z`).
- Tests en jeu : `docs/CHECKLIST-INGAME.md`.
~~~

- [ ] **Step 2: Migration**

`docs/MIGRATION-V3.md`
```markdown
# Migrer de CS2RetakeV3 vers RetakeV4

## Étapes
1. Arrêter le serveur et **retirer** `addons/counterstrikesharp/plugins/CS2Retake/` : V3 et V4 ne cohabitent pas.
2. Installer RetakeV4 (voir le README).
3. **Spawns** : copier vos fichiers `CS2Retake/spawns/<map>.json` dans `plugins/RetakeV4/spawns/` (les 11 maps officielles sont déjà fournies). Le format V3 est lu tel quel ; la première sauvegarde depuis l'éditeur écrit le format V4 et garde une copie `<map>.json.v3.bak`.
4. **Préférences d'armes** : avec le serveur démarré, `css_retake_import_v3 <chemin>/CS2Retake/data/CommandAllocator/cs2retake.db` (console serveur ou admin `@retakev4/root`). Base SQLite V3 uniquement.
5. **Permissions** : remplacer `@cs2retake/admin` par `@retakev4/admin` dans `admins.json` ou dans CS2-SimpleAdmin.
6. **Commandes** : `css_retakeXxx` devient `css_retake_xxx` (tableau ci-dessous).
7. Reporter vos réglages dans les nouveaux fichiers de config (tableau ci-dessous).

## Commandes
| V3 | V4 |
|---|---|
| `css_retakeinfo` | `css_retake_info` |
| `css_retakespawn <index>` | `css_retake_tpspawn <numéro>` (numérotation à partir de 1) |
| `css_retakewrite` | `css_retake_savespawns` |
| `css_retakeread` | `css_retake_reloadspawns` |
| `css_retakescramble` | `css_retake_scramble` |
| `css_retaketeleport x y z` | `css_retake_teleport x y z` |
| `css_retakeaddspawn <2\|3> <0\|1>` | `css_retake_addspawn <T\|CT> <A\|B> [plant]` (formes V3 acceptées), dans l'éditeur |
| `!guns` et alias | inchangés |

## Clés de configuration
### `CS2Retake.json`
| Clé V3 | V4 |
|---|---|
| `PlantType` | `plant.json` → `Mode` |
| `SecondsUntilBombPlantedCheck` | `plant.json` → `PlantCheckSeconds` |
| `RoundTypeMode` | `roundtypes.json` → `Mode` |
| `RoundTypeSequence` | `roundtypes.json` → `Sequence` |
| `RoundTypeSpecific` | `roundtypes.json` → `Specific` |
| `Allocator` | `allocation.json` → `Mode` (`Menu`, `NativeBuy` ou `Both`) |
| `MaxPlayers` | `teams.json` → `MaxPlayers` |
| `TeamBalanceRatio` | `teams.json` → `TeamBalanceRatio` |
| `EnableScramble`, `ScrambleAfterSubsequentTerroristRoundWins` | `teams.json` → `ScrambleAfterTWins` (0 = désactivé) |
| `EnableSwitchOnRoundWin` | `teams.json` → `SwitchTeamsOnCtWin` |
| `EnableQueue` | supprimé : la file d'attente est toujours active |
| `SpotAnnouncerEnabled` | supprimé : le site est annoncé dans le chat et le HUD (`hud.json` → `Widgets.RoundInfo`) |
| `EnableThankYouMessage` | supprimé |
| `MessageLanguage` | supprimé : langue par joueur (`!lang`), textes dans `lang/*.json` |
| `InstaDefuseEnabled` | `instadefuse.json` → `Enabled` |
| `InstaDefuseRequireNoTAlive`, `InstaDefuseBlockOnHe`, `InstaDefuseBlockOnMolotov`, `InstaDefuseBlockOnInferno`, `InstaDefuseInfernoDistance`, `InstaDefuseForceExplodeIfNoTime`, `InstaDefuseChatNotification` | `instadefuse.json` → même nom sans le préfixe `InstaDefuse` |
| `EnableDebug` | `Debug` dans chaque fichier de module |

### Allocateur V3 (`CommandAllocator`, `FullBuy`, `Mid`, `Pistol`)
| Clé V3 | V4 |
|---|---|
| `EnableRoundTypePistolMenu`, `…MidMenu`, `…FullBuyMenu` | supprimé : un type de round apparaît dans `!guns` s'il offre au moins deux armes au choix |
| `DefuseKitMode`, `DefuseKitQuota`, `DefuseKitChance` | `roundtypes.json` → `RoundTypes[].DefuseKit` (`Mode`, `Quota`, `Chance`) |
| `PistolDefuseKitChance`, `PistolDefuseKitGuaranteeMinimum` | type `Pistol` → `DefuseKit.Chance`, `DefuseKit.GuaranteeMinimum` |
| `EnableZeus`, `ZeusChance` | `roundtypes.json` → `RoundTypes[].Zeus` (`Enabled`, `Chance`) |
| `DatabaseType`, `ConnectionString` | `allocation.json` → `Database.Type` (`Sqlite`, `MySql`, `None`), `Database.MySqlConnectionString`, `Database.SqliteFile` — PostgreSQL n'est plus géré |
| `HowToMessageDelayInMinutes` | `allocation.json` → `HowToIntervalMinutes` (entier, 0 = désactivé) |
| `HowToMessage` | `lang/*.json` → `allocation.howto.menu` / `.native` / `.both` |
| listes d'armes `FullBuy`, `Mid`, `Pistol` | `roundtypes.json` → `RoundTypes[].Primaries`, `Secondaries` (`T`, `CT`, `Any`) et `Defaults` |
| AWP (chance, nombre) | `roundtypes.json` → `RoundTypes[].Awp` (`Enabled`, `MaxPerTeam`, `MinActivePlayers`, `Chance`) |
```

- [ ] **Step 3: Checklist et CLAUDE.md**

Ajouter à `docs/CHECKLIST-INGAME.md` :
```markdown

## Phase 5b — API publique et release
- [ ] Installer le zip `RetakeV4-4.0.0.zip` sur un serveur propre : le plugin démarre, `css_retake_info` affiche 4.0.0, les configs fournies sont lues sans avertissement.
- [ ] Mise à jour avec `RetakeV4-4.0.0-no-configs.zip` : vos configs existantes ne sont pas touchées.
- [ ] Un plugin de test qui lit `RetakeApi.Capability.Get()` dans `OnAllPluginsLoaded` reçoit `RoundPrepared` (type, site, poseur), `LoadoutAssigned`, `BombPlanted`, `RoundEnded` (vainqueur) et `PlayerQueued`.
- [ ] `LastPlayerAlive` : déclenché une fois quand une équipe de 2+ joueurs n'a plus qu'un vivant ; jamais pour une équipe d'un seul joueur.
- [ ] Un abonné qui lève une exception : avertissement dans les logs, le round continue.
- [ ] `css_plugins reload RetakeV4` : l'API reste disponible (pas d'erreur d'enregistrement bloquante).
```

Dans la section Structure de `CLAUDE.md`, ajouter :
```markdown
- `src/RetakeV4.Contracts` : API publique (`IRetakeApi`, capacité `retakev4:api`), installée dans `shared/`. Toute modification incompatible incrémente `RetakeApi.Version`.
- `tools/RetakeV4.ConfigExporter` : écrit les configs par défaut (`dotnet run --project tools/RetakeV4.ConfigExporter -- <dossier>`).
```
et dans la section Commandes :
```markdown
- Release : `pwsh -NoProfile -File scripts/package-release.ps1 -Version x.y.z` → `artifacts/release/` (fait par GitHub Actions sur un tag `vx.y.z`)
```

- [ ] **Step 4: Vérification finale**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo && dotnet test tests/RetakeV4.Domain.Tests --nologo -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`
puis (outil PowerShell) `pwsh -NoProfile -File scripts/package-release.ps1 -Version 4.0.0`
Expected: 0 warning, `Failed: 0`, couverture Domain ≥ 80 %, `Release ready`.

- [ ] **Step 5: Commit**

```bash
git add README.md docs/MIGRATION-V3.md docs/CHECKLIST-INGAME.md CLAUDE.md
git commit -m "docs: README, guide de migration depuis V3 et checklist en jeu phase 5b"
```
