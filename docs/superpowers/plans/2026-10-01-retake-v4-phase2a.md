# RetakeV4 — Phase 2a Implementation Plan (types de round, spawns, équipes)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rendre le retake jouable côté placement : chaque round a un type (Pistol/Mid/FullBuy…), un site A/B, des joueurs téléportés sur des spawns retake avec un planteur désigné, et des équipes gérées automatiquement (file d'attente avec priorité VIP, ratio, rotation après victoire CT, scramble). Les armes, le plant et InstaDefuse sont la phase 2b.

**Architecture:** Même découpage hexagonal que la phase 1. Toute décision (sélection du type de round, lecture/migration des spawns, choix du site, placement, planificateur d'équipes) est dans `RetakeV4.Domain` et testée. Trois nouveaux modules CSSharp fins (`RoundTypes`, `Spawns`, `Teams`) branchent le Domain sur le jeu. Au préalable, deux dettes de la relecture phase 1 sont soldées : enregistrements CSSharp tracés et libérés par module (`ModuleHooks`), et publication de `MapStarted` seulement après le chargement de tous les modules (`ModulesReady`).

**Tech Stack:** C# / .NET 10, CounterStrikeSharp.API 1.0.370, System.Text.Json, xUnit 2.9.3, coverlet.msbuild 6.0.4.

**Spec:** `docs/superpowers/specs/2026-09-30-retake-v4-design.md` (§3.3, §3.4, §5.1 séquence, §6, §7.1, §7.2)

## Global Constraints

- CounterStrikeSharp.API **1.0.370**, `[MinimumApiVersion(370)]`, `net10.0`, `Nullable` + `TreatWarningsAsErrors` (via `Directory.Build.props`).
- `RetakeV4.Domain` ne référence **jamais** CounterStrikeSharp.
- Fonctions < 50 lignes, fichiers < 800 lignes, imbrication ≤ 4, objets de domaine immuables (records) ; seuls les trackers runtime nommés (modules, `ModuleRegistrations`) ont un état mutable.
- Couverture de `RetakeV4.Domain` ≥ **80 %** : `dotnet test tests/RetakeV4.Domain.Tests -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line` (toujours `-p:`, jamais `/p:` sous Git Bash).
- Textes joueurs uniquement via `lang/*.json` (en + fr synchronisés, clés `module.section.key`) ; logs en anglais avec templates constants.
- Tout handler CSSharp d'un module passe par `ModuleHooks` (gardé par `ModuleGuard` et libéré au déchargement).
- Hasard toujours injecté via `IRandom` (jamais `new Random()` dans le Domain).
- Commits conventionnels, jamais `--no-verify`. Fichiers contenant des apostrophes : les écrire avec l'outil Write, pas en heredoc Bash.

## Décisions de conception (écarts assumés vs V3)

1. **Type de round** : index de séquence = `CCSGameRules.TotalRoundsPlayed` lu **au round_start** (V3 le calculait au round_end, décalé d'un round). Une entrée `Count = -1` signifie « jusqu'à la fin du match » ; séquence épuisée ⇒ on garde le dernier type (V3 gardait le type précédent, même résultat). Plus de dépendance à `mp_maxrounds`.
2. **Ratio** : `T = round(n × TeamBalanceRatio)` borné à `[1, n-1]` (n ≥ 2), `n = 1 ⇒ 0 CT / 1 T` : identique au tableau V3 (la formule `floor` de la spec §6 donnait 3/1 à 4 joueurs, contraire à V3 ; on garde V3).
3. **Pas assez de spawns** : les joueurs en trop gardent le spawn CS2 par défaut (comportement V3) au lieu d'être empilés avec décalage (spec §7.2) — avec `mp_solid_teammates 1`, un empilement bloquerait les joueurs.
4. **Planteur** : un T tiré au hasard reçoit un spawn `CanPlant` du site (V3 : « premier T apparu », non déterministe).
5. **File** : les joueurs non admis restent spectateurs (V3 les envoyait CT puis les renvoyait spectateurs au tick suivant — bug).
6. L'annonce chat de début de round (type + site) est faite par le module Spawns en attendant le widget HUD `RoundInfo` (phase 3).
7. **Fin du warmup** : le scramble a lieu à la transition Warmup → PostRound (premier `round_start` hors warmup), puis `mp_restartgame 1` si des joueurs ont changé d'équipe, pour qu'ils réapparaissent dans leur nouvelle équipe (V3 anticipait via un tick avant la fin du warmup, avec une course possible avec le moteur).

## Review Focus

1. **Déconnexion d'un joueur en jeu ou en file** : il disparaît de l'état, les positions de file se recalculent, le prochain round rééquilibre. Tests : `TeamPlannerJoinTests.Leave_*` (Task 8).
2. **Serveur plein** : un 10e joueur (MaxPlayers 9) reste en file après la fin de round si personne ne part, et sa position lui est annoncée. Test : `TeamPlannerRoundEndTests.FullServer_KeepsQueue` (Task 7).
3. **Map sans fichier de spawns ou nom de map exotique** (`workshop/123/de_x`, `../x`) : spawns CS2 par défaut, aucune exception, aucun accès hors du dossier `spawns/`. Tests : `MapNamesTests` (Task 5).
4. **Hot reload en plein match** : les humains déjà en T/CT sont adoptés comme joueurs actifs (pas de scramble, pas de file). Test : `TeamPlannerJoinTests.Adopt_*` (Task 8).
5. **Plus de joueurs que de spawns sur un site** : les joueurs en trop ne sont pas placés (pas d'empilement), le planteur est quand même désigné. Test : `SpawnSelectorTests.MorePlayersThanSpawns_LeavesExtrasUnplaced` (Task 6).

---

## File Structure

```
src/RetakeV4.Domain/
├─ Common/BombSite.cs, TeamSide.cs, PlayerId.cs, IRandom.cs, RandomExtensions.cs        (Task 2)
├─ Events/RoundEvents.cs  (+ ModulesReady)                                                (Task 1)
├─ Rounds/PreparationContext.cs (étendu), PreparationOrder.cs, DelegatePreparationStep.cs, RoundTracker.cs (Task 3)
├─ RoundTypes/RoundTypeMode.cs, RoundTypeRules.cs, RoundTypeSelector.cs, RoundTypeStep.cs (Task 4)
├─ Spawns/SpawnPoint.cs, SpawnFileFormat.cs, MapNames.cs                                  (Task 5)
├─ Spawns/SiteSelector.cs, SpawnSelector.cs                                               (Task 6)
└─ Teams/TeamRules.cs, TeamState.cs, TeamRatio.cs, TeamPlan.cs, TeamPlanner.RoundEnd.cs, TeamPlanner.Membership.cs (Tasks 7-8)
src/RetakeV4/
├─ Modules/ModuleRegistrations.cs, ModuleHooks.cs, ModuleContext.cs, ModuleHost.cs        (Task 1)
├─ Adapters/GameRulesAccessor.cs, PlayerQueries.cs                                         (Task 3)
├─ Modules/Core/CoreModule.cs                                                             (Tasks 1, 3)
├─ Modules/RoundTypes/RoundTypesConfig.cs, RoundTypesConfigValidator.cs, RoundTypesModule.cs (Task 4)
├─ Modules/Spawns/SpawnsConfig.cs, SpawnsConfigValidator.cs, SpawnsModule.cs               (Task 9)
├─ Modules/Teams/TeamsConfig.cs, TeamsConfigValidator.cs, TeamsModule.cs                   (Task 10)
├─ spawns/*.json (11 maps au format V2)                                                    (Task 9)
└─ lang/en.json, lang/fr.json                                                              (Tasks 9-10)
tools/RetakeV4.SpawnMigrator/                                                              (Task 9)
tests/RetakeV4.Domain.Tests/TestDoubles/FixedRandom.cs + tests par dossier
tests/RetakeV4.Integration.Tests/ (hooks, validateurs, fichiers de spawns livrés)
```

---

### Task 1: Enregistrements tracés par module (`ModuleHooks`) et `ModulesReady`

**Files:**
- Create: `src/RetakeV4/Modules/ModuleRegistrations.cs`, `src/RetakeV4/Modules/ModuleHooks.cs`
- Modify: `src/RetakeV4/Modules/ModuleContext.cs`, `src/RetakeV4/Modules/ModuleHost.cs`, `src/RetakeV4/Modules/Core/CoreModule.cs`, `src/RetakeV4/RetakeV4Plugin.cs`, `src/RetakeV4.Domain/Events/RoundEvents.cs`
- Test: `tests/RetakeV4.Integration.Tests/Modules/ModuleRegistrationsTests.cs`, `tests/RetakeV4.Integration.Tests/Modules/ModuleHostTests.cs`

**Interfaces:**
- Produces:
  - `sealed class ModuleRegistrations(string module, ILogger logger) : IDisposable` avec `void Track(Action undo)`, `int Count`, `Dispose()` (annule en ordre inverse, isole les exceptions, idempotent).
  - `sealed class ModuleHooks(BasePlugin plugin, IEventBus bus, PreparationPipeline pipeline, ModuleGuard guard, string module, ModuleRegistrations registrations)` avec :
    `OnEvent<T>(string stage, Action<T> handler, HookMode mode = HookMode.Post) where T : GameEvent`,
    `OnEventHook<T>(string stage, Func<T, HookResult> handler, HookMode mode) where T : GameEvent`,
    `OnMapStart(string stage, Action<string> handler)`,
    `Command(string name, string description, Action<CCSPlayerController?, CommandInfo> handler)`,
    `CommandListener(string name, Func<CCSPlayerController?, CommandInfo, HookResult> handler, HookMode mode)`,
    `OnBus<T>(Action<T> handler) where T : notnull`,
    `PreparationStep(IPreparationStep step)`.
    Chaque appel est gardé par `ModuleGuard` (fallback `HookResult.Continue`) et tracé pour libération.
  - `sealed record ModuleContext(BasePlugin Plugin, IEventBus Bus, ModuleGuard Guard, ITextService Text, ILogger Logger, RoundTracker Rounds, ModuleHooks Hooks)` (les champs `Preparation` et `HotReload` disparaissent : passer par `Hooks.PreparationStep` et `ModulesReady`).
  - `ModuleHost.Start(JsonConfigStore store, Func<IRetakeModule, ModuleRegistrations, ModuleContext> contextFor)` : crée un `ModuleRegistrations` par module, le libère après `Unload` (arrêt normal) et après un échec de `Load`.
  - Événement Domain `sealed record ModulesReady(bool HotReload)` publié par le plugin après `ModuleHost.Start`. Core démarre la map courante sur cet événement (et non plus dans `Load`).

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/Modules/ModuleRegistrationsTests.cs`
```csharp
using RetakeV4.Modules;

namespace RetakeV4.Integration.Tests.Modules;

public class ModuleRegistrationsTests
{
    private readonly ListLogger _logger = new();

    [Fact]
    public void Dispose_UndoesInReverseOrder()
    {
        var journal = new List<string>();
        var registrations = new ModuleRegistrations("Core", _logger);
        registrations.Track(() => journal.Add("first"));
        registrations.Track(() => journal.Add("second"));
        registrations.Dispose();
        Assert.Equal(new[] { "second", "first" }, journal);
    }

    [Fact]
    public void Dispose_IsolatesFailures_AndLogsThem()
    {
        var journal = new List<string>();
        var registrations = new ModuleRegistrations("Core", _logger);
        registrations.Track(() => journal.Add("first"));
        registrations.Track(() => throw new InvalidOperationException("boom"));
        registrations.Dispose();
        Assert.Equal(new[] { "first" }, journal);
        Assert.Single(_logger.Entries);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var calls = 0;
        var registrations = new ModuleRegistrations("Core", _logger);
        registrations.Track(() => calls++);
        registrations.Dispose();
        registrations.Dispose();
        Assert.Equal(1, calls);
        Assert.Equal(0, registrations.Count);
    }
}
```

Dans `tests/RetakeV4.Integration.Tests/Modules/ModuleHostTests.cs`, remplacer le helper `Start` et ajouter deux tests :
```csharp
    private ModuleHost Start(params IRetakeModule[] modules)
    {
        var host = new ModuleHost(modules, _logger);
        host.Start(new JsonConfigStore(_dir.Path), (module, registrations) =>
        {
            registrations.Track(() => _journal.Add($"release:{module.Name}"));
            return null!;
        });
        return host;
    }

    [Fact]
    public void ModuleFailingToLoad_ReleasesItsRegistrations()
    {
        Start(new FakeModule("Core", _journal, throwOnLoad: true));
        Assert.Equal(new[] { "unload:Core", "release:Core" }, _journal);
    }

    [Fact]
    public void Stop_ReleasesRegistrationsAfterUnload()
    {
        var host = Start(new FakeModule("Core", _journal), new FakeModule("Hud", _journal, deps: new[] { "Core" }));
        _journal.Clear();
        host.Stop();
        Assert.Equal(new[] { "unload:Hud", "release:Hud", "unload:Core", "release:Core" }, _journal);
    }
```
et adapter `Stop_UnloadsInReverseOrder` (supprimé : remplacé par `Stop_ReleasesRegistrationsAfterUnload`) et `ModuleFailingToLoad_IsUnloaded_AndDependentsAreSkipped` (le journal contient désormais aussi `release:Core` ; garder les assertions `Contains("unload:Core")` et `DoesNotContain("load:Hud")`).

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`ModuleRegistrations` introuvable, signature de `Start` incompatible).

- [ ] **Step 3: Implémenter `ModuleRegistrations`**

`src/RetakeV4/Modules/ModuleRegistrations.cs`
```csharp
using Microsoft.Extensions.Logging;

namespace RetakeV4.Modules;

public sealed class ModuleRegistrations : IDisposable
{
    private readonly string _module;
    private readonly ILogger _logger;
    private readonly List<Action> _undo = new();

    public ModuleRegistrations(string module, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(logger);
        _module = module;
        _logger = logger;
    }

    public int Count => _undo.Count;

    public void Track(Action undo)
    {
        ArgumentNullException.ThrowIfNull(undo);
        _undo.Add(undo);
    }

    public void Dispose()
    {
        for (var i = _undo.Count - 1; i >= 0; i--)
        {
            try
            {
                _undo[i]();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module {Module}: failed to release a registration", _module);
            }
        }
        _undo.Clear();
    }
}
```

- [ ] **Step 4: Implémenter `ModuleHooks`**

`src/RetakeV4/Modules/ModuleHooks.cs`
```csharp
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Modules;

public sealed class ModuleHooks
{
    private readonly BasePlugin _plugin;
    private readonly IEventBus _bus;
    private readonly PreparationPipeline _pipeline;
    private readonly ModuleGuard _guard;
    private readonly string _module;
    private readonly ModuleRegistrations _registrations;

    public ModuleHooks(BasePlugin plugin, IEventBus bus, PreparationPipeline pipeline, ModuleGuard guard, string module, ModuleRegistrations registrations)
    {
        _plugin = plugin;
        _bus = bus;
        _pipeline = pipeline;
        _guard = guard;
        _module = module;
        _registrations = registrations;
    }

    public void OnEvent<T>(string stage, Action<T> handler, HookMode mode = HookMode.Post) where T : GameEvent =>
        OnEventHook<T>(stage, e =>
        {
            handler(e);
            return HookResult.Continue;
        }, mode);

    public void OnEventHook<T>(string stage, Func<T, HookResult> handler, HookMode mode) where T : GameEvent
    {
        BasePlugin.GameEventHandler<T> wrapper = (e, _) => _guard.Run(_module, stage, () => handler(e), HookResult.Continue);
        _plugin.RegisterEventHandler(wrapper, mode);
        _registrations.Track(() => _plugin.DeregisterEventHandler(wrapper, mode));
    }

    public void OnMapStart(string stage, Action<string> handler)
    {
        Listeners.OnMapStart wrapper = map => _guard.Run(_module, stage, () => handler(map));
        _plugin.RegisterListener(wrapper);
        _registrations.Track(() => _plugin.RemoveListener(wrapper));
    }

    public void Command(string name, string description, Action<CCSPlayerController?, CommandInfo> handler)
    {
        CommandInfo.CommandCallback wrapper = (player, info) => _guard.Run(_module, $"cmd:{name}", () => handler(player, info));
        _plugin.AddCommand(name, description, wrapper);
        _registrations.Track(() => _plugin.RemoveCommand(name, wrapper));
    }

    public void CommandListener(string name, Func<CCSPlayerController?, CommandInfo, HookResult> handler, HookMode mode)
    {
        CommandInfo.CommandListenerCallback wrapper = (player, info) =>
            _guard.Run(_module, $"listener:{name}", () => handler(player, info), HookResult.Continue);
        _plugin.AddCommandListener(name, wrapper, mode);
        _registrations.Track(() => _plugin.RemoveCommandListener(name, wrapper, mode));
    }

    public void OnBus<T>(Action<T> handler) where T : notnull
    {
        var subscription = _bus.Subscribe<T>(_module, e => _guard.Run(_module, $"bus:{typeof(T).Name}", () => handler(e)));
        _registrations.Track(subscription.Dispose);
    }

    public void PreparationStep(IPreparationStep step)
    {
        var registration = _pipeline.Register(_module, step);
        _registrations.Track(registration.Dispose);
    }
}
```

- [ ] **Step 5: Adapter contexte, hôte, événement et Core**

`src/RetakeV4/Modules/ModuleContext.cs`
```csharp
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;
using RetakeV4.Localization;

namespace RetakeV4.Modules;

public sealed record ModuleContext(
    BasePlugin Plugin,
    IEventBus Bus,
    ModuleGuard Guard,
    ITextService Text,
    ILogger Logger,
    RoundTracker Rounds,
    ModuleHooks Hooks);
```

Dans `src/RetakeV4/Modules/ModuleHost.cs` :
- ajouter le champ `private readonly Dictionary<string, ModuleRegistrations> _registrations = new();`
- `Start(JsonConfigStore store, Func<IRetakeModule, ModuleRegistrations, ModuleContext> contextFor)` passe `contextFor` à `TryLoad` ;
- remplacer `TryLoad` et `Stop` par :
```csharp
    public void Stop()
    {
        foreach (var module in Enumerable.Reverse(_loaded).ToList())
        {
            SafeUnload(module);
            Release(module);
        }
        _loaded.Clear();
    }

    private void TryLoad(IRetakeModule module, Func<IRetakeModule, ModuleRegistrations, ModuleContext> contextFor)
    {
        var failedDependency = module.DependsOn.FirstOrDefault(d => _loaded.All(l => l.Name != d));
        if (failedDependency is not null)
        {
            _logger.LogWarning("Module {Module} not loaded: dependency {Dependency} failed to load", module.Name, failedDependency);
            return;
        }
        var registrations = new ModuleRegistrations(module.Name, _logger);
        _registrations[module.Name] = registrations;
        try
        {
            module.Load(contextFor(module, registrations));
            _loaded.Add(module);
            _logger.LogInformation("Module {Module} loaded", module.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {Module} failed to load and is disabled", module.Name);
            SafeUnload(module);
            Release(module);
        }
    }

    private void Release(IRetakeModule module)
    {
        if (_registrations.Remove(module.Name, out var registrations))
        {
            registrations.Dispose();
        }
    }
```

Dans `src/RetakeV4.Domain/Events/RoundEvents.cs`, ajouter :
```csharp
public sealed record ModulesReady(bool HotReload);
```

Dans `src/RetakeV4/RetakeV4Plugin.cs`, remplacer le démarrage de l'hôte par :
```csharp
        _host = new ModuleHost(CreateModules(), Logger);
        _host.Start(new JsonConfigStore(ConfigDirectory()), (module, registrations) =>
            new ModuleContext(this, bus, guard, text, Logger, rounds,
                new ModuleHooks(this, bus, pipeline, guard, module.Name, registrations)));
        Logger.LogInformation("RetakeV4 {Version} loaded with modules: {Modules}", ModuleVersion, string.Join(", ", _host.LoadedModules));
        bus.Publish(new ModulesReady(hotReload));
```

Dans `src/RetakeV4/Modules/Core/CoreModule.cs`, remplacer `Load`, `RegisterGameHandlers`, `Unload` et `Guarded` par (supprimer le champ `_debugSubscription`) :
```csharp
    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.OnMapStart("map_start", mapName => StartForMap(mapName, false));
        hooks.OnEvent<EventRoundStart>("round_start", _ => OnRoundStart());
        hooks.OnEvent<EventRoundFreezeEnd>("freeze_end", _ => Context.Rounds.Handle(RoundSignal.FreezeEnded));
        hooks.OnEvent<EventRoundEnd>("round_end", _ => Context.Rounds.Handle(RoundSignal.RoundEnded));
        hooks.Command("css_retake_info", "Shows the RetakeV4 version", OnInfoCommand);
        hooks.OnBus<ModulesReady>(e =>
        {
            if (!string.IsNullOrWhiteSpace(Server.MapName))
            {
                StartForMap(Server.MapName, e.HotReload);
            }
        });
        if (_config.Debug)
        {
            hooks.OnBus<RoundPhaseChanged>(e =>
                context.Logger.LogInformation("Round {Round}: {From} -> {To}", e.RoundNumber, e.From, e.To));
        }
    }

    public void Unload()
    {
        _watchdog?.Kill();
        _watchdog = null;
        _context = null;
    }
```

- [ ] **Step 6: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, tous les tests `Failed: 0` (y compris les 3 nouveaux `ModuleRegistrationsTests` et les 2 nouveaux `ModuleHostTests`).

- [ ] **Step 7: Commit**

```bash
git add src/RetakeV4/Modules src/RetakeV4/RetakeV4Plugin.cs src/RetakeV4.Domain/Events/RoundEvents.cs tests/RetakeV4.Integration.Tests/Modules
git commit -m "feat: enregistrements CSSharp tracés par module et événement ModulesReady"
```

---

### Task 2: Types communs du Domain (sites, camps, joueurs, hasard)

**Files:**
- Create: `src/RetakeV4.Domain/Common/BombSite.cs`, `TeamSide.cs`, `PlayerId.cs`, `IRandom.cs`, `RandomExtensions.cs`
- Create: `tests/RetakeV4.Domain.Tests/TestDoubles/FixedRandom.cs`
- Test: `tests/RetakeV4.Domain.Tests/Common/RandomExtensionsTests.cs`

**Interfaces:**
- Produces:
  - `enum BombSite { A, B }`, `enum TeamSide { T, CT }`
  - `readonly record struct PlayerId(int Slot)` (slot du contrôleur CSSharp)
  - `interface IRandom { int Next(int maxExclusive); double NextDouble(); }` — `Next` lève `ArgumentOutOfRangeException` si `maxExclusive < 1`.
  - `sealed class SystemRandom(Random random) : IRandom` avec `static SystemRandom Shared` (sur `Random.Shared`).
  - `static class RandomExtensions { IReadOnlyList<T> Shuffle<T>(this IRandom random, IEnumerable<T> items); T Pick<T>(this IRandom random, IReadOnlyList<T> items); }` — `Shuffle` renvoie une nouvelle liste (Fisher-Yates), `Pick` lève `ArgumentException` sur liste vide.
  - Double de test `FixedRandom(params int[] values)` : `Next` renvoie les valeurs en boucle, bornées à `[0, maxExclusive-1]` ; `NextDouble` renvoie `0.0`.

- [ ] **Step 1: Écrire le double de test et les tests qui échouent**

`tests/RetakeV4.Domain.Tests/TestDoubles/FixedRandom.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Tests.TestDoubles;

public sealed class FixedRandom(params int[] values) : IRandom
{
    private int _index;

    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, 1);
        var value = values.Length == 0 ? 0 : values[_index++ % values.Length];
        return Math.Clamp(value, 0, maxExclusive - 1);
    }

    public double NextDouble() => 0.0;
}
```

`tests/RetakeV4.Domain.Tests/Common/RandomExtensionsTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Common;

public class RandomExtensionsTests
{
    [Fact]
    public void Shuffle_ReturnsPermutation_WithoutMutatingInput()
    {
        var input = new List<int> { 1, 2, 3, 4, 5 };
        var shuffled = new SystemRandom(new Random(7)).Shuffle(input);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, input);
        Assert.Equal(input.Order(), shuffled.Order());
    }

    [Fact]
    public void Shuffle_IsDeterministicForAGivenSeed()
    {
        var first = new SystemRandom(new Random(42)).Shuffle(Enumerable.Range(0, 20));
        var second = new SystemRandom(new Random(42)).Shuffle(Enumerable.Range(0, 20));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Shuffle_WithFixedZeros_RotatesDeterministically()
    {
        Assert.Equal(new[] { 2, 3, 1 }, new FixedRandom(0).Shuffle(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void Shuffle_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(new FixedRandom().Shuffle(Array.Empty<int>()));
    }

    [Fact]
    public void Pick_UsesRandomIndex()
    {
        Assert.Equal("b", new FixedRandom(1).Pick(new[] { "a", "b", "c" }));
    }

    [Fact]
    public void Pick_EmptyList_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FixedRandom().Pick(Array.Empty<string>()));
    }

    [Fact]
    public void SystemRandom_Next_RejectsNonPositiveBound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SystemRandom.Shared.Next(0));
    }

    [Fact]
    public void SystemRandom_StaysInBounds()
    {
        var random = new SystemRandom(new Random(1));
        Assert.All(Enumerable.Range(0, 200), _ => Assert.InRange(random.Next(3), 0, 2));
        Assert.InRange(random.NextDouble(), 0.0, 1.0);
    }
}
```

Le test `Shuffle_WithFixedZeros_RotatesDeterministically` fixe l'algorithme : Fisher-Yates de la fin vers le début avec `j = Next(i + 1)` ; avec des zéros, `[1,2,3]` → i=2 échange 0↔2 `[3,2,1]` → i=1 échange 0↔1 `[2,3,1]`.

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Domain.Common` introuvable).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Common/BombSite.cs`
```csharp
namespace RetakeV4.Domain.Common;

public enum BombSite
{
    A,
    B,
}
```

`src/RetakeV4.Domain/Common/TeamSide.cs`
```csharp
namespace RetakeV4.Domain.Common;

public enum TeamSide
{
    T,
    CT,
}
```

`src/RetakeV4.Domain/Common/PlayerId.cs`
```csharp
namespace RetakeV4.Domain.Common;

public readonly record struct PlayerId(int Slot);
```

`src/RetakeV4.Domain/Common/IRandom.cs`
```csharp
namespace RetakeV4.Domain.Common;

public interface IRandom
{
    int Next(int maxExclusive);

    double NextDouble();
}

public sealed class SystemRandom : IRandom
{
    private readonly Random _random;

    public SystemRandom(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public static SystemRandom Shared { get; } = new(Random.Shared);

    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, 1);
        return _random.Next(maxExclusive);
    }

    public double NextDouble() => _random.NextDouble();
}
```

`src/RetakeV4.Domain/Common/RandomExtensions.cs`
```csharp
namespace RetakeV4.Domain.Common;

public static class RandomExtensions
{
    public static IReadOnlyList<T> Shuffle<T>(this IRandom random, IEnumerable<T> items)
    {
        var copy = items.ToArray();
        for (var i = copy.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    public static T Pick<T>(this IRandom random, IReadOnlyList<T> items)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("Cannot pick from an empty list", nameof(items));
        }
        return items[random.Next(items.Count)];
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Common tests/RetakeV4.Domain.Tests/Common tests/RetakeV4.Domain.Tests/TestDoubles
git commit -m "feat: types communs du Domain (sites, camps, joueurs, hasard injectable)"
```

---

### Task 3: Contexte de préparation enrichi, ordre des étapes et accès gamerules partagé

**Files:**
- Modify: `src/RetakeV4.Domain/Rounds/PreparationContext.cs`, `src/RetakeV4.Domain/Rounds/RoundTracker.cs`
- Create: `src/RetakeV4.Domain/Rounds/PreparationOrder.cs`, `src/RetakeV4.Domain/Rounds/DelegatePreparationStep.cs`
- Create: `src/RetakeV4/Adapters/GameRulesAccessor.cs`, `src/RetakeV4/Adapters/PlayerQueries.cs`
- Delete: `src/RetakeV4/Modules/Core/GameRulesAccessor.cs`
- Modify: `src/RetakeV4/Modules/Core/CoreModule.cs`, `src/RetakeV4/RetakeV4Plugin.cs`
- Test: `tests/RetakeV4.Domain.Tests/Rounds/RoundTrackerTests.cs`, `tests/RetakeV4.Domain.Tests/Rounds/DelegatePreparationStepTests.cs`

**Interfaces:**
- Consumes: `BombSite`, `PlayerId` (Task 2).
- Produces:
  - `sealed record PreparationContext(int RoundNumber)` + propriétés `init` : `int RoundsPlayed`, `string? RoundType`, `BombSite? Site`, `PlayerId? Planter`.
  - `static class PreparationOrder { const int RoundType = 10; const int Site = 20; const int Teams = 30; const int Placement = 40; const int Loadout = 50; }`
  - `sealed class DelegatePreparationStep(string name, int order, Func<PreparationContext, PreparationContext> run) : IPreparationStep`
  - `RoundTracker(IEventBus bus, PreparationPipeline pipeline, RoundState initial, Func<int>? roundsPlayed = null)` : `Prepare` construit `new PreparationContext(n) { RoundsPlayed = roundsPlayed() }` (0 par défaut).
  - `internal static class GameRulesAccessor { CCSGameRules? Get(); bool IsWarmup(); int TotalRoundsPlayed(); }` dans `RetakeV4.Adapters` (`IsWarmup` vaut `true` si les gamerules sont absentes).
  - `internal static class PlayerQueries { IReadOnlyList<CCSPlayerController> Humans(); TeamSide? SideOf(CCSPlayerController); CsTeam ToCsTeam(TeamSide); }`

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `tests/RetakeV4.Domain.Tests/Rounds/RoundTrackerTests.cs` :
```csharp
    [Fact]
    public void Prepare_PassesRoundsPlayedToThePipeline()
    {
        PreparationContext? seen = null;
        _pipeline.Register("Probe", new DelegatePreparationStep("probe", 1, c =>
        {
            seen = c;
            return c;
        }));
        var tracker = new RoundTracker(_bus, _pipeline, new RoundState(RoundPhase.PostRound, 0), () => 7);
        tracker.Handle(RoundSignal.RoundStarted);
        Assert.Equal(7, seen?.RoundsPlayed);
    }
```

`tests/RetakeV4.Domain.Tests/Rounds/DelegatePreparationStepTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class DelegatePreparationStepTests
{
    [Fact]
    public void Execute_DelegatesAndExposesNameAndOrder()
    {
        var step = new DelegatePreparationStep("site", PreparationOrder.Site, c => c with { Site = BombSite.B });
        Assert.Equal("site", step.Name);
        Assert.Equal(20, step.Order);
        Assert.Equal(BombSite.B, step.Execute(new PreparationContext(1)).Site);
    }

    [Fact]
    public void Orders_AreStrictlyIncreasingInPipelineOrder()
    {
        var orders = new[] { PreparationOrder.RoundType, PreparationOrder.Site, PreparationOrder.Teams, PreparationOrder.Placement, PreparationOrder.Loadout };
        Assert.Equal(orders.Order(), orders);
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }

    [Fact]
    public void Constructor_RejectsBlankName()
    {
        Assert.Throws<ArgumentException>(() => new DelegatePreparationStep(" ", 1, c => c));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`DelegatePreparationStep`, `PreparationOrder`, `RoundsPlayed`, `Site` introuvables).

- [ ] **Step 3: Implémentation Domain**

`src/RetakeV4.Domain/Rounds/PreparationContext.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Rounds;

public sealed record PreparationContext(int RoundNumber)
{
    public int RoundsPlayed { get; init; }

    public string? RoundType { get; init; }

    public BombSite? Site { get; init; }

    public PlayerId? Planter { get; init; }
}
```

`src/RetakeV4.Domain/Rounds/PreparationOrder.cs`
```csharp
namespace RetakeV4.Domain.Rounds;

public static class PreparationOrder
{
    public const int RoundType = 10;
    public const int Site = 20;
    public const int Teams = 30;
    public const int Placement = 40;
    public const int Loadout = 50;
}
```

`src/RetakeV4.Domain/Rounds/DelegatePreparationStep.cs`
```csharp
namespace RetakeV4.Domain.Rounds;

public sealed class DelegatePreparationStep : IPreparationStep
{
    private readonly Func<PreparationContext, PreparationContext> _run;

    public DelegatePreparationStep(string name, int order, Func<PreparationContext, PreparationContext> run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(run);
        Name = name;
        Order = order;
        _run = run;
    }

    public string Name { get; }

    public int Order { get; }

    public PreparationContext Execute(PreparationContext context) => _run(context);
}
```

Dans `src/RetakeV4.Domain/Rounds/RoundTracker.cs` :
- ajouter le champ `private readonly Func<int> _roundsPlayed;`
- constructeur : `public RoundTracker(IEventBus bus, PreparationPipeline pipeline, RoundState initial, Func<int>? roundsPlayed = null)` avec `_roundsPlayed = roundsPlayed ?? (() => 0);`
- dans `Prepare` : `var context = _pipeline.Execute(new PreparationContext(roundNumber) { RoundsPlayed = _roundsPlayed() });`

- [ ] **Step 4: Adaptateurs partagés**

`src/RetakeV4/Adapters/GameRulesAccessor.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace RetakeV4.Adapters;

internal static class GameRulesAccessor
{
    public static CCSGameRules? Get() =>
        Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault(proxy => proxy.IsValid)?.GameRules;

    public static bool IsWarmup() => Get()?.WarmupPeriod ?? true;

    public static int TotalRoundsPlayed() => Get()?.TotalRoundsPlayed ?? 0;
}
```

`src/RetakeV4/Adapters/PlayerQueries.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using RetakeV4.Domain.Common;

namespace RetakeV4.Adapters;

internal static class PlayerQueries
{
    public static IReadOnlyList<CCSPlayerController> Humans() =>
        Utilities.GetPlayers()
            .Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false } && p.Connected == PlayerConnectedState.Connected)
            .ToList();

    public static TeamSide? SideOf(CCSPlayerController player) => (CsTeam)player.TeamNum switch
    {
        CsTeam.Terrorist => TeamSide.T,
        CsTeam.CounterTerrorist => TeamSide.CT,
        _ => null,
    };

    public static CsTeam ToCsTeam(TeamSide side) => side == TeamSide.T ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
}
```

Supprimer `src/RetakeV4/Modules/Core/GameRulesAccessor.cs` ; dans `CoreModule.cs` ajouter `using RetakeV4.Adapters;`.

Dans `src/RetakeV4/RetakeV4Plugin.cs` : ajouter `using RetakeV4.Adapters;` et construire le tracker ainsi :
```csharp
        var rounds = new RoundTracker(bus, pipeline, RoundState.Initial, GameRulesAccessor.TotalRoundsPlayed);
```

- [ ] **Step 5: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0` partout.

- [ ] **Step 6: Commit**

```bash
git add -A src/RetakeV4.Domain/Rounds src/RetakeV4/Adapters src/RetakeV4/Modules/Core src/RetakeV4/RetakeV4Plugin.cs tests/RetakeV4.Domain.Tests/Rounds
git commit -m "feat: contexte de préparation enrichi, ordre des étapes et adaptateurs partagés"
```

---

### Task 4: Types de round (Domain + module `RoundTypes`)

**Files:**
- Create: `src/RetakeV4.Domain/RoundTypes/RoundTypeMode.cs`, `RoundTypeRules.cs`, `RoundTypeSelector.cs`, `RoundTypeStep.cs`
- Create: `src/RetakeV4/Modules/RoundTypes/RoundTypesConfig.cs`, `RoundTypesConfigValidator.cs`, `RoundTypesModule.cs`
- Modify: `src/RetakeV4/RetakeV4Plugin.cs` (`CreateModules`)
- Test: `tests/RetakeV4.Domain.Tests/RoundTypes/RoundTypeSelectorTests.cs`, `tests/RetakeV4.Integration.Tests/Modules/RoundTypes/RoundTypesConfigValidatorTests.cs`

**Interfaces:**
- Consumes: `IRandom`, `RandomExtensions.Pick` (Task 2) ; `PreparationContext.RoundsPlayed/RoundType`, `PreparationOrder.RoundType` (Task 3) ; `ModuleHooks.PreparationStep`, `ModuleHooks.OnBus` (Task 1).
- Produces:
  - `enum RoundTypeMode { Sequence, Random, Specific }`
  - `sealed record RoundTypeSequenceEntry(string RoundType, int Count)` (`Count = -1` : jusqu'à la fin du match)
  - `sealed record RoundTypeRules(RoundTypeMode Mode, IReadOnlyList<string> Available, IReadOnlyList<RoundTypeSequenceEntry> Sequence, string Specific)`
  - `static class RoundTypeSelector { string Select(RoundTypeRules rules, int roundsPlayed, IRandom random); }`
  - `sealed class RoundTypeStep(RoundTypeRules rules, IRandom random) : IPreparationStep` (Name `"round_type"`, Order `PreparationOrder.RoundType`)
  - Config `roundtypes.json` : `RoundTypesConfig` (Version 1) avec `Mode` (Sequence), `RoundTypes` (liste d'objets `{ "Name": … }` : Pistol, Mid, FullBuy — la phase 2b y ajoutera les armes), `Sequence` (Pistol×3, Mid×3, FullBuy×-1), `Specific` ("FullBuy") et `RoundTypeRules ToRules()`.
  - Module `RoundTypes` (dépend de `Core`).

- [ ] **Step 1: Écrire les tests Domain qui échouent**

`tests/RetakeV4.Domain.Tests/RoundTypes/RoundTypeSelectorTests.cs`
```csharp
using RetakeV4.Domain.RoundTypes;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.RoundTypes;

public class RoundTypeSelectorTests
{
    private static readonly string[] Available = { "Pistol", "Mid", "FullBuy" };

    private static RoundTypeRules Sequence(params RoundTypeSequenceEntry[] entries) =>
        new(RoundTypeMode.Sequence, Available, entries, "FullBuy");

    private static readonly RoundTypeRules Default = Sequence(
        new("Pistol", 3), new("Mid", 3), new("FullBuy", -1));

    [Theory]
    [InlineData(0, "Pistol")]
    [InlineData(2, "Pistol")]
    [InlineData(3, "Mid")]
    [InlineData(5, "Mid")]
    [InlineData(6, "FullBuy")]
    [InlineData(50, "FullBuy")]
    public void Sequence_FollowsCountsAndOpenEndedEntry(int roundsPlayed, string expected)
    {
        Assert.Equal(expected, RoundTypeSelector.Select(Default, roundsPlayed, new FixedRandom()));
    }

    [Fact]
    public void Sequence_ExhaustedWithoutOpenEntry_KeepsLastType()
    {
        var rules = Sequence(new("Pistol", 2), new("Mid", 1));
        Assert.Equal("Mid", RoundTypeSelector.Select(rules, 3, new FixedRandom()));
        Assert.Equal("Mid", RoundTypeSelector.Select(rules, 40, new FixedRandom()));
    }

    [Fact]
    public void Sequence_EntriesAfterOpenEndedEntryAreUnreachable()
    {
        var rules = Sequence(new("Pistol", -1), new("Mid", 3));
        Assert.Equal("Pistol", RoundTypeSelector.Select(rules, 10, new FixedRandom()));
    }

    [Fact]
    public void Sequence_Empty_UsesSpecific()
    {
        Assert.Equal("FullBuy", RoundTypeSelector.Select(Sequence(), 0, new FixedRandom()));
    }

    [Fact]
    public void NegativeRoundsPlayed_IsTreatedAsZero()
    {
        Assert.Equal("Pistol", RoundTypeSelector.Select(Default, -3, new FixedRandom()));
    }

    [Fact]
    public void Random_PicksFromAvailable()
    {
        var rules = Default with { Mode = RoundTypeMode.Random };
        Assert.Equal("Mid", RoundTypeSelector.Select(rules, 0, new FixedRandom(1)));
    }

    [Fact]
    public void Random_WithNothingAvailable_UsesSpecific()
    {
        var rules = Default with { Mode = RoundTypeMode.Random, Available = Array.Empty<string>() };
        Assert.Equal("FullBuy", RoundTypeSelector.Select(rules, 0, new FixedRandom()));
    }

    [Fact]
    public void Specific_AlwaysReturnsSpecific()
    {
        var rules = Default with { Mode = RoundTypeMode.Specific, Specific = "Mid" };
        Assert.Equal("Mid", RoundTypeSelector.Select(rules, 0, new FixedRandom()));
    }

    [Fact]
    public void Step_WritesRoundTypeIntoContext()
    {
        var step = new RoundTypeStep(Default, new FixedRandom());
        var result = step.Execute(new PreparationContext(4) { RoundsPlayed = 3 });
        Assert.Equal("Mid", result.RoundType);
        Assert.Equal(PreparationOrder.RoundType, step.Order);
        Assert.Equal("round_type", step.Name);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Domain.RoundTypes` introuvable).

- [ ] **Step 3: Implémentation Domain**

`src/RetakeV4.Domain/RoundTypes/RoundTypeMode.cs`
```csharp
namespace RetakeV4.Domain.RoundTypes;

public enum RoundTypeMode
{
    Sequence,
    Random,
    Specific,
}
```

`src/RetakeV4.Domain/RoundTypes/RoundTypeRules.cs`
```csharp
namespace RetakeV4.Domain.RoundTypes;

public sealed record RoundTypeSequenceEntry(string RoundType, int Count);

public sealed record RoundTypeRules(
    RoundTypeMode Mode,
    IReadOnlyList<string> Available,
    IReadOnlyList<RoundTypeSequenceEntry> Sequence,
    string Specific);
```

`src/RetakeV4.Domain/RoundTypes/RoundTypeSelector.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.RoundTypes;

public static class RoundTypeSelector
{
    public static string Select(RoundTypeRules rules, int roundsPlayed, IRandom random) => rules.Mode switch
    {
        RoundTypeMode.Sequence => FromSequence(rules, Math.Max(0, roundsPlayed)),
        RoundTypeMode.Random => rules.Available.Count == 0 ? rules.Specific : random.Pick(rules.Available),
        _ => rules.Specific,
    };

    private static string FromSequence(RoundTypeRules rules, int roundsPlayed)
    {
        var remaining = roundsPlayed;
        foreach (var entry in rules.Sequence)
        {
            if (entry.Count < 0 || remaining < entry.Count)
            {
                return entry.RoundType;
            }
            remaining -= entry.Count;
        }
        return rules.Sequence.Count > 0 ? rules.Sequence[^1].RoundType : rules.Specific;
    }
}
```

`src/RetakeV4.Domain/RoundTypes/RoundTypeStep.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.RoundTypes;

public sealed class RoundTypeStep : IPreparationStep
{
    private readonly RoundTypeRules _rules;
    private readonly IRandom _random;

    public RoundTypeStep(RoundTypeRules rules, IRandom random)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);
        _rules = rules;
        _random = random;
    }

    public string Name => "round_type";

    public int Order => PreparationOrder.RoundType;

    public PreparationContext Execute(PreparationContext context) =>
        context with { RoundType = RoundTypeSelector.Select(_rules, context.RoundsPlayed, _random) };
}
```

- [ ] **Step 4: Vérifier le succès Domain**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Écrire les tests du validateur qui échouent**

`tests/RetakeV4.Integration.Tests/Modules/RoundTypes/RoundTypesConfigValidatorTests.cs`
```csharp
using RetakeV4.Domain.RoundTypes;
using RetakeV4.Modules.RoundTypes;

namespace RetakeV4.Integration.Tests.Modules.RoundTypes;

public class RoundTypesConfigValidatorTests
{
    private static readonly RoundTypesConfig Defaults = new();
    private readonly RoundTypesConfigValidator _validator = new();

    private static RoundTypeDefinitionConfig Def(string name) => new() { Name = name };

    [Fact]
    public void Defaults_AreValid_AndMatchV3Sequence()
    {
        var result = _validator.Validate(Defaults, Defaults, "roundtypes.json");
        Assert.Empty(result.Issues);
        var rules = result.Config.ToRules();
        Assert.Equal(new[] { "Pistol", "Mid", "FullBuy" }, rules.Available);
        Assert.Equal(new RoundTypeSequenceEntry("FullBuy", -1), rules.Sequence[^1]);
        Assert.Equal(RoundTypeMode.Sequence, rules.Mode);
    }

    [Fact]
    public void EmptyRoundTypeList_FallsBackToDefaults()
    {
        var result = _validator.Validate(Defaults with { RoundTypes = Array.Empty<RoundTypeDefinitionConfig>() }, Defaults, "roundtypes.json");
        Assert.Equal(3, result.Config.RoundTypes.Count);
        Assert.Contains(result.Issues, i => i.Key == nameof(RoundTypesConfig.RoundTypes));
    }

    [Fact]
    public void BlankAndDuplicateNames_AreRemoved()
    {
        var config = Defaults with { RoundTypes = new[] { Def("Pistol"), Def(" "), Def("Pistol"), Def("FullBuy"), Def("Mid") } };
        var result = _validator.Validate(config, Defaults, "roundtypes.json");
        Assert.Equal(new[] { "Pistol", "FullBuy", "Mid" }, result.Config.RoundTypes.Select(r => r.Name));
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void SequenceEntries_WithUnknownTypeOrZeroCount_AreRemoved()
    {
        var config = Defaults with
        {
            Sequence = new[] { new RoundTypeSequenceEntry("Pistol", 2), new RoundTypeSequenceEntry("Eco", 3), new RoundTypeSequenceEntry("Mid", 0), new RoundTypeSequenceEntry("FullBuy", -1) },
        };
        var result = _validator.Validate(config, Defaults, "roundtypes.json");
        Assert.Equal(new[] { "Pistol", "FullBuy" }, result.Config.Sequence.Select(e => e.RoundType));
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void UnknownSpecific_FallsBackToFirstAvailable()
    {
        var result = _validator.Validate(Defaults with { Specific = "Eco" }, Defaults, "roundtypes.json");
        Assert.Equal("Pistol", result.Config.Specific);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void CountBelowMinusOne_IsRemoved()
    {
        var config = Defaults with { Sequence = new[] { new RoundTypeSequenceEntry("Pistol", -5), new RoundTypeSequenceEntry("Mid", 2) } };
        var result = _validator.Validate(config, Defaults, "roundtypes.json");
        Assert.Equal(new[] { "Mid" }, result.Config.Sequence.Select(e => e.RoundType));
    }
}
```

- [ ] **Step 6: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Modules.RoundTypes` introuvable).

- [ ] **Step 7: Implémenter config, validateur et module**

`src/RetakeV4/Modules/RoundTypes/RoundTypesConfig.cs`
```csharp
using RetakeV4.Configuration;
using RetakeV4.Domain.RoundTypes;

namespace RetakeV4.Modules.RoundTypes;

public sealed record RoundTypeDefinitionConfig
{
    public string Name { get; init; } = string.Empty;
}

public sealed record RoundTypesConfig : ModuleConfig
{
    public RoundTypesConfig() => Version = 1;

    public RoundTypeMode Mode { get; init; } = RoundTypeMode.Sequence;

    public IReadOnlyList<RoundTypeDefinitionConfig> RoundTypes { get; init; } = new[]
    {
        new RoundTypeDefinitionConfig { Name = "Pistol" },
        new RoundTypeDefinitionConfig { Name = "Mid" },
        new RoundTypeDefinitionConfig { Name = "FullBuy" },
    };

    public IReadOnlyList<RoundTypeSequenceEntry> Sequence { get; init; } = new[]
    {
        new RoundTypeSequenceEntry("Pistol", 3),
        new RoundTypeSequenceEntry("Mid", 3),
        new RoundTypeSequenceEntry("FullBuy", -1),
    };

    public string Specific { get; init; } = "FullBuy";

    public RoundTypeRules ToRules() =>
        new(Mode, RoundTypes.Select(r => r.Name).ToList(), Sequence, Specific);
}
```

`src/RetakeV4/Modules/RoundTypes/RoundTypesConfigValidator.cs`
```csharp
using RetakeV4.Configuration;
using RetakeV4.Domain.RoundTypes;

namespace RetakeV4.Modules.RoundTypes;

public sealed class RoundTypesConfigValidator : IConfigValidator<RoundTypesConfig>
{
    public ValidationResult<RoundTypesConfig> Validate(RoundTypesConfig config, RoundTypesConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var roundTypes = CleanRoundTypes(config.RoundTypes, defaults.RoundTypes, file, issues);
        var names = roundTypes.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        var sequence = CleanSequence(config.Sequence, names, file, issues);
        var specific = names.Contains(config.Specific) ? config.Specific : Fallback(roundTypes[0].Name, config.Specific, file, issues);
        return new ValidationResult<RoundTypesConfig>(
            config with { RoundTypes = roundTypes, Sequence = sequence, Specific = specific }, issues);
    }

    private static IReadOnlyList<RoundTypeDefinitionConfig> CleanRoundTypes(
        IReadOnlyList<RoundTypeDefinitionConfig>? roundTypes, IReadOnlyList<RoundTypeDefinitionConfig> defaults, string file, List<ConfigIssue> issues)
    {
        var kept = new List<RoundTypeDefinitionConfig>();
        foreach (var roundType in roundTypes ?? Array.Empty<RoundTypeDefinitionConfig>())
        {
            if (string.IsNullOrWhiteSpace(roundType.Name) || kept.Any(k => k.Name == roundType.Name))
            {
                issues.Add(new ConfigIssue(file, nameof(RoundTypesConfig.RoundTypes), $"blank or duplicate round type '{roundType.Name}' removed"));
                continue;
            }
            kept.Add(roundType);
        }
        if (kept.Count > 0)
        {
            return kept;
        }
        issues.Add(new ConfigIssue(file, nameof(RoundTypesConfig.RoundTypes), "no valid round type; using defaults"));
        return defaults;
    }

    private static IReadOnlyList<RoundTypeSequenceEntry> CleanSequence(
        IReadOnlyList<RoundTypeSequenceEntry>? sequence, IReadOnlySet<string> names, string file, List<ConfigIssue> issues)
    {
        var kept = new List<RoundTypeSequenceEntry>();
        foreach (var entry in sequence ?? Array.Empty<RoundTypeSequenceEntry>())
        {
            if (!names.Contains(entry.RoundType) || entry.Count == 0 || entry.Count < -1)
            {
                issues.Add(new ConfigIssue(file, nameof(RoundTypesConfig.Sequence), $"entry '{entry.RoundType}' x{entry.Count} removed (unknown type or invalid count)"));
                continue;
            }
            kept.Add(entry);
        }
        return kept;
    }

    private static string Fallback(string replacement, string invalid, string file, List<ConfigIssue> issues)
    {
        issues.Add(new ConfigIssue(file, nameof(RoundTypesConfig.Specific), $"unknown round type '{invalid}'; using '{replacement}'"));
        return replacement;
    }
}
```

`src/RetakeV4/Modules/RoundTypes/RoundTypesModule.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.RoundTypes;

namespace RetakeV4.Modules.RoundTypes;

public sealed class RoundTypesModule : IRetakeModule
{
    private RoundTypesConfig _config = new();

    public string Name => "RoundTypes";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("roundtypes.json", new RoundTypesConfig(), new RoundTypesConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        context.Hooks.PreparationStep(new RoundTypeStep(_config.ToRules(), SystemRandom.Shared));
        if (_config.Debug)
        {
            context.Hooks.OnBus<RoundPrepared>(e =>
                context.Logger.LogInformation("Round {Round}: type {RoundType} (rounds played {Played})", e.Context.RoundNumber, e.Context.RoundType, e.Context.RoundsPlayed));
        }
    }

    public void Unload()
    {
    }
}
```

Dans `src/RetakeV4/RetakeV4Plugin.cs` : `using RetakeV4.Modules.RoundTypes;` et
```csharp
    private static IReadOnlyList<IRetakeModule> CreateModules() => new IRetakeModule[]
    {
        new CoreModule(),
        new RoundTypesModule(),
    };
```

- [ ] **Step 8: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 9: Commit**

```bash
git add src/RetakeV4.Domain/RoundTypes src/RetakeV4/Modules/RoundTypes src/RetakeV4/RetakeV4Plugin.cs tests/RetakeV4.Domain.Tests/RoundTypes tests/RetakeV4.Integration.Tests/Modules/RoundTypes
git commit -m "feat: types de round configurables (Sequence/Random/Specific) et module RoundTypes"
```

---

### Task 5: Modèle et format des fichiers de spawns (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Spawns/SpawnPoint.cs`, `SpawnFileFormat.cs`, `MapNames.cs`
- Test: `tests/RetakeV4.Domain.Tests/Spawns/SpawnFileFormatTests.cs`, `tests/RetakeV4.Domain.Tests/Spawns/MapNamesTests.cs`

**Interfaces:**
- Consumes: `Vec3`, `ViewAngles` (phase 1) ; `TeamSide`, `BombSite` (Task 2).
- Produces:
  - `sealed record SpawnPoint(Guid Id, TeamSide Team, BombSite Site, bool CanPlant, Vec3 Position, ViewAngles Angle)`
  - `sealed record SpawnFileResult(IReadOnlyList<SpawnPoint> Spawns, bool IsLegacyFormat, IReadOnlyList<string> Issues)`
  - `static class SpawnFileFormat { const int CurrentSchemaVersion = 2; SpawnFileResult Parse(string json); string Serialize(string mapName, IReadOnlyList<SpawnPoint> spawns); }` — ne lève jamais d'exception sur un contenu invalide.
  - `static class MapNames { bool IsSafe(string? mapName); }` — `^[A-Za-z0-9_\-]{1,64}$`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Spawns/SpawnFileFormatTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class SpawnFileFormatTests
{
    private const string LegacyMirage = """
        [
          {"SpawnId":"c9616622-dfa0-4de3-b2a0-c7ef9ad372e5","Team":2,"BombSite":0,"IsInBombZone":true,"PositionX":-401.6623,"PositionY":-2394.9688,"PositionZ":-102.42038,"QAngleX":0,"QAngleY":106.08686,"QAngleZ":0},
          {"SpawnId":"44230977-093e-45d5-84a9-8f516dc0c153","Team":3,"BombSite":1,"IsInBombZone":false,"PositionX":1176.5684,"PositionY":-556.2163,"PositionZ":-135.40727,"QAngleX":0,"QAngleY":-92.24271,"QAngleZ":0}
        ]
        """;

    [Fact]
    public void Legacy_IsConverted()
    {
        var result = SpawnFileFormat.Parse(LegacyMirage);
        Assert.True(result.IsLegacyFormat);
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Spawns.Count);
        var t = result.Spawns[0];
        Assert.Equal(Guid.Parse("c9616622-dfa0-4de3-b2a0-c7ef9ad372e5"), t.Id);
        Assert.Equal(TeamSide.T, t.Team);
        Assert.Equal(BombSite.A, t.Site);
        Assert.True(t.CanPlant);
        Assert.Equal(new Vec3(-401.6623f, -2394.9688f, -102.42038f), t.Position);
        Assert.Equal(106.08686f, t.Angle.Yaw, 4);
        Assert.Equal(TeamSide.CT, result.Spawns[1].Team);
        Assert.Equal(BombSite.B, result.Spawns[1].Site);
    }

    [Theory]
    [InlineData("""[{"SpawnId":"c9616622-dfa0-4de3-b2a0-c7ef9ad372e5","Team":5,"BombSite":0}]""")]
    [InlineData("""[{"SpawnId":"c9616622-dfa0-4de3-b2a0-c7ef9ad372e5","Team":2,"BombSite":-1}]""")]
    public void Legacy_InvalidTeamOrSite_IsSkippedWithIssue(string json)
    {
        var result = SpawnFileFormat.Parse(json);
        Assert.Empty(result.Spawns);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void Legacy_EmptyId_GetsANewId()
    {
        var result = SpawnFileFormat.Parse("""[{"SpawnId":"00000000-0000-0000-0000-000000000000","Team":3,"BombSite":0}]""");
        Assert.NotEqual(Guid.Empty, Assert.Single(result.Spawns).Id);
    }

    [Fact]
    public void V2_RoundTrips()
    {
        var spawns = SpawnFileFormat.Parse(LegacyMirage).Spawns;
        var json = SpawnFileFormat.Serialize("de_mirage", spawns);
        var result = SpawnFileFormat.Parse(json);
        Assert.False(result.IsLegacyFormat);
        Assert.Empty(result.Issues);
        Assert.Equal(spawns, result.Spawns);
        Assert.Contains("\"SchemaVersion\": 2", json);
        Assert.Contains("\"Team\": \"CT\"", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("""{ "SchemaVersion": 2, "Map": "x", "Spawns": [ { "Team": "Z" } ] }""")]
    public void InvalidContent_ReturnsEmptyWithIssue(string json)
    {
        var result = SpawnFileFormat.Parse(json);
        Assert.Empty(result.Spawns);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void FutureSchemaVersion_IsRejected()
    {
        var result = SpawnFileFormat.Parse("""{ "SchemaVersion": 3, "Map": "x", "Spawns": [] }""");
        Assert.Empty(result.Spawns);
        Assert.Contains("schema", Assert.Single(result.Issues));
    }

    [Fact]
    public void DuplicateIds_KeepFirst()
    {
        var spawn = SpawnFileFormat.Parse(LegacyMirage).Spawns[0];
        var json = SpawnFileFormat.Serialize("x", new[] { spawn, spawn with { Site = BombSite.B } });
        var result = SpawnFileFormat.Parse(json);
        Assert.Equal(BombSite.A, Assert.Single(result.Spawns).Site);
        Assert.Single(result.Issues);
    }
}
```

`tests/RetakeV4.Domain.Tests/Spawns/MapNamesTests.cs`
```csharp
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class MapNamesTests
{
    [Theory]
    [InlineData("de_mirage")]
    [InlineData("de_ancient_night")]
    [InlineData("cs-office_v2")]
    public void RegularNames_AreSafe(string name) => Assert.True(MapNames.IsSafe(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../server")]
    [InlineData("workshop/123/de_x")]
    [InlineData("de mirage")]
    [InlineData("C:\\maps\\x")]
    public void SuspiciousNames_AreRejected(string? name) => Assert.False(MapNames.IsSafe(name));

    [Fact]
    public void TooLongName_IsRejected() => Assert.False(MapNames.IsSafe(new string('a', 65)));
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Domain.Spawns` introuvable).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Spawns/SpawnPoint.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Spawns;

public sealed record SpawnPoint(Guid Id, TeamSide Team, BombSite Site, bool CanPlant, Vec3 Position, ViewAngles Angle);

public sealed record SpawnFileResult(IReadOnlyList<SpawnPoint> Spawns, bool IsLegacyFormat, IReadOnlyList<string> Issues);
```

`src/RetakeV4.Domain/Spawns/MapNames.cs`
```csharp
using System.Text.RegularExpressions;

namespace RetakeV4.Domain.Spawns;

public static partial class MapNames
{
    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,64}\z")]
    private static partial Regex SafeName();

    public static bool IsSafe(string? mapName) => mapName is not null && SafeName().IsMatch(mapName);
}
```

`src/RetakeV4.Domain/Spawns/SpawnFileFormat.cs`
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Spawns;

public static class SpawnFileFormat
{
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record SpawnFileDto(int SchemaVersion, string Map, List<SpawnPoint>? Spawns);

    private sealed record LegacySpawnDto(
        Guid SpawnId, int Team, int BombSite, bool IsInBombZone,
        float PositionX, float PositionY, float PositionZ, float QAngleX, float QAngleY, float QAngleZ);

    public static SpawnFileResult Parse(string json)
    {
        try
        {
            return json.TrimStart().StartsWith('[') ? ParseLegacy(json) : ParseCurrent(json);
        }
        catch (JsonException ex)
        {
            return Failure($"invalid spawn file ({ex.Message})");
        }
    }

    public static string Serialize(string mapName, IReadOnlyList<SpawnPoint> spawns) =>
        JsonSerializer.Serialize(new SpawnFileDto(CurrentSchemaVersion, mapName, spawns.ToList()), Options);

    private static SpawnFileResult ParseCurrent(string json)
    {
        var dto = JsonSerializer.Deserialize<SpawnFileDto>(json, Options);
        if (dto is null)
        {
            return Failure("spawn file is empty");
        }
        if (dto.SchemaVersion > CurrentSchemaVersion)
        {
            return Failure($"unsupported spawn file schema version {dto.SchemaVersion}");
        }
        var issues = new List<string>();
        return new SpawnFileResult(Deduplicate(dto.Spawns ?? new List<SpawnPoint>(), issues), false, issues);
    }

    private static SpawnFileResult ParseLegacy(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<LegacySpawnDto>>(json, Options) ?? new List<LegacySpawnDto>();
        var issues = new List<string>();
        var spawns = new List<SpawnPoint>();
        for (var i = 0; i < dtos.Count; i++)
        {
            var converted = Convert(dtos[i], i, issues);
            if (converted is not null)
            {
                spawns.Add(converted);
            }
        }
        return new SpawnFileResult(Deduplicate(spawns, issues), true, issues);
    }

    private static SpawnPoint? Convert(LegacySpawnDto dto, int index, List<string> issues)
    {
        TeamSide? team = dto.Team switch { 2 => TeamSide.T, 3 => TeamSide.CT, _ => null };
        BombSite? site = dto.BombSite switch { 0 => BombSite.A, 1 => BombSite.B, _ => null };
        if (team is null || site is null)
        {
            issues.Add($"spawn #{index}: invalid team {dto.Team} or bombsite {dto.BombSite}, skipped");
            return null;
        }
        return new SpawnPoint(
            dto.SpawnId == Guid.Empty ? Guid.NewGuid() : dto.SpawnId,
            team.Value, site.Value, dto.IsInBombZone,
            new Vec3(dto.PositionX, dto.PositionY, dto.PositionZ),
            new ViewAngles(dto.QAngleX, dto.QAngleY, dto.QAngleZ));
    }

    private static IReadOnlyList<SpawnPoint> Deduplicate(IEnumerable<SpawnPoint> spawns, List<string> issues)
    {
        var seen = new HashSet<Guid>();
        var kept = new List<SpawnPoint>();
        foreach (var spawn in spawns)
        {
            if (seen.Add(spawn.Id))
            {
                kept.Add(spawn);
                continue;
            }
            issues.Add($"duplicate spawn id {spawn.Id}, kept the first one");
        }
        return kept;
    }

    private static SpawnFileResult Failure(string issue) =>
        new(Array.Empty<SpawnPoint>(), false, new[] { issue });
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`. Si `Parse("")` lève autre chose qu'une `JsonException` (ex. `ArgumentException`), ajouter le cas vide en tête de `Parse` : `if (string.IsNullOrWhiteSpace(json)) return Failure("spawn file is empty");` et ledger la ruling.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Spawns tests/RetakeV4.Domain.Tests/Spawns
git commit -m "feat: format de fichier de spawns V2 avec lecture et conversion du format V3"
```

---

### Task 6: Choix du site et placement des joueurs (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Spawns/SiteSelector.cs`, `src/RetakeV4.Domain/Spawns/SpawnSelector.cs`
- Test: `tests/RetakeV4.Domain.Tests/Spawns/SiteSelectorTests.cs`, `tests/RetakeV4.Domain.Tests/Spawns/SpawnSelectorTests.cs`

**Interfaces:**
- Consumes: `IRandom`, `Shuffle`, `Pick`, `BombSite`, `TeamSide`, `PlayerId` (Task 2) ; `SpawnPoint` (Task 5).
- Produces:
  - `enum ForceSiteMode { Once, Sticky }`, `sealed record SiteForce(BombSite Site, ForceSiteMode Mode)`
  - `sealed record SiteHistory(BombSite? Last, int Streak) { static SiteHistory Empty }`
  - `sealed record SiteDecision(BombSite Site, SiteHistory History, SiteForce? Force)`
  - `static class SiteSelector { SiteDecision Choose(SiteHistory history, SiteForce? force, int maxSameSiteInRow, IReadOnlyCollection<BombSite> available, IRandom random); }` — `available` vide ⇒ A et B.
  - `sealed record SpawnRequest(PlayerId Player, TeamSide Team)`, `sealed record SpawnAssignment(PlayerId Player, SpawnPoint Spawn)`
  - `sealed record PlacementResult(IReadOnlyList<SpawnAssignment> Assignments, PlayerId? Planter, IReadOnlyList<PlayerId> Unplaced)`
  - `static class SpawnSelector { PlacementResult Place(IReadOnlyList<SpawnRequest> players, IReadOnlyList<SpawnPoint> spawns, BombSite site, IRandom random); }`

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Spawns/SiteSelectorTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Spawns;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Spawns;

public class SiteSelectorTests
{
    private static readonly BombSite[] Both = { BombSite.A, BombSite.B };

    [Fact]
    public void Random_UsesRandomIndexAmongAvailable()
    {
        var decision = SiteSelector.Choose(SiteHistory.Empty, null, 0, Both, new FixedRandom(1));
        Assert.Equal(BombSite.B, decision.Site);
        Assert.Equal(new SiteHistory(BombSite.B, 1), decision.History);
    }

    [Fact]
    public void OnlyOneSiteAvailable_IsAlwaysChosen()
    {
        var decision = SiteSelector.Choose(SiteHistory.Empty, null, 0, new[] { BombSite.B }, new FixedRandom(0));
        Assert.Equal(BombSite.B, decision.Site);
    }

    [Fact]
    public void NoSiteAvailable_FallsBackToBoth()
    {
        var decision = SiteSelector.Choose(SiteHistory.Empty, null, 0, Array.Empty<BombSite>(), new FixedRandom(1));
        Assert.Equal(BombSite.B, decision.Site);
    }

    [Fact]
    public void SameSite_IncrementsStreak()
    {
        var decision = SiteSelector.Choose(new SiteHistory(BombSite.A, 2), null, 0, Both, new FixedRandom(0));
        Assert.Equal(new SiteHistory(BombSite.A, 3), decision.History);
    }

    [Fact]
    public void MaxSameSiteInRow_ForcesTheOtherSite()
    {
        var decision = SiteSelector.Choose(new SiteHistory(BombSite.A, 2), null, 2, Both, new FixedRandom(0));
        Assert.Equal(BombSite.B, decision.Site);
        Assert.Equal(new SiteHistory(BombSite.B, 1), decision.History);
    }

    [Fact]
    public void MaxSameSiteInRow_IsIgnoredWhenOtherSiteUnavailable()
    {
        var decision = SiteSelector.Choose(new SiteHistory(BombSite.A, 5), null, 2, new[] { BombSite.A }, new FixedRandom(0));
        Assert.Equal(BombSite.A, decision.Site);
    }

    [Fact]
    public void ForceOnce_IsConsumed()
    {
        var decision = SiteSelector.Choose(SiteHistory.Empty, new SiteForce(BombSite.B, ForceSiteMode.Once), 0, Both, new FixedRandom(0));
        Assert.Equal(BombSite.B, decision.Site);
        Assert.Null(decision.Force);
    }

    [Fact]
    public void ForceSticky_IsKept_AndBeatsMaxInRow()
    {
        var force = new SiteForce(BombSite.A, ForceSiteMode.Sticky);
        var decision = SiteSelector.Choose(new SiteHistory(BombSite.A, 9), force, 2, Both, new FixedRandom(1));
        Assert.Equal(BombSite.A, decision.Site);
        Assert.Equal(force, decision.Force);
    }
}
```

`tests/RetakeV4.Domain.Tests/Spawns/SpawnSelectorTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Spawns;

public class SpawnSelectorTests
{
    private static SpawnPoint Spawn(TeamSide team, BombSite site, bool canPlant = false) =>
        new(Guid.NewGuid(), team, site, canPlant, new Vec3(0, 0, 0), new ViewAngles(0, 0));

    private static SpawnRequest Player(int slot, TeamSide team) => new(new PlayerId(slot), team);

    [Fact]
    public void EachPlayerGetsADistinctSpawnOfTheirTeamAndSite()
    {
        var spawns = new[]
        {
            Spawn(TeamSide.T, BombSite.A, true), Spawn(TeamSide.T, BombSite.A), Spawn(TeamSide.CT, BombSite.A),
            Spawn(TeamSide.CT, BombSite.A), Spawn(TeamSide.CT, BombSite.B), Spawn(TeamSide.T, BombSite.B, true),
        };
        var players = new[] { Player(1, TeamSide.T), Player(2, TeamSide.T), Player(3, TeamSide.CT), Player(4, TeamSide.CT) };
        var result = SpawnSelector.Place(players, spawns, BombSite.A, new SystemRandom(new Random(3)));
        Assert.Equal(4, result.Assignments.Count);
        Assert.Equal(4, result.Assignments.Select(a => a.Spawn.Id).Distinct().Count());
        Assert.All(result.Assignments, a => Assert.Equal(BombSite.A, a.Spawn.Site));
        Assert.All(result.Assignments, a => Assert.Equal(players.Single(p => p.Player == a.Player).Team, a.Spawn.Team));
        Assert.Empty(result.Unplaced);
    }

    [Fact]
    public void Planter_IsATerrorist_OnACanPlantSpawn()
    {
        var spawns = new[] { Spawn(TeamSide.T, BombSite.A), Spawn(TeamSide.T, BombSite.A, true), Spawn(TeamSide.CT, BombSite.A) };
        var players = new[] { Player(1, TeamSide.CT), Player(2, TeamSide.T), Player(3, TeamSide.T) };
        var result = SpawnSelector.Place(players, spawns, BombSite.A, new FixedRandom(1));
        Assert.Equal(new PlayerId(3), result.Planter);
        Assert.True(result.Assignments.Single(a => a.Player == new PlayerId(3)).Spawn.CanPlant);
    }

    [Fact]
    public void Planter_FallsBackToAnyTerroristSpawn()
    {
        var spawns = new[] { Spawn(TeamSide.T, BombSite.A) };
        var result = SpawnSelector.Place(new[] { Player(1, TeamSide.T) }, spawns, BombSite.A, new FixedRandom(0));
        Assert.Equal(new PlayerId(1), result.Planter);
        Assert.Single(result.Assignments);
    }

    [Fact]
    public void NoTerrorist_MeansNoPlanter()
    {
        var result = SpawnSelector.Place(new[] { Player(1, TeamSide.CT) }, new[] { Spawn(TeamSide.CT, BombSite.A) }, BombSite.A, new FixedRandom(0));
        Assert.Null(result.Planter);
    }

    [Fact]
    public void MorePlayersThanSpawns_LeavesExtrasUnplaced()
    {
        var spawns = new[] { Spawn(TeamSide.T, BombSite.A, true), Spawn(TeamSide.CT, BombSite.A) };
        var players = new[] { Player(1, TeamSide.T), Player(2, TeamSide.T), Player(3, TeamSide.CT), Player(4, TeamSide.CT) };
        var result = SpawnSelector.Place(players, spawns, BombSite.A, new FixedRandom(0));
        Assert.Equal(2, result.Assignments.Count);
        Assert.Equal(2, result.Unplaced.Count);
        Assert.NotNull(result.Planter);
        Assert.Contains(result.Assignments, a => a.Player == result.Planter);
    }

    [Fact]
    public void SpawnsOfTheOtherSite_AreIgnored()
    {
        var spawns = new[] { Spawn(TeamSide.T, BombSite.B, true) };
        var result = SpawnSelector.Place(new[] { Player(1, TeamSide.T) }, spawns, BombSite.A, new FixedRandom(0));
        Assert.Empty(result.Assignments);
        Assert.Equal(new[] { new PlayerId(1) }, result.Unplaced);
        Assert.Equal(new PlayerId(1), result.Planter);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`SiteSelector`, `SpawnSelector` introuvables).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Spawns/SiteSelector.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Spawns;

public enum ForceSiteMode
{
    Once,
    Sticky,
}

public sealed record SiteForce(BombSite Site, ForceSiteMode Mode);

public sealed record SiteHistory(BombSite? Last, int Streak)
{
    public static SiteHistory Empty { get; } = new(null, 0);
}

public sealed record SiteDecision(BombSite Site, SiteHistory History, SiteForce? Force);

public static class SiteSelector
{
    private static readonly BombSite[] BothSites = { BombSite.A, BombSite.B };

    public static SiteDecision Choose(SiteHistory history, SiteForce? force, int maxSameSiteInRow, IReadOnlyCollection<BombSite> available, IRandom random)
    {
        if (force is not null)
        {
            return new SiteDecision(force.Site, Next(history, force.Site), force.Mode == ForceSiteMode.Sticky ? force : null);
        }
        var candidates = available.Count > 0 ? available.Distinct().ToList() : BothSites.ToList();
        if (maxSameSiteInRow > 0 && history.Last is { } last && history.Streak >= maxSameSiteInRow && candidates.Count > 1)
        {
            candidates.Remove(last);
        }
        var site = random.Pick(candidates);
        return new SiteDecision(site, Next(history, site), null);
    }

    private static SiteHistory Next(SiteHistory history, BombSite site) =>
        history.Last == site ? history with { Streak = history.Streak + 1 } : new SiteHistory(site, 1);
}
```

`src/RetakeV4.Domain/Spawns/SpawnSelector.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Spawns;

public sealed record SpawnRequest(PlayerId Player, TeamSide Team);

public sealed record SpawnAssignment(PlayerId Player, SpawnPoint Spawn);

public sealed record PlacementResult(IReadOnlyList<SpawnAssignment> Assignments, PlayerId? Planter, IReadOnlyList<PlayerId> Unplaced);

public static class SpawnSelector
{
    public static PlacementResult Place(IReadOnlyList<SpawnRequest> players, IReadOnlyList<SpawnPoint> spawns, BombSite site, IRandom random)
    {
        var siteSpawns = spawns.Where(s => s.Site == site).ToList();
        var terrorists = players.Where(p => p.Team == TeamSide.T).ToList();
        PlayerId? planter = terrorists.Count > 0 ? random.Pick(terrorists).Player : null;
        var assignments = new List<SpawnAssignment>();
        var unplaced = new List<PlayerId>();
        var used = new HashSet<Guid>();

        if (planter is { } planterId)
        {
            var planterSpawn = PickPlanterSpawn(siteSpawns, random);
            AddOrMark(planterId, planterSpawn, assignments, unplaced, used);
        }
        foreach (var request in random.Shuffle(players.Where(p => p.Player != planter)))
        {
            var free = random.Shuffle(siteSpawns.Where(s => s.Team == request.Team && !used.Contains(s.Id)));
            AddOrMark(request.Player, free.Count > 0 ? free[0] : null, assignments, unplaced, used);
        }
        return new PlacementResult(assignments, planter, unplaced);
    }

    private static SpawnPoint? PickPlanterSpawn(IReadOnlyList<SpawnPoint> siteSpawns, IRandom random)
    {
        var plantable = siteSpawns.Where(s => s.Team == TeamSide.T && s.CanPlant).ToList();
        var pool = plantable.Count > 0 ? plantable : siteSpawns.Where(s => s.Team == TeamSide.T).ToList();
        return pool.Count > 0 ? random.Pick(pool) : null;
    }

    private static void AddOrMark(PlayerId player, SpawnPoint? spawn, List<SpawnAssignment> assignments, List<PlayerId> unplaced, HashSet<Guid> used)
    {
        if (spawn is null)
        {
            unplaced.Add(player);
            return;
        }
        used.Add(spawn.Id);
        assignments.Add(new SpawnAssignment(player, spawn));
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Spawns/SiteSelector.cs src/RetakeV4.Domain/Spawns/SpawnSelector.cs tests/RetakeV4.Domain.Tests/Spawns/SiteSelectorTests.cs tests/RetakeV4.Domain.Tests/Spawns/SpawnSelectorTests.cs
git commit -m "feat: choix du site et placement des joueurs avec planteur (Domain)"
```

---

### Task 7: Planificateur d'équipes — fin de round (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Teams/TeamRules.cs`, `TeamState.cs`, `TeamRatio.cs`, `TeamPlan.cs`, `TeamPlanner.RoundEnd.cs`
- Test: `tests/RetakeV4.Domain.Tests/Teams/TeamRatioTests.cs`, `tests/RetakeV4.Domain.Tests/Teams/TeamPlannerRoundEndTests.cs`

**Interfaces:**
- Consumes: `IRandom`, `Shuffle`, `PlayerId`, `TeamSide` (Task 2).
- Produces:
  - `sealed record TeamRules(int MaxPlayers, double TBalanceRatio, int ScrambleAfterTWins, bool SwitchTeamsOnCtWin)`
  - `sealed record QueuedPlayer(PlayerId Player, int Priority, long Ticket)`
  - `sealed record TeamState(IReadOnlyList<PlayerId> Ct, IReadOnlyList<PlayerId> T, IReadOnlyList<QueuedPlayer> Queue, int TWinStreak, long NextTicket)` avec `static Empty`, `int PlayingCount`, `TeamSide? SideOf(PlayerId)`, `bool IsQueued(PlayerId)`, `IReadOnlyList<QueuedPlayer> OrderedQueue()` (priorité décroissante puis ticket croissant), `int? QueuePosition(PlayerId)` (1-based).
  - `static class TeamRatio { (int Ct, int T) Compute(int total, double tRatio); }`
  - `enum RoundWinner { None, T, CT }`, `enum MoveReason { SwitchedAfterCtWin, EnteredFromQueue, Scrambled, Balanced }`
  - `sealed record TeamMove(PlayerId Player, TeamSide To, MoveReason Reason)`, `sealed record TeamPlan(TeamState State, IReadOnlyList<TeamMove> Moves, bool Scrambled)`
  - `static partial class TeamPlanner { TeamPlan PlanRoundEnd(TeamState state, RoundWinner winner, bool scrambleRequested, TeamRules rules, IRandom random); }`

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Teams/TeamRatioTests.cs`
```csharp
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Teams;

public class TeamRatioTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 2, 1)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(6, 3, 3)]
    [InlineData(7, 4, 3)]
    [InlineData(8, 4, 4)]
    [InlineData(9, 5, 4)]
    public void Compute_MatchesV3Table(int total, int expectedCt, int expectedT)
    {
        Assert.Equal((expectedCt, expectedT), TeamRatio.Compute(total, 0.499));
    }

    [Fact]
    public void Compute_KeepsAtLeastOnePerSide()
    {
        Assert.Equal((1, 1), TeamRatio.Compute(2, 0.01));
        Assert.Equal((1, 1), TeamRatio.Compute(2, 0.99));
    }
}
```

`tests/RetakeV4.Domain.Tests/Teams/TeamPlannerRoundEndTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Teams;

public class TeamPlannerRoundEndTests
{
    private static readonly TeamRules Rules = new(MaxPlayers: 9, TBalanceRatio: 0.499, ScrambleAfterTWins: 5, SwitchTeamsOnCtWin: true);
    private static readonly IRandom Random = new SystemRandom(new Random(11));

    private static PlayerId[] Ids(params int[] slots) => slots.Select(s => new PlayerId(s)).ToArray();

    private static TeamState State(int[] ct, int[] t, params QueuedPlayer[] queue) =>
        TeamState.Empty with { Ct = Ids(ct), T = Ids(t), Queue = queue, NextTicket = queue.Length };

    [Fact]
    public void CtWin_WinnersAttack_LosersDefend()
    {
        var state = State(new[] { 1, 2, 3, 4, 5 }, new[] { 6, 7, 8, 9 });
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.CT, false, Rules, Random);
        Assert.Equal(4, plan.State.T.Count);
        Assert.Equal(5, plan.State.Ct.Count);
        Assert.All(plan.State.T, p => Assert.Contains(p, state.Ct));
        Assert.All(Ids(6, 7, 8, 9), p => Assert.Contains(p, plan.State.Ct));
        Assert.All(plan.Moves, m => Assert.Equal(MoveReason.SwitchedAfterCtWin, m.Reason));
        Assert.Equal(8, plan.Moves.Count);
    }

    [Fact]
    public void TWin_KeepsTeams_AndIncrementsStreak()
    {
        var state = State(new[] { 1, 2, 3 }, new[] { 4, 5 }) with { TWinStreak = 2 };
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules, Random);
        Assert.Empty(plan.Moves);
        Assert.Equal(3, plan.State.TWinStreak);
        Assert.False(plan.Scrambled);
    }

    [Fact]
    public void QueuedPlayers_EnterAsCt_AndTeamsAreRebalanced()
    {
        var state = State(new[] { 1, 2, 3, 4 }, new[] { 5, 6, 7 }, new QueuedPlayer(new PlayerId(8), 0, 0), new QueuedPlayer(new PlayerId(9), 0, 1));
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules, Random);
        Assert.Equal(4, plan.State.T.Count);
        Assert.Equal(5, plan.State.Ct.Count);
        Assert.Contains(new PlayerId(8), plan.State.Ct);
        Assert.Contains(new PlayerId(9), plan.State.Ct);
        Assert.Empty(plan.State.Queue);
        Assert.Contains(plan.Moves, m => m.Player == new PlayerId(8) && m.Reason == MoveReason.EnteredFromQueue);
        var promoted = Assert.Single(plan.Moves, m => m.Reason == MoveReason.Balanced);
        Assert.Contains(promoted.Player, state.Ct);
        Assert.Equal(TeamSide.T, promoted.To);
    }

    [Fact]
    public void FullServer_KeepsQueue()
    {
        var state = State(new[] { 1, 2, 3, 4, 5 }, new[] { 6, 7, 8, 9 }, new QueuedPlayer(new PlayerId(10), 0, 0));
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules, Random);
        Assert.Equal(new PlayerId(10), Assert.Single(plan.State.Queue).Player);
        Assert.Equal(9, plan.State.PlayingCount);
    }

    [Fact]
    public void Queue_AdmitsHigherPriorityFirst()
    {
        var state = State(new[] { 1, 2, 3, 4 }, new[] { 5, 6, 7, 8 },
            new QueuedPlayer(new PlayerId(10), 0, 0), new QueuedPlayer(new PlayerId(11), 1, 1));
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules, Random);
        Assert.Contains(new PlayerId(11), plan.State.Ct.Concat(plan.State.T));
        Assert.Equal(new PlayerId(10), Assert.Single(plan.State.Queue).Player);
    }

    [Fact]
    public void TWinStreak_TriggersScramble_AndResetsStreak()
    {
        var state = State(new[] { 1, 2, 3 }, new[] { 4, 5 }) with { TWinStreak = 4 };
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules, Random);
        Assert.True(plan.Scrambled);
        Assert.Equal(0, plan.State.TWinStreak);
        Assert.Equal(2, plan.State.T.Count);
        Assert.All(plan.Moves, m => Assert.Equal(MoveReason.Scrambled, m.Reason));
    }

    [Fact]
    public void RequestedScramble_IsApplied()
    {
        var plan = TeamPlanner.PlanRoundEnd(State(new[] { 1, 2 }, new[] { 3 }), RoundWinner.None, true, Rules, Random);
        Assert.True(plan.Scrambled);
    }

    [Fact]
    public void CtWin_ResetsStreak()
    {
        var state = State(new[] { 1, 2 }, new[] { 3 }) with { TWinStreak = 3 };
        Assert.Equal(0, TeamPlanner.PlanRoundEnd(state, RoundWinner.CT, false, Rules, Random).State.TWinStreak);
    }

    [Fact]
    public void CtWin_WithSwitchDisabled_KeepsTeams()
    {
        var plan = TeamPlanner.PlanRoundEnd(State(new[] { 1, 2 }, new[] { 3 }), RoundWinner.CT, false, Rules with { SwitchTeamsOnCtWin = false }, Random);
        Assert.Empty(plan.Moves);
    }

    [Fact]
    public void ScrambleThresholdZero_NeverScramblesAutomatically()
    {
        var state = State(new[] { 1, 2 }, new[] { 3 }) with { TWinStreak = 50 };
        Assert.False(TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules with { ScrambleAfterTWins = 0 }, Random).Scrambled);
    }

    [Fact]
    public void EmptyServer_ProducesEmptyPlan()
    {
        var plan = TeamPlanner.PlanRoundEnd(TeamState.Empty, RoundWinner.None, false, Rules, Random);
        Assert.Empty(plan.Moves);
        Assert.Equal(0, plan.State.PlayingCount);
    }

    [Fact]
    public void QueuePosition_FollowsPriorityThenTicket()
    {
        var state = State(new[] { 1 }, new[] { 2 },
            new QueuedPlayer(new PlayerId(10), 0, 0), new QueuedPlayer(new PlayerId(11), 2, 1), new QueuedPlayer(new PlayerId(12), 0, 2));
        Assert.Equal(1, state.QueuePosition(new PlayerId(11)));
        Assert.Equal(2, state.QueuePosition(new PlayerId(10)));
        Assert.Equal(3, state.QueuePosition(new PlayerId(12)));
        Assert.Null(state.QueuePosition(new PlayerId(1)));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Domain.Teams` introuvable).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Teams/TeamRules.cs`
```csharp
namespace RetakeV4.Domain.Teams;

public sealed record TeamRules(int MaxPlayers, double TBalanceRatio, int ScrambleAfterTWins, bool SwitchTeamsOnCtWin);
```

`src/RetakeV4.Domain/Teams/TeamState.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public sealed record QueuedPlayer(PlayerId Player, int Priority, long Ticket);

public sealed record TeamState(
    IReadOnlyList<PlayerId> Ct,
    IReadOnlyList<PlayerId> T,
    IReadOnlyList<QueuedPlayer> Queue,
    int TWinStreak,
    long NextTicket)
{
    public static TeamState Empty { get; } =
        new(Array.Empty<PlayerId>(), Array.Empty<PlayerId>(), Array.Empty<QueuedPlayer>(), 0, 0);

    public int PlayingCount => Ct.Count + T.Count;

    public TeamSide? SideOf(PlayerId player) =>
        Ct.Contains(player) ? TeamSide.CT : T.Contains(player) ? TeamSide.T : null;

    public bool IsQueued(PlayerId player) => Queue.Any(q => q.Player == player);

    public IReadOnlyList<QueuedPlayer> OrderedQueue() =>
        Queue.OrderByDescending(q => q.Priority).ThenBy(q => q.Ticket).ToList();

    public int? QueuePosition(PlayerId player)
    {
        var index = OrderedQueue().Select(q => q.Player).ToList().IndexOf(player);
        return index < 0 ? null : index + 1;
    }
}
```

`src/RetakeV4.Domain/Teams/TeamRatio.cs`
```csharp
namespace RetakeV4.Domain.Teams;

public static class TeamRatio
{
    public static (int Ct, int T) Compute(int total, double tRatio)
    {
        if (total <= 0)
        {
            return (0, 0);
        }
        if (total == 1)
        {
            return (0, 1);
        }
        var t = (int)Math.Round(total * tRatio, MidpointRounding.AwayFromZero);
        t = Math.Clamp(t, 1, total - 1);
        return (total - t, t);
    }
}
```

`src/RetakeV4.Domain/Teams/TeamPlan.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public enum RoundWinner
{
    None,
    T,
    CT,
}

public enum MoveReason
{
    SwitchedAfterCtWin,
    EnteredFromQueue,
    Scrambled,
    Balanced,
}

public sealed record TeamMove(PlayerId Player, TeamSide To, MoveReason Reason);

public sealed record TeamPlan(TeamState State, IReadOnlyList<TeamMove> Moves, bool Scrambled);
```

`src/RetakeV4.Domain/Teams/TeamPlanner.RoundEnd.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public static partial class TeamPlanner
{
    public static TeamPlan PlanRoundEnd(TeamState state, RoundWinner winner, bool scrambleRequested, TeamRules rules, IRandom random)
    {
        var effectiveWinner = winner == RoundWinner.CT && !rules.SwitchTeamsOnCtWin ? RoundWinner.None : winner;
        var streak = winner switch
        {
            RoundWinner.T => state.TWinStreak + 1,
            RoundWinner.CT => 0,
            _ => state.TWinStreak,
        };
        var scramble = scrambleRequested || (rules.ScrambleAfterTWins > 0 && streak >= rules.ScrambleAfterTWins);
        var (admitted, remainingQueue) = Admit(state, rules.MaxPlayers);
        var everyone = state.Ct.Concat(state.T).Concat(admitted).ToList();
        var (_, tCount) = TeamRatio.Compute(everyone.Count, rules.TBalanceRatio);
        var newT = scramble
            ? random.Shuffle(everyone).Take(tCount).ToList()
            : ChooseTerrorists(state, admitted, effectiveWinner, tCount, random);
        var newCt = everyone.Where(p => !newT.Contains(p)).ToList();
        var moves = BuildMoves(state, newT, newCt, scramble, effectiveWinner);
        var next = new TeamState(newCt, newT, remainingQueue, scramble ? 0 : streak, state.NextTicket);
        return new TeamPlan(next, moves, scramble);
    }

    private static (List<PlayerId> Admitted, List<QueuedPlayer> Remaining) Admit(TeamState state, int maxPlayers)
    {
        var capacity = Math.Max(0, maxPlayers - state.PlayingCount);
        var ordered = state.OrderedQueue();
        return (ordered.Take(capacity).Select(q => q.Player).ToList(), ordered.Skip(capacity).ToList());
    }

    private static List<PlayerId> ChooseTerrorists(TeamState state, IReadOnlyList<PlayerId> admitted, RoundWinner winner, int tCount, IRandom random)
    {
        var (primary, secondary) = winner == RoundWinner.CT ? (state.Ct, state.T) : (state.T, state.Ct);
        return random.Shuffle(primary)
            .Concat(random.Shuffle(secondary))
            .Concat(random.Shuffle(admitted))
            .Take(tCount)
            .ToList();
    }

    private static List<TeamMove> BuildMoves(TeamState state, List<PlayerId> newT, List<PlayerId> newCt, bool scramble, RoundWinner winner)
    {
        var targets = newT.Select(p => (Player: p, Side: TeamSide.T)).Concat(newCt.Select(p => (Player: p, Side: TeamSide.CT)));
        var moves = new List<TeamMove>();
        foreach (var (player, side) in targets)
        {
            var previous = state.SideOf(player);
            if (previous == side)
            {
                continue;
            }
            var reason = previous is null ? MoveReason.EnteredFromQueue
                : scramble ? MoveReason.Scrambled
                : winner == RoundWinner.CT ? MoveReason.SwitchedAfterCtWin
                : MoveReason.Balanced;
            moves.Add(new TeamMove(player, side, reason));
        }
        return moves;
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Teams tests/RetakeV4.Domain.Tests/Teams
git commit -m "feat: planificateur d'équipes de fin de round (ratio, rotation, file prioritaire, scramble)"
```

---

### Task 8: Planificateur d'équipes — arrivées, départs, adoption, réconciliation (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Teams/TeamPlanner.Membership.cs`
- Test: `tests/RetakeV4.Domain.Tests/Teams/TeamPlannerJoinTests.cs`

**Interfaces:**
- Consumes: `TeamState`, `TeamRules`, `TeamRatio`, `TeamMove`, `MoveReason` (Task 7).
- Produces (dans `static partial class TeamPlanner`) :
  - `enum JoinOutcome { JoinedNow, Queued, AlreadyQueued, AlreadyPlaying }`
  - `sealed record JoinResult(TeamState State, JoinOutcome Outcome, TeamSide? Side, int? QueuePosition, bool RestartRound)`
  - `JoinResult RequestJoin(TeamState state, PlayerId player, int priority, bool isWarmup, TeamSide requested, TeamRules rules)` — warmup : rejoint le camp demandé s'il reste de la place ; hors warmup avec moins de 2 joueurs actifs : rejoint le camp manquant et `RestartRound = true` ; sinon file.
  - `TeamState Leave(TeamState state, PlayerId player)`
  - `TeamState Adopt(TeamState state, IReadOnlyList<(PlayerId Player, TeamSide Side)> onTeams, TeamRules rules)` — ajoute les inconnus comme actifs sur leur camp actuel (file au-delà de `MaxPlayers`).
  - `sealed record ReconcileResult(IReadOnlyList<TeamMove> Fixes, IReadOnlyList<PlayerId> ToSpectator)`
  - `ReconcileResult Reconcile(TeamState state, IReadOnlyDictionary<PlayerId, TeamSide?> actual)` — camp réel ≠ camp prévu ⇒ fix (`Balanced`) ; humain en T/CT inconnu de l'état actif ⇒ spectateur ; joueur absent de `actual` ignoré.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Teams/TeamPlannerJoinTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Teams;

public class TeamPlannerJoinTests
{
    private static readonly TeamRules Rules = new(MaxPlayers: 4, TBalanceRatio: 0.499, ScrambleAfterTWins: 5, SwitchTeamsOnCtWin: true);

    private static PlayerId P(int slot) => new(slot);

    private static TeamState State(int[] ct, int[] t) =>
        TeamState.Empty with { Ct = ct.Select(P).ToArray(), T = t.Select(P).ToArray() };

    [Fact]
    public void Warmup_JoinsRequestedSideImmediately()
    {
        var result = TeamPlanner.RequestJoin(State(new[] { 1 }, new[] { 2 }), P(3), 0, isWarmup: true, TeamSide.T, Rules);
        Assert.Equal(JoinOutcome.JoinedNow, result.Outcome);
        Assert.Equal(TeamSide.T, result.Side);
        Assert.Contains(P(3), result.State.T);
        Assert.False(result.RestartRound);
    }

    [Fact]
    public void Warmup_WhenFull_Queues()
    {
        var result = TeamPlanner.RequestJoin(State(new[] { 1, 2 }, new[] { 3, 4 }), P(5), 0, isWarmup: true, TeamSide.CT, Rules);
        Assert.Equal(JoinOutcome.Queued, result.Outcome);
        Assert.Equal(1, result.QueuePosition);
    }

    [Fact]
    public void Live_WithEnoughPlayers_Queues()
    {
        var result = TeamPlanner.RequestJoin(State(new[] { 1 }, new[] { 2 }), P(3), 1, isWarmup: false, TeamSide.CT, Rules);
        Assert.Equal(JoinOutcome.Queued, result.Outcome);
        Assert.Equal(1, result.State.NextTicket);
        Assert.Equal(1, Assert.Single(result.State.Queue).Priority);
    }

    [Fact]
    public void Live_WithFewerThanTwoPlayers_JoinsMissingSideAndRestarts()
    {
        var result = TeamPlanner.RequestJoin(State(new[] { 1 }, Array.Empty<int>()), P(2), 0, isWarmup: false, TeamSide.CT, Rules);
        Assert.Equal(JoinOutcome.JoinedNow, result.Outcome);
        Assert.Equal(TeamSide.T, result.Side);
        Assert.True(result.RestartRound);
    }

    [Fact]
    public void AlreadyPlaying_IsReported()
    {
        var result = TeamPlanner.RequestJoin(State(new[] { 1 }, new[] { 2 }), P(2), 0, isWarmup: false, TeamSide.CT, Rules);
        Assert.Equal(JoinOutcome.AlreadyPlaying, result.Outcome);
        Assert.Equal(TeamSide.T, result.Side);
    }

    [Fact]
    public void AlreadyQueued_IsIdempotent()
    {
        var queued = TeamPlanner.RequestJoin(State(new[] { 1 }, new[] { 2 }), P(3), 0, false, TeamSide.CT, Rules).State;
        var again = TeamPlanner.RequestJoin(queued, P(3), 0, false, TeamSide.T, Rules);
        Assert.Equal(JoinOutcome.AlreadyQueued, again.Outcome);
        Assert.Single(again.State.Queue);
    }

    [Fact]
    public void Leave_RemovesPlayingPlayer()
    {
        var state = TeamPlanner.Leave(State(new[] { 1, 2 }, new[] { 3 }), P(2));
        Assert.DoesNotContain(P(2), state.Ct);
        Assert.Equal(2, state.PlayingCount);
    }

    [Fact]
    public void Leave_RemovesQueuedPlayer_AndShiftsPositions()
    {
        var state = State(new[] { 1 }, new[] { 2 });
        state = TeamPlanner.RequestJoin(state, P(3), 0, false, TeamSide.CT, Rules).State;
        state = TeamPlanner.RequestJoin(state, P(4), 0, false, TeamSide.CT, Rules).State;
        state = TeamPlanner.Leave(state, P(3));
        Assert.Equal(1, state.QueuePosition(P(4)));
    }

    [Fact]
    public void Leave_UnknownPlayer_ChangesNothing()
    {
        var state = State(new[] { 1 }, new[] { 2 });
        Assert.Equal(2, TeamPlanner.Leave(state, P(9)).PlayingCount);
    }

    [Fact]
    public void Adopt_AddsUnknownHumansOnTheirCurrentSide()
    {
        var state = TeamPlanner.Adopt(TeamState.Empty, new[] { (P(1), TeamSide.CT), (P(2), TeamSide.T) }, Rules);
        Assert.Equal(new[] { P(1) }, state.Ct);
        Assert.Equal(new[] { P(2) }, state.T);
    }

    [Fact]
    public void Adopt_QueuesBeyondMaxPlayers_AndIgnoresKnownPlayers()
    {
        var start = State(new[] { 1, 2 }, new[] { 3 });
        var state = TeamPlanner.Adopt(start, new[] { (P(1), TeamSide.T), (P(4), TeamSide.T), (P(5), TeamSide.CT) }, Rules);
        Assert.Equal(4, state.PlayingCount);
        Assert.Contains(P(1), state.Ct);
        Assert.Equal(P(5), Assert.Single(state.Queue).Player);
    }

    [Fact]
    public void Reconcile_FixesWrongSides_AndSpectatesIntruders()
    {
        var state = State(new[] { 1 }, new[] { 2 });
        var actual = new Dictionary<PlayerId, TeamSide?> { [P(1)] = TeamSide.T, [P(2)] = TeamSide.T, [P(3)] = TeamSide.CT, [P(4)] = null };
        var result = TeamPlanner.Reconcile(state, actual);
        var fix = Assert.Single(result.Fixes);
        Assert.Equal(new TeamMove(P(1), TeamSide.CT, MoveReason.Balanced), fix);
        Assert.Equal(new[] { P(3) }, result.ToSpectator);
    }

    [Fact]
    public void Reconcile_IgnoresPlayersMissingFromActual()
    {
        var result = TeamPlanner.Reconcile(State(new[] { 1 }, new[] { 2 }), new Dictionary<PlayerId, TeamSide?>());
        Assert.Empty(result.Fixes);
        Assert.Empty(result.ToSpectator);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`RequestJoin`, `Leave`, `Adopt`, `Reconcile` introuvables).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Teams/TeamPlanner.Membership.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public enum JoinOutcome
{
    JoinedNow,
    Queued,
    AlreadyQueued,
    AlreadyPlaying,
}

public sealed record JoinResult(TeamState State, JoinOutcome Outcome, TeamSide? Side, int? QueuePosition, bool RestartRound);

public sealed record ReconcileResult(IReadOnlyList<TeamMove> Fixes, IReadOnlyList<PlayerId> ToSpectator);

public static partial class TeamPlanner
{
    private const int MinimumPlayersForARound = 2;

    public static JoinResult RequestJoin(TeamState state, PlayerId player, int priority, bool isWarmup, TeamSide requested, TeamRules rules)
    {
        if (state.SideOf(player) is { } side)
        {
            return new JoinResult(state, JoinOutcome.AlreadyPlaying, side, null, false);
        }
        if (state.IsQueued(player))
        {
            return new JoinResult(state, JoinOutcome.AlreadyQueued, null, state.QueuePosition(player), false);
        }
        var hasRoom = state.PlayingCount < rules.MaxPlayers;
        if (hasRoom && isWarmup)
        {
            return new JoinResult(AddTo(state, player, requested), JoinOutcome.JoinedNow, requested, null, false);
        }
        if (hasRoom && state.PlayingCount < MinimumPlayersForARound)
        {
            var needed = SideNeeded(state, rules);
            return new JoinResult(AddTo(state, player, needed), JoinOutcome.JoinedNow, needed, null, true);
        }
        var queued = Enqueue(state, player, priority);
        return new JoinResult(queued, JoinOutcome.Queued, null, queued.QueuePosition(player), false);
    }

    public static TeamState Leave(TeamState state, PlayerId player) => state with
    {
        Ct = state.Ct.Where(p => p != player).ToList(),
        T = state.T.Where(p => p != player).ToList(),
        Queue = state.Queue.Where(q => q.Player != player).ToList(),
    };

    public static TeamState Adopt(TeamState state, IReadOnlyList<(PlayerId Player, TeamSide Side)> onTeams, TeamRules rules)
    {
        var result = state;
        foreach (var (player, side) in onTeams)
        {
            if (result.SideOf(player) is not null || result.IsQueued(player))
            {
                continue;
            }
            result = result.PlayingCount < rules.MaxPlayers ? AddTo(result, player, side) : Enqueue(result, player, 0);
        }
        return result;
    }

    public static ReconcileResult Reconcile(TeamState state, IReadOnlyDictionary<PlayerId, TeamSide?> actual)
    {
        var expected = state.Ct.Select(p => (Player: p, Side: TeamSide.CT)).Concat(state.T.Select(p => (Player: p, Side: TeamSide.T)));
        var fixes = expected
            .Where(e => actual.TryGetValue(e.Player, out var side) && side != e.Side)
            .Select(e => new TeamMove(e.Player, e.Side, MoveReason.Balanced))
            .ToList();
        var intruders = actual
            .Where(a => a.Value is not null && state.SideOf(a.Key) is null)
            .Select(a => a.Key)
            .ToList();
        return new ReconcileResult(fixes, intruders);
    }

    private static TeamSide SideNeeded(TeamState state, TeamRules rules)
    {
        var (_, t) = TeamRatio.Compute(state.PlayingCount + 1, rules.TBalanceRatio);
        return state.T.Count < t ? TeamSide.T : TeamSide.CT;
    }

    private static TeamState AddTo(TeamState state, PlayerId player, TeamSide side) => side == TeamSide.T
        ? state with { T = state.T.Append(player).ToList() }
        : state with { Ct = state.Ct.Append(player).ToList() };

    private static TeamState Enqueue(TeamState state, PlayerId player, int priority) => state with
    {
        Queue = state.Queue.Append(new QueuedPlayer(player, priority, state.NextTicket)).ToList(),
        NextTicket = state.NextTicket + 1,
    };
}
```

- [ ] **Step 4: Vérifier le succès et la couverture**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`
Expected: `Failed: 0` et couverture `RetakeV4.Domain` ≥ 80 %.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Teams/TeamPlanner.Membership.cs tests/RetakeV4.Domain.Tests/Teams/TeamPlannerJoinTests.cs
git commit -m "feat: arrivées, départs, adoption et réconciliation des équipes (Domain)"
```

---

### Task 9: Module `Spawns`, spawns livrés au format V2 et outil de migration

**Files:**
- Create: `tools/RetakeV4.SpawnMigrator/RetakeV4.SpawnMigrator.csproj`, `tools/RetakeV4.SpawnMigrator/Program.cs`
- Create: `src/RetakeV4/spawns/*.json` (11 maps, générés par l'outil)
- Create: `src/RetakeV4/Modules/Spawns/SpawnsConfig.cs`, `SpawnsConfigValidator.cs`, `SpawnsModule.cs`
- Modify: `src/RetakeV4/RetakeV4.csproj` (copie `spawns/`), `src/RetakeV4/RetakeV4Plugin.cs`, `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`, `scripts/package-dev.ps1`
- Test: `tests/RetakeV4.Integration.Tests/Spawns/ShippedSpawnFilesTests.cs`, `tests/RetakeV4.Integration.Tests/Modules/Spawns/SpawnsConfigValidatorTests.cs`

**Interfaces:**
- Consumes: `SpawnFileFormat`, `MapNames`, `SiteSelector`, `SpawnSelector` (Tasks 5-6) ; `PreparationOrder`, `DelegatePreparationStep`, `PreparationContext` (Task 3) ; `PlayerQueries`, `GameRulesAccessor` (Task 3) ; `ModuleHooks` (Task 1) ; `MapStarted`, `RoundPrepared`, `RoundPhaseChanged` (phase 1).
- Produces :
  - Outil `RetakeV4.SpawnMigrator <inputDir> <outputDir>` (convertit tout `*.json` V3/V2 en V2 ; code retour 0 si tout est converti, 2 sinon).
  - Config `spawns.json` : `SpawnsConfig` (Version 1) avec `MaxSameSiteInRow` (0 = désactivé).
  - Module `Spawns` (dépend de `Core`) : charge `<ModuleDirectory>/spawns/<map>.json` sur `MapStarted` ; étape `site` (Order 20) qui remplit `Site` ; étape `placement` (Order 40) qui téléporte et remplit `Planter` ; re-téléporte sur `player_spawn` pendant `Preparing`/`FreezeTime` ; annonce chat `spawns.round.announce` sur `RoundPrepared`.
  - Clé lang : `spawns.round.announce` (`{0}` = type de round, `{1}` = site).

- [ ] **Step 1: Outil de migration**

`tools/RetakeV4.SpawnMigrator/RetakeV4.SpawnMigrator.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RetakeV4.Domain\RetakeV4.Domain.csproj" />
  </ItemGroup>
</Project>
```

`tools/RetakeV4.SpawnMigrator/Program.cs`
```csharp
using RetakeV4.Domain.Spawns;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: RetakeV4.SpawnMigrator <inputDir> <outputDir>");
    return 1;
}

Directory.CreateDirectory(args[1]);
var failures = 0;
foreach (var file in Directory.GetFiles(args[0], "*.json").Order())
{
    var map = Path.GetFileNameWithoutExtension(file);
    var result = SpawnFileFormat.Parse(File.ReadAllText(file));
    foreach (var issue in result.Issues)
    {
        Console.Error.WriteLine($"{map}: {issue}");
    }
    if (result.Spawns.Count == 0)
    {
        failures++;
        continue;
    }
    File.WriteAllText(Path.Combine(args[1], map + ".json"), SpawnFileFormat.Serialize(map, result.Spawns));
    Console.WriteLine($"{map}: {result.Spawns.Count} spawns");
}
return failures == 0 ? 0 : 2;
```

```bash
dotnet sln RetakeV4.sln add tools/RetakeV4.SpawnMigrator/RetakeV4.SpawnMigrator.csproj
dotnet run --project tools/RetakeV4.SpawnMigrator -- "../CS2RetakeV3/CS2Retake-main/CS2Retake-main/CS2Retake/spawns" src/RetakeV4/spawns
```
Expected: 11 lignes `de_xxx: N spawns` (de_mirage: 52), aucune ligne sur stderr, code retour 0.

- [ ] **Step 2: Écrire les tests qui échouent**

Dans `src/RetakeV4/RetakeV4.csproj`, ajouter à l'`ItemGroup` de `lang` :
```xml
    <None Include="spawns\*.json" CopyToOutputDirectory="PreserveNewest" />
```

`tests/RetakeV4.Integration.Tests/Spawns/ShippedSpawnFilesTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Integration.Tests.Spawns;

public class ShippedSpawnFilesTests
{
    private static readonly string SpawnDirectory = Path.Combine(AppContext.BaseDirectory, "spawns");

    public static IEnumerable<object[]> Maps() =>
        new[] { "de_ancient", "de_ancient_night", "de_anubis", "de_cache", "de_dust2", "de_inferno", "de_mirage", "de_nuke", "de_overpass", "de_train", "de_vertigo" }
            .Select(m => new object[] { m });

    [Theory]
    [MemberData(nameof(Maps))]
    public void ShippedFile_IsCurrentFormat_AndCoversBothSites(string map)
    {
        var result = SpawnFileFormat.Parse(File.ReadAllText(Path.Combine(SpawnDirectory, map + ".json")));
        Assert.False(result.IsLegacyFormat);
        Assert.Empty(result.Issues);
        foreach (var site in new[] { BombSite.A, BombSite.B })
        {
            Assert.Contains(result.Spawns, s => s.Site == site && s.Team == TeamSide.CT);
            Assert.Contains(result.Spawns, s => s.Site == site && s.Team == TeamSide.T && s.CanPlant);
        }
    }

    [Fact]
    public void Mirage_KeepsAllSpawnsFromV3()
    {
        var result = SpawnFileFormat.Parse(File.ReadAllText(Path.Combine(SpawnDirectory, "de_mirage.json")));
        Assert.Equal(52, result.Spawns.Count);
    }
}
```

`tests/RetakeV4.Integration.Tests/Modules/Spawns/SpawnsConfigValidatorTests.cs`
```csharp
using RetakeV4.Modules.Spawns;

namespace RetakeV4.Integration.Tests.Modules.Spawns;

public class SpawnsConfigValidatorTests
{
    private static readonly SpawnsConfig Defaults = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = new SpawnsConfigValidator().Validate(Defaults, Defaults, "spawns.json");
        Assert.Empty(result.Issues);
        Assert.Equal(0, result.Config.MaxSameSiteInRow);
    }

    [Fact]
    public void NegativeMaxSameSiteInRow_FallsBackToDefault()
    {
        var result = new SpawnsConfigValidator().Validate(Defaults with { MaxSameSiteInRow = -2 }, Defaults, "spawns.json");
        Assert.Equal(0, result.Config.MaxSameSiteInRow);
        Assert.Single(result.Issues);
    }
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Modules.Spawns` introuvable).

- [ ] **Step 3: Implémenter config, validateur et module**

`src/RetakeV4/Modules/Spawns/SpawnsConfig.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Spawns;

public sealed record SpawnsConfig : ModuleConfig
{
    public SpawnsConfig() => Version = 1;

    public int MaxSameSiteInRow { get; init; }
}
```

`src/RetakeV4/Modules/Spawns/SpawnsConfigValidator.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Spawns;

public sealed class SpawnsConfigValidator : IConfigValidator<SpawnsConfig>
{
    public ValidationResult<SpawnsConfig> Validate(SpawnsConfig config, SpawnsConfig defaults, string file) =>
        config.MaxSameSiteInRow >= 0
            ? new ValidationResult<SpawnsConfig>(config, Array.Empty<ConfigIssue>())
            : new ValidationResult<SpawnsConfig>(
                config with { MaxSameSiteInRow = defaults.MaxSameSiteInRow },
                new[] { new ConfigIssue(file, nameof(SpawnsConfig.MaxSameSiteInRow), "must be >= 0; using default") });
}
```

`src/RetakeV4/Modules/Spawns/SpawnsModule.cs`
```csharp
using System.Collections.Immutable;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Modules.Spawns;

public sealed class SpawnsModule : IRetakeModule
{
    private readonly IRandom _random = SystemRandom.Shared;
    private SpawnsConfig _config = new();
    private ModuleContext? _context;
    private IReadOnlyList<SpawnPoint> _spawns = Array.Empty<SpawnPoint>();
    private SiteHistory _history = SiteHistory.Empty;
    private ImmutableDictionary<int, SpawnPoint> _assignments = ImmutableDictionary<int, SpawnPoint>.Empty;

    public string Name => "Spawns";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Spawns module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("spawns.json", new SpawnsConfig(), new SpawnsConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.OnBus<MapStarted>(e => LoadSpawns(e.MapName));
        hooks.PreparationStep(new DelegatePreparationStep("site", PreparationOrder.Site, ChooseSite));
        hooks.PreparationStep(new DelegatePreparationStep("placement", PreparationOrder.Placement, PlacePlayers));
        hooks.OnEvent<EventPlayerSpawn>("player_spawn", OnPlayerSpawn);
        hooks.OnBus<RoundPhaseChanged>(e =>
        {
            if (e.To == RoundPhase.PostRound)
            {
                _assignments = ImmutableDictionary<int, SpawnPoint>.Empty;
            }
        });
        hooks.OnBus<RoundPrepared>(Announce);
    }

    public void Unload() => _context = null;

    private void LoadSpawns(string mapName)
    {
        _spawns = Array.Empty<SpawnPoint>();
        _history = SiteHistory.Empty;
        if (!MapNames.IsSafe(mapName))
        {
            Context.Logger.LogWarning("Map name {Map} is not a plain file name: default CS2 spawns will be used", mapName);
            return;
        }
        var path = Path.Combine(Context.Plugin.ModuleDirectory, "spawns", mapName + ".json");
        if (!File.Exists(path))
        {
            Context.Logger.LogWarning("No spawn file for {Map} at {Path}: default CS2 spawns will be used", mapName, path);
            return;
        }
        var result = SpawnFileFormat.Parse(File.ReadAllText(path));
        foreach (var issue in result.Issues)
        {
            Context.Logger.LogWarning("Spawn file {Map}: {Issue}", mapName, issue);
        }
        _spawns = result.Spawns;
        Context.Logger.LogInformation("Loaded {Count} spawns for {Map} (legacy format: {Legacy})", _spawns.Count, mapName, result.IsLegacyFormat);
    }

    private PreparationContext ChooseSite(PreparationContext context)
    {
        var available = _spawns.Select(s => s.Site).Distinct().ToList();
        var decision = SiteSelector.Choose(_history, null, _config.MaxSameSiteInRow, available, _random);
        _history = decision.History;
        return context with { Site = decision.Site };
    }

    private PreparationContext PlacePlayers(PreparationContext context)
    {
        if (context.Site is not { } site)
        {
            return context;
        }
        var humans = PlayerQueries.Humans();
        var requests = humans
            .Select(p => (Player: p, Side: PlayerQueries.SideOf(p)))
            .Where(p => p.Side is not null)
            .Select(p => new SpawnRequest(new PlayerId(p.Player.Slot), p.Side!.Value))
            .ToList();
        var result = SpawnSelector.Place(requests, _spawns, site, _random);
        _assignments = result.Assignments.ToImmutableDictionary(a => a.Player.Slot, a => a.Spawn);
        foreach (var player in humans.Where(p => _assignments.ContainsKey(p.Slot)))
        {
            Teleport(player, _assignments[player.Slot]);
        }
        if (result.Unplaced.Count > 0 && _spawns.Count > 0)
        {
            Context.Logger.LogWarning("{Count} player(s) kept the default spawn: not enough spawns on site {Site}", result.Unplaced.Count, site);
        }
        return context with { Planter = result.Planter };
    }

    private void OnPlayerSpawn(EventPlayerSpawn e)
    {
        var phase = Context.Rounds.State.Phase;
        if (e.Userid is not { IsValid: true } player || phase is not (RoundPhase.Preparing or RoundPhase.FreezeTime))
        {
            return;
        }
        if (_assignments.TryGetValue(player.Slot, out var spawn))
        {
            Teleport(player, spawn);
        }
    }

    private static void Teleport(CCSPlayerController player, SpawnPoint spawn)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
        {
            return;
        }
        pawn.Teleport(
            new Vector(spawn.Position.X, spawn.Position.Y, spawn.Position.Z),
            new QAngle(spawn.Angle.Pitch, spawn.Angle.Yaw, spawn.Angle.Roll),
            new Vector(0f, 0f, 0f));
    }

    private void Announce(RoundPrepared e)
    {
        if (e.Context.Site is { } site)
        {
            Context.Text.ChatAll("spawns.round.announce", e.Context.RoundType ?? "-", site.ToString());
        }
    }
}
```

Lang : ajouter à `src/RetakeV4/lang/en.json`
```json
  "spawns.round.announce": "{0} round — bombsite {1}"
```
et à `src/RetakeV4/lang/fr.json`
```json
  "spawns.round.announce": "Round {0} — site {1}"
```

`src/RetakeV4/RetakeV4Plugin.cs` : `using RetakeV4.Modules.Spawns;` et ajouter `new SpawnsModule(),` après `new RoundTypesModule(),` dans `CreateModules`.

`scripts/package-dev.ps1` : après la copie de `lang`, ajouter
```powershell
Copy-Item (Join-Path $bin "spawns") $pluginDir -Recurse
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0` (dont 11 cas `ShippedFile_IsCurrentFormat_AndCoversBothSites`).

- [ ] **Step 5: Commit**

```bash
git add tools/RetakeV4.SpawnMigrator RetakeV4.sln src/RetakeV4/spawns src/RetakeV4/Modules/Spawns src/RetakeV4/RetakeV4.csproj src/RetakeV4/RetakeV4Plugin.cs src/RetakeV4/lang scripts/package-dev.ps1 tests/RetakeV4.Integration.Tests/Spawns tests/RetakeV4.Integration.Tests/Modules/Spawns
git commit -m "feat: module Spawns, spawns des 11 maps au format V2 et outil de migration"
```

---

### Task 10: Module `Teams`

**Files:**
- Create: `src/RetakeV4/Modules/Teams/TeamsConfig.cs`, `TeamsConfigValidator.cs`, `TeamsModule.cs`
- Modify: `src/RetakeV4/RetakeV4Plugin.cs`, `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`
- Test: `tests/RetakeV4.Integration.Tests/Modules/Teams/TeamsConfigValidatorTests.cs`, `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs`

**Interfaces:**
- Consumes: `TeamPlanner` (`PlanRoundEnd`, `RequestJoin`, `Leave`, `Adopt`, `Reconcile`), `TeamState`, `TeamRules`, `TeamMove`, `MoveReason`, `RoundWinner`, `JoinOutcome` (Tasks 7-8) ; `PlayerQueries`, `GameRulesAccessor` (Task 3) ; `ModuleHooks` (Task 1).
- Produces :
  - Config `teams.json` : `TeamsConfig` (Version 1) : `MaxPlayers` 9, `TeamBalanceRatio` 0.499, `ScrambleAfterTWins` 5, `SwitchTeamsOnCtWin` true, `RestartOnInconsistency` true, `PriorityFlags` (`@css/vip` → 1, `@css/root` → 2 ; un flag commençant par `#` est un groupe) ; `TeamRules ToRules()`.
  - Module `Teams` (dépend de `Core`) : listener `jointeam` (Pre), silence de `player_team`, départs sur `player_disconnect`, plan de fin de round sur `round_end` (hors warmup), scramble + adoption à la fin du warmup, adoption à chaque `MapStarted`, réconciliation au `round_freeze_end`, commande `css_retake_scramble` (`@retakev4/admin`).
  - Clés lang : `teams.queue.joined`, `teams.switch.refused`, `teams.move.switched_after_ct_win`, `teams.move.entered_from_queue`, `teams.move.scrambled`, `teams.move.balanced`, `teams.scramble.requested`, `teams.round.t_streak`, `teams.inconsistent`, `teams.no_permission`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/Modules/Teams/TeamsConfigValidatorTests.cs`
```csharp
using RetakeV4.Modules.Teams;

namespace RetakeV4.Integration.Tests.Modules.Teams;

public class TeamsConfigValidatorTests
{
    private static readonly TeamsConfig Defaults = new();
    private readonly TeamsConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid_AndMatchV3()
    {
        var result = _validator.Validate(Defaults, Defaults, "teams.json");
        Assert.Empty(result.Issues);
        var rules = result.Config.ToRules();
        Assert.Equal(9, rules.MaxPlayers);
        Assert.Equal(0.499, rules.TBalanceRatio);
        Assert.Equal(5, rules.ScrambleAfterTWins);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65)]
    public void OutOfRangeMaxPlayers_FallsBackToDefault(int maxPlayers)
    {
        var result = _validator.Validate(Defaults with { MaxPlayers = maxPlayers }, Defaults, "teams.json");
        Assert.Equal(9, result.Config.MaxPlayers);
        Assert.Single(result.Issues);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.5)]
    public void OutOfRangeRatio_FallsBackToDefault(double ratio)
    {
        var result = _validator.Validate(Defaults with { TeamBalanceRatio = ratio }, Defaults, "teams.json");
        Assert.Equal(0.499, result.Config.TeamBalanceRatio);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void NegativeScrambleThreshold_FallsBackToDefault()
    {
        var result = _validator.Validate(Defaults with { ScrambleAfterTWins = -1 }, Defaults, "teams.json");
        Assert.Equal(5, result.Config.ScrambleAfterTWins);
    }

    [Fact]
    public void InvalidPriorityFlags_AreRemoved()
    {
        var flags = new[]
        {
            new PriorityFlagConfig { Flag = "@css/vip", Priority = 1 },
            new PriorityFlagConfig { Flag = "vip", Priority = 1 },
            new PriorityFlagConfig { Flag = "#retake/vip", Priority = 0 },
            new PriorityFlagConfig { Flag = "#retake/vip", Priority = 3 },
        };
        var result = _validator.Validate(Defaults with { PriorityFlags = flags }, Defaults, "teams.json");
        Assert.Equal(new[] { "@css/vip", "#retake/vip" }, result.Config.PriorityFlags.Select(f => f.Flag));
        Assert.Equal(2, result.Issues.Count);
    }
}
```

Ajouter à `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs` :
```csharp
    [Theory]
    [InlineData("spawns.round.announce")]
    [InlineData("teams.queue.joined")]
    [InlineData("teams.switch.refused")]
    [InlineData("teams.move.switched_after_ct_win")]
    [InlineData("teams.move.entered_from_queue")]
    [InlineData("teams.move.scrambled")]
    [InlineData("teams.move.balanced")]
    [InlineData("teams.scramble.requested")]
    [InlineData("teams.round.t_streak")]
    [InlineData("teams.inconsistent")]
    [InlineData("teams.no_permission")]
    public void Phase2aKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Modules.Teams` introuvable).

- [ ] **Step 2: Implémenter config et validateur**

`src/RetakeV4/Modules/Teams/TeamsConfig.cs`
```csharp
using RetakeV4.Configuration;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Modules.Teams;

public sealed record PriorityFlagConfig
{
    public string Flag { get; init; } = string.Empty;

    public int Priority { get; init; }
}

public sealed record TeamsConfig : ModuleConfig
{
    public TeamsConfig() => Version = 1;

    public int MaxPlayers { get; init; } = 9;

    public double TeamBalanceRatio { get; init; } = 0.499;

    public int ScrambleAfterTWins { get; init; } = 5;

    public bool SwitchTeamsOnCtWin { get; init; } = true;

    public bool RestartOnInconsistency { get; init; } = true;

    public IReadOnlyList<PriorityFlagConfig> PriorityFlags { get; init; } = new[]
    {
        new PriorityFlagConfig { Flag = "@css/vip", Priority = 1 },
        new PriorityFlagConfig { Flag = "@css/root", Priority = 2 },
    };

    public TeamRules ToRules() => new(MaxPlayers, TeamBalanceRatio, ScrambleAfterTWins, SwitchTeamsOnCtWin);
}
```

`src/RetakeV4/Modules/Teams/TeamsConfigValidator.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Teams;

public sealed class TeamsConfigValidator : IConfigValidator<TeamsConfig>
{
    private const int MinPlayers = 2;
    private const int MaxServerPlayers = 64;

    public ValidationResult<TeamsConfig> Validate(TeamsConfig config, TeamsConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var result = config;
        if (config.MaxPlayers is < MinPlayers or > MaxServerPlayers)
        {
            issues.Add(new ConfigIssue(file, nameof(TeamsConfig.MaxPlayers), $"must be between {MinPlayers} and {MaxServerPlayers}; using default"));
            result = result with { MaxPlayers = defaults.MaxPlayers };
        }
        if (config.TeamBalanceRatio is <= 0 or >= 1)
        {
            issues.Add(new ConfigIssue(file, nameof(TeamsConfig.TeamBalanceRatio), "must be strictly between 0 and 1; using default"));
            result = result with { TeamBalanceRatio = defaults.TeamBalanceRatio };
        }
        if (config.ScrambleAfterTWins < 0)
        {
            issues.Add(new ConfigIssue(file, nameof(TeamsConfig.ScrambleAfterTWins), "must be >= 0 (0 disables); using default"));
            result = result with { ScrambleAfterTWins = defaults.ScrambleAfterTWins };
        }
        return new ValidationResult<TeamsConfig>(result with { PriorityFlags = CleanFlags(config.PriorityFlags, file, issues) }, issues);
    }

    private static IReadOnlyList<PriorityFlagConfig> CleanFlags(IReadOnlyList<PriorityFlagConfig>? flags, string file, List<ConfigIssue> issues)
    {
        var kept = new List<PriorityFlagConfig>();
        foreach (var flag in flags ?? Array.Empty<PriorityFlagConfig>())
        {
            var validName = flag.Flag.StartsWith('@') || flag.Flag.StartsWith('#');
            if (!validName || flag.Priority <= 0)
            {
                issues.Add(new ConfigIssue(file, nameof(TeamsConfig.PriorityFlags), $"entry '{flag.Flag}' (priority {flag.Priority}) removed: flag must start with @ or #, priority must be > 0"));
                continue;
            }
            kept.Add(flag);
        }
        return kept;
    }
}
```

- [ ] **Step 3: Implémenter le module**

`src/RetakeV4/Modules/Teams/TeamsModule.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Modules.Teams;

public sealed class TeamsModule : IRetakeModule
{
    private const string AdminFlag = "@retakev4/admin";
    private const int SpectatorArg = 1;
    private const int TerroristArg = 2;
    private const int CounterTerroristArg = 3;

    private readonly IRandom _random = SystemRandom.Shared;
    private TeamsConfig _config = new();
    private ModuleContext? _context;
    private TeamState _state = TeamState.Empty;
    private bool _scrambleRequested;

    public string Name => "Teams";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Teams module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("teams.json", new TeamsConfig(), new TeamsConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.CommandListener("jointeam", OnJoinTeam, HookMode.Pre);
        hooks.OnEventHook<EventPlayerTeam>("player_team", e =>
        {
            e.Silent = true;
            return HookResult.Continue;
        }, HookMode.Pre);
        hooks.OnEvent<EventPlayerDisconnect>("player_disconnect", e =>
        {
            if (e.Userid is { } player)
            {
                _state = TeamPlanner.Leave(_state, new PlayerId(player.Slot));
            }
        });
        hooks.OnEvent<EventRoundEnd>("round_end", OnRoundEnd);
        hooks.OnEvent<EventRoundFreezeEnd>("freeze_end", _ => Reconcile());
        hooks.OnBus<MapStarted>(_ => ResetForMap());
        hooks.OnBus<RoundPhaseChanged>(OnPhaseChanged);
        hooks.Command("css_retake_scramble", "Scrambles the teams at the end of the round", OnScrambleCommand);
    }

    public void Unload() => _context = null;

    private HookResult OnJoinTeam(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || !player.IsValid || player.IsBot || player.IsHLTV)
        {
            return HookResult.Continue;
        }
        if (!int.TryParse(info.GetArg(1), out var requestedArg) || requestedArg is < SpectatorArg or > CounterTerroristArg)
        {
            return HookResult.Handled;
        }
        var id = new PlayerId(player.Slot);
        if (requestedArg == SpectatorArg)
        {
            _state = TeamPlanner.Leave(_state, id);
            return HookResult.Continue;
        }
        var requested = requestedArg == TerroristArg ? TeamSide.T : TeamSide.CT;
        var result = TeamPlanner.RequestJoin(_state, id, PriorityOf(player), GameRulesAccessor.IsWarmup(), requested, _config.ToRules());
        _state = result.State;
        ApplyJoin(player, result, requested);
        return HookResult.Handled;
    }

    private void ApplyJoin(CCSPlayerController player, JoinResult result, TeamSide requested)
    {
        switch (result.Outcome)
        {
            case JoinOutcome.JoinedNow:
                player.ChangeTeam(PlayerQueries.ToCsTeam(result.Side!.Value));
                if (result.RestartRound)
                {
                    GameRulesAccessor.Get()?.TerminateRound(1f, RoundEndReason.RoundDraw);
                }
                break;
            case JoinOutcome.AlreadyPlaying when result.Side != requested:
                Context.Text.Chat(player, "teams.switch.refused");
                break;
            case JoinOutcome.Queued:
            case JoinOutcome.AlreadyQueued:
                if ((CsTeam)player.TeamNum != CsTeam.Spectator)
                {
                    player.ChangeTeam(CsTeam.Spectator);
                }
                Context.Text.Chat(player, "teams.queue.joined", result.QueuePosition ?? 0);
                break;
        }
    }

    private int PriorityOf(CCSPlayerController player) =>
        _config.PriorityFlags
            .Where(f => f.Flag.StartsWith('#')
                ? AdminManager.PlayerInGroup(player, f.Flag)
                : AdminManager.PlayerHasPermissions(player, f.Flag))
            .Select(f => f.Priority)
            .DefaultIfEmpty(0)
            .Max();

    private void OnRoundEnd(EventRoundEnd e)
    {
        if (GameRulesAccessor.IsWarmup())
        {
            return;
        }
        var winner = e.Winner switch
        {
            (int)CsTeam.Terrorist => RoundWinner.T,
            (int)CsTeam.CounterTerrorist => RoundWinner.CT,
            _ => RoundWinner.None,
        };
        var plan = TeamPlanner.PlanRoundEnd(_state, winner, _scrambleRequested, _config.ToRules(), _random);
        _scrambleRequested = false;
        if (winner == RoundWinner.T && !plan.Scrambled)
        {
            Context.Text.ChatAll("teams.round.t_streak", plan.State.TWinStreak);
        }
        ApplyPlan(plan);
    }

    private void OnPhaseChanged(RoundPhaseChanged e)
    {
        if (e.From != RoundPhase.Warmup || e.To != RoundPhase.PostRound)
        {
            return;
        }
        AdoptPlayersOnTeams();
        var plan = TeamPlanner.PlanRoundEnd(_state, RoundWinner.None, true, _config.ToRules(), _random);
        ApplyPlan(plan);
        if (plan.Moves.Count > 0)
        {
            // Players already respawned in their warmup team for round 1: restart so they spawn
            // in their new team (also resets TotalRoundsPlayed for the round type sequence).
            Server.ExecuteCommand("mp_restartgame 1");
        }
    }

    private void ResetForMap()
    {
        _state = TeamState.Empty;
        _scrambleRequested = false;
        AdoptPlayersOnTeams();
    }

    private void AdoptPlayersOnTeams()
    {
        var onTeams = PlayerQueries.Humans()
            .Select(p => (Player: new PlayerId(p.Slot), Side: PlayerQueries.SideOf(p)))
            .Where(p => p.Side is not null)
            .Select(p => (p.Player, p.Side!.Value))
            .ToList();
        _state = TeamPlanner.Adopt(_state, onTeams, _config.ToRules());
    }

    private void ApplyPlan(TeamPlan plan)
    {
        _state = plan.State;
        foreach (var move in plan.Moves)
        {
            var player = Utilities.GetPlayerFromSlot(move.Player.Slot);
            if (player is null || !player.IsValid)
            {
                continue;
            }
            player.SwitchTeam(PlayerQueries.ToCsTeam(move.To));
            Context.Text.Chat(player, ReasonKey(move.Reason), move.To.ToString());
        }
        NotifyQueue();
    }

    private void NotifyQueue()
    {
        foreach (var queued in _state.OrderedQueue())
        {
            var player = Utilities.GetPlayerFromSlot(queued.Player.Slot);
            if (player is { IsValid: true })
            {
                Context.Text.Chat(player, "teams.queue.joined", _state.QueuePosition(queued.Player) ?? 0);
            }
        }
    }

    private static string ReasonKey(MoveReason reason) => reason switch
    {
        MoveReason.SwitchedAfterCtWin => "teams.move.switched_after_ct_win",
        MoveReason.EnteredFromQueue => "teams.move.entered_from_queue",
        MoveReason.Scrambled => "teams.move.scrambled",
        _ => "teams.move.balanced",
    };

    private void Reconcile()
    {
        if (GameRulesAccessor.IsWarmup())
        {
            return;
        }
        var actual = PlayerQueries.Humans().ToDictionary(p => new PlayerId(p.Slot), PlayerQueries.SideOf);
        var result = TeamPlanner.Reconcile(_state, actual);
        foreach (var intruder in result.ToSpectator)
        {
            Utilities.GetPlayerFromSlot(intruder.Slot)?.ChangeTeam(CsTeam.Spectator);
        }
        if (result.Fixes.Count == 0)
        {
            return;
        }
        foreach (var fix in result.Fixes)
        {
            Utilities.GetPlayerFromSlot(fix.Player.Slot)?.SwitchTeam(PlayerQueries.ToCsTeam(fix.To));
        }
        if (_config.RestartOnInconsistency)
        {
            Context.Text.ChatAll("teams.inconsistent");
            GameRulesAccessor.Get()?.TerminateRound(1f, RoundEndReason.RoundDraw);
        }
    }

    private void OnScrambleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null && !AdminManager.PlayerHasPermissions(player, AdminFlag))
        {
            Context.Text.Chat(player, "teams.no_permission");
            return;
        }
        _scrambleRequested = true;
        Context.Text.ChatAll("teams.scramble.requested");
    }
}
```

Lang : ajouter à `src/RetakeV4/lang/en.json`
```json
  "teams.queue.joined": "You are in the queue (position {0}): you will join at the end of a round.",
  "teams.switch.refused": "You cannot switch teams manually: it is done automatically.",
  "teams.move.switched_after_ct_win": "CTs won the round: you are now {0}.",
  "teams.move.entered_from_queue": "You leave the queue and play as {0}.",
  "teams.move.scrambled": "Teams have been scrambled: you are now {0}.",
  "teams.move.balanced": "Teams have been balanced: you are now {0}.",
  "teams.scramble.requested": "An admin requested a scramble: teams will be scrambled at the end of the round.",
  "teams.round.t_streak": "Terrorists have won {0} round(s) in a row.",
  "teams.inconsistent": "Teams were inconsistent: restarting the round.",
  "teams.no_permission": "You do not have permission to use this command."
```
et à `src/RetakeV4/lang/fr.json`
```json
  "teams.queue.joined": "Tu es dans la file d'attente (position {0}) : tu rejoindras la partie à la fin d'un round.",
  "teams.switch.refused": "Tu ne peux pas changer d'équipe toi-même : c'est automatique.",
  "teams.move.switched_after_ct_win": "Les CT ont gagné le round : tu passes {0}.",
  "teams.move.entered_from_queue": "Tu sors de la file et tu joues {0}.",
  "teams.move.scrambled": "Les équipes ont été mélangées : tu es maintenant {0}.",
  "teams.move.balanced": "Les équipes ont été rééquilibrées : tu es maintenant {0}.",
  "teams.scramble.requested": "Un admin a demandé un scramble : les équipes seront mélangées à la fin du round.",
  "teams.round.t_streak": "Les terroristes ont gagné {0} round(s) d'affilée.",
  "teams.inconsistent": "Les équipes étaient incohérentes : le round redémarre.",
  "teams.no_permission": "Tu n'as pas la permission d'utiliser cette commande."
```

`src/RetakeV4/RetakeV4Plugin.cs` : `using RetakeV4.Modules.Teams;` et `CreateModules` devient
```csharp
    private static IReadOnlyList<IRetakeModule> CreateModules() => new IRetakeModule[]
    {
        new CoreModule(),
        new RoundTypesModule(),
        new TeamsModule(),
        new SpawnsModule(),
    };
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`. Si `AdminManager.PlayerInGroup(player, string)` n'accepte pas un seul `string` (signature `params string[]`), l'appel reste valide ; sinon passer `new[] { f.Flag }`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4/Modules/Teams src/RetakeV4/RetakeV4Plugin.cs src/RetakeV4/lang tests/RetakeV4.Integration.Tests/Modules/Teams tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs
git commit -m "feat: module Teams (file prioritaire, rotation, scramble, réconciliation)"
```

---

### Task 11: Checklist en jeu phase 2a, documentation et vérification finale

**Files:**
- Modify: `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`

**Interfaces:**
- Consumes: tout ce qui précède.
- Produces: section « Phase 2a » de la checklist ; `CLAUDE.md` mentionne `Adapters/`, `ModuleHooks` et l'outil de migration.

- [ ] **Step 1: Checklist**

Ajouter à `docs/CHECKLIST-INGAME.md` :
```markdown
## Phase 2a — Types de round, spawns, équipes
Préparation : `pwsh -NoProfile -File scripts/package-dev.ps1`, copier `artifacts/dev/*` (le dossier `plugins/RetakeV4/spawns/` doit être présent).
- [ ] Log `RetakeV4 … loaded with modules: Core, RoundTypes, Teams, Spawns` puis `Loaded 52 spawns for de_mirage (legacy format: False)`.
- [ ] `roundtypes.json`, `teams.json`, `spawns.json` créés avec les valeurs par défaut.
- [ ] Après le warmup : scramble (message « Les équipes ont été mélangées »), un `mp_restartgame 1` automatique si des joueurs ont changé d'équipe, puis round 1 avec tout le monde dans sa nouvelle équipe et ratio respecté (ex. 5 joueurs → 3 CT / 2 T).
- [ ] Chaque round : message « Round Pistol — site A » ; rounds 1-3 Pistol, 4-6 Mid, puis FullBuy (avec `"Debug": true` dans `roundtypes.json`, le log indique `rounds played`).
- [ ] Les joueurs apparaissent sur des spawns retake du site annoncé, un T est sur un spawn en zone de plant, aucun joueur empilé.
- [ ] Victoire CT : les CT passent T, les T passent CT, message correspondant.
- [ ] Victoire T : équipes inchangées, message « … round(s) d'affilée » ; 5 victoires T de suite → scramble.
- [ ] Un joueur qui rejoint en cours de partie est mis en file (position annoncée) et entre à la fin du round.
- [ ] Un joueur `@css/vip` rejoint la file après un joueur normal : il passe devant.
- [ ] Changer d'équipe via le menu CT↔T est refusé avec message.
- [ ] Serveur plein (MaxPlayers atteint) : le suivant reste en file tant que personne ne part.
- [ ] Déconnexion d'un joueur en jeu : le round suivant rééquilibre.
- [ ] `css_retake_scramble` (admin) : message puis scramble à la fin du round ; sans permission : refus.
- [ ] Map sans fichier de spawns (ex. `de_basalt`) : warning, spawns CS2 par défaut, aucune erreur.
- [ ] `css_plugins reload RetakeV4` en plein match : les joueurs en T/CT restent en jeu, aucune erreur.
```

- [ ] **Step 2: CLAUDE.md**

Dans `CLAUDE.md`, section Structure, ajouter :
```markdown
- `src/RetakeV4/Adapters` : accès CSSharp partagés (gamerules, requêtes joueurs).
- `tools/RetakeV4.SpawnMigrator` : convertit des spawns V3 (tableau à plat) au format V2 (`dotnet run --project tools/RetakeV4.SpawnMigrator -- <in> <out>`).
```
et section Règles, remplacer la ligne sur `ModuleGuard.Run` par :
```markdown
- Tout handler CSSharp, abonnement au bus ou étape de préparation d'un module passe par `context.Hooks` (`ModuleHooks`) : gardé par `ModuleGuard` et libéré automatiquement au déchargement.
- Tout hasard passe par `IRandom` (`SystemRandom.Shared` en jeu, `FixedRandom` en test).
```

- [ ] **Step 3: Vérification finale**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo && dotnet test tests/RetakeV4.Domain.Tests --nologo -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line && pwsh -NoProfile -File scripts/package-dev.ps1`
Expected: 0 warning, `Failed: 0` partout, couverture Domain ≥ 80 %, `Package ready` avec `plugins/RetakeV4/spawns/de_mirage.json` présent.

- [ ] **Step 4: Commit**

```bash
git add docs/CHECKLIST-INGAME.md CLAUDE.md
git commit -m "docs: checklist en jeu phase 2a et guide du dépôt"
```
