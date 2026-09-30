# RetakeV4 — Phase 2b Implementation Plan (armes, plant, InstaDefuse)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Atteindre la parité de jeu avec V3 (sans menus) : chaque joueur reçoit son équipement selon le type de round (armes par défaut, armure, kits de défuse, Zeus, grenades, règles AWP prêtes pour les préférences de la phase 3), la bombe est posée automatiquement (AutoPlant) ou rapidement par le planteur (FastPlant), et le défuse est instantané quand plus aucun T n'est en vie (InstaDefuse).

**Architecture:** Comme les phases précédentes : décisions pures dans `RetakeV4.Domain` (catalogue d'armes, planificateur d'équipement, règle du plant, suivi des menaces et politique InstaDefuse), modules CSSharp fins (`Allocation`, `Plant`, `InstaDefuse`) qui reprennent le code moteur V3 validé en production. Les définitions complètes des types de round (pools d'armes, défauts, AWP, kits, Zeus, pool de grenades) vivent dans `roundtypes.json` et voyagent jusqu'à l'allocation via le `PreparationContext`.

**Tech Stack:** C# / .NET 10, CounterStrikeSharp.API 1.0.370, System.Text.Json, xUnit 2.9.3, coverlet.msbuild 6.0.4.

**Spec:** `docs/superpowers/specs/2026-09-30-retake-v4-design.md` (§5 allocation, §7.4 plant, §8 InstaDefuse)

## Global Constraints

- CounterStrikeSharp.API **1.0.370**, `[MinimumApiVersion(370)]`, `net10.0`, `Nullable` + `TreatWarningsAsErrors`.
- `RetakeV4.Domain` ne référence **jamais** CounterStrikeSharp.
- Fonctions < 50 lignes, fichiers < 800 lignes, imbrication ≤ 4, Domain immuable (records).
- Couverture Domain ≥ **80 %** : `dotnet test tests/RetakeV4.Domain.Tests -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`.
- Textes joueurs uniquement via `lang/*.json` (en + fr) ; logs en anglais avec templates constants ; un nom de joueur est toujours un argument, jamais un template.
- Tout handler / abonnement / étape d'un module passe par `context.Hooks` ; tout callback `Server.NextFrame` ou timer passe par `ModuleGuard.Run`.
- Hasard via `IRandom` uniquement.
- Fichiers contenant des apostrophes : outil Write (pas de heredoc Bash). Script de package : outil PowerShell (`pwsh` absent du PATH de Git Bash).

## Décisions de conception (écarts assumés vs V3)

1. **Préférences** : pas encore de préférences joueur (phase 3). Le planificateur les accepte déjà (`LoadoutPreference`), mais en 2b tout le monde reçoit les armes par défaut du type de round, et l'AWP n'est donc jamais attribuée (aucun volontaire) — comportement V3 d'un joueur sans préférence.
2. **Kits de défuse** : le cas spécial « pistol » de V3 devient une configuration normale du type Pistol (`Mode: Chance`, `Chance: 34.44444`, `GuaranteeMinimum: true`) : un type de round personnalisé peut avoir sa propre règle.
3. **AWP** : `MaxPerTeam` tirages (V3 : un seul) parmi les volontaires mélangés, chacun avec `Chance` ; avec la valeur par défaut `MaxPerTeam = 1`, identique à V3.
4. **Couteaux** : le retrait des armes garde tout ce dont le nom contient `knife` **ou** `bayonet` (V3 retirait les baïonnettes).
5. **FastPlant raté** : fin de round victoire CT (V3 faisait en plus un scramble et tuait tout le monde quand aucune C4 n'existait ; supprimé).
6. **Site de la bombe** : pris dans le `PreparationContext` (le champ `site` de l'événement `bomb_planted` natif est un index d'entité, pas A/B).
7. **InstaDefuse** : aucun message quand des T sont encore en vie (défuse normal), comme V3 ; les messages de blocage distinguent HE, molotov et feu.

## Review Focus

1. **Planteur déconnecté ou mort entre `round_start` et le freeze end** : pas de plant automatique, aucune exception, le round continue. Test : `PlantRulesTests.CanAutoPlant_*` (Task 7).
2. **`roundtypes.json` édité avec des armes inconnues, dans le mauvais emplacement, ou des défauts hors pool** : entrées retirées, défauts réparés, avertissements, module actif. Tests : `RoundTypeDefinitionValidationTests` (Task 4).
3. **Round avec kit par chance où tous les tirages échouent, ou sans CT** : minimum garanti respecté, aucune exception. Tests : `LoadoutPlannerTests.Kits_*` (Task 3).
4. **Pool de grenades absent de `grenades.json`** : aucune grenade, pas d'exception. Test : `GrenadesConfigTests.UnknownPool_GivesNoKits` (Task 5).
5. **Défuse commencé trop tard** : message « temps insuffisant », explosion forcée seulement si configurée. Tests : `InstaDefusePolicyTests.NotEnoughTime_*` (Task 6).

---

## File Structure

```
src/RetakeV4.Domain/
├─ Common/Chance.cs                                                      (Task 1)
├─ Rounds/PreparationOrder.cs (+ Plant = 60), PreparationContext.cs (+ RoundTypeDefinition)  (Tasks 1, 4)
├─ Loadouts/WeaponCatalog.cs, LoadoutModel.cs                            (Task 2)
├─ Loadouts/LoadoutPlanner.cs                                            (Task 3)
├─ RoundTypes/RoundTypeStep.cs (définitions)                              (Task 4)
├─ InstaDefuse/ThreatState.cs, InstaDefusePolicy.cs                      (Task 6)
├─ Plant/PlantRules.cs                                                   (Task 7)
└─ Events/RoundEvents.cs (+ BombPlanted)                                 (Task 7)
src/RetakeV4/Modules/
├─ RoundTypes/RoundTypeDefinitionConfig.cs, RoundTypeDefaults.cs, RoundTypeDefinitionValidation.cs (Task 4)
├─ Allocation/GrenadesConfig.cs, GrenadesConfigValidator.cs, AllocationConfig.cs, LoadoutApplier.cs, AllocationModule.cs (Task 5)
├─ InstaDefuse/InstaDefuseConfig.cs, InstaDefuseConfigValidator.cs, InstaDefuseModule.cs (Task 8)
└─ Plant/PlantConfig.cs, PlantConfigValidator.cs, PlantModule.cs         (Task 7)
tests/RetakeV4.Domain.Tests/{Common,Loadouts,InstaDefuse,Plant}/…
tests/RetakeV4.Integration.Tests/Modules/{RoundTypes,Allocation,Plant,InstaDefuse}/…
```

---

### Task 1: Tirage de probabilité et contrôle du `NextDouble` en test

**Files:**
- Create: `src/RetakeV4.Domain/Common/Chance.cs`
- Modify: `tests/RetakeV4.Domain.Tests/TestDoubles/FixedRandom.cs`, `src/RetakeV4.Domain/Rounds/PreparationOrder.cs`
- Test: `tests/RetakeV4.Domain.Tests/Common/ChanceTests.cs`

**Interfaces:**
- Produces:
  - `static class Chance { bool Roll(double percent, IRandom random); }` — `≤ 0` ⇒ `false`, `≥ 100` ⇒ `true`, sinon `NextDouble() * 100 <= percent` (règle V3/agora).
  - `FixedRandom` gagne `public double DoubleValue { get; init; }` renvoyé par `NextDouble()` (défaut 0.0).
  - `PreparationOrder.Plant = 60`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Common/ChanceTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Common;

public class ChanceTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void NonPositiveChance_NeverSucceeds(double percent) =>
        Assert.False(Chance.Roll(percent, new FixedRandom { DoubleValue = 0.0 }));

    [Theory]
    [InlineData(100.0)]
    [InlineData(150.0)]
    public void FullChance_AlwaysSucceeds(double percent) =>
        Assert.True(Chance.Roll(percent, new FixedRandom { DoubleValue = 0.9999 }));

    [Theory]
    [InlineData(0.29, true)]
    [InlineData(0.30, true)]
    [InlineData(0.31, false)]
    public void PartialChance_ComparesDrawToPercent(double draw, bool expected) =>
        Assert.Equal(expected, Chance.Roll(30.0, new FixedRandom { DoubleValue = draw }));

    [Fact]
    public void Distribution_IsCloseToPercent()
    {
        var random = new SystemRandom(new Random(5));
        var successes = Enumerable.Range(0, 10_000).Count(_ => Chance.Roll(30.0, random));
        Assert.InRange(successes, 2700, 3300);
    }

    [Fact]
    public void PlantStep_RunsAfterLoadout() =>
        Assert.True(PreparationOrder.Plant > PreparationOrder.Loadout);
}
```

Dans `tests/RetakeV4.Domain.Tests/TestDoubles/FixedRandom.cs`, remplacer `public double NextDouble() => 0.0;` par :
```csharp
    public double DoubleValue { get; init; }

    public double NextDouble() => DoubleValue;
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`Chance`, `PreparationOrder.Plant` introuvables).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Common/Chance.cs`
```csharp
namespace RetakeV4.Domain.Common;

public static class Chance
{
    public static bool Roll(double percent, IRandom random)
    {
        if (percent <= 0d)
        {
            return false;
        }
        if (percent >= 100d)
        {
            return true;
        }
        return random.NextDouble() * 100d <= percent;
    }
}
```

Dans `src/RetakeV4.Domain/Rounds/PreparationOrder.cs`, ajouter après `Loadout` :
```csharp
    public const int Plant = 60;
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Common/Chance.cs src/RetakeV4.Domain/Rounds/PreparationOrder.cs tests/RetakeV4.Domain.Tests/Common/ChanceTests.cs tests/RetakeV4.Domain.Tests/TestDoubles/FixedRandom.cs
git commit -m "feat: tirage de probabilité (Chance) et étape de plant dans l'ordre de préparation"
```

---

### Task 2: Catalogue d'armes et modèle d'équipement (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Loadouts/WeaponCatalog.cs`, `src/RetakeV4.Domain/Loadouts/LoadoutModel.cs`
- Test: `tests/RetakeV4.Domain.Tests/Loadouts/WeaponCatalogTests.cs`, `tests/RetakeV4.Domain.Tests/Loadouts/LoadoutModelTests.cs`

**Interfaces:**
- Consumes: `TeamSide`, `PlayerId` (phase 2a).
- Produces:
  - `static class WeaponCatalog { const string Awp = "weapon_awp"; bool IsPrimary(string? id); bool IsSecondary(string? id); bool IsGrenade(string? id); }` (identifiants CS2 `weapon_*`, sensibles à la casse).
  - `enum ArmorKind { None, Kevlar, KevlarHelmet }`, `enum DefuseKitMode { All, Quota, Chance }`
  - `sealed record TeamWeapons(IReadOnlyList<string> T, IReadOnlyList<string> CT, IReadOnlyList<string> Any)` avec `static Empty` et `IReadOnlyList<string> For(TeamSide side)` (camp + Any, sans doublon, ordre conservé).
  - `sealed record TeamDefault(string? Primary, string Secondary)`
  - `sealed record AwpSettings(bool Enabled, int MaxPerTeam, int MinActivePlayers, double Chance)`
  - `sealed record DefuseKitSettings(DefuseKitMode Mode, int Quota, double Chance, bool GuaranteeMinimum)`
  - `sealed record ZeusSettings(bool Enabled, double Chance)`
  - `sealed record RoundTypeDefinition(string Name, ArmorKind Armor, TeamWeapons Primaries, TeamWeapons Secondaries, TeamDefault DefaultT, TeamDefault DefaultCT, AwpSettings Awp, DefuseKitSettings DefuseKit, ZeusSettings Zeus, string GrenadePool)` avec `TeamDefault DefaultFor(TeamSide side)`.
  - `sealed record GrenadeKit(TeamSide? Team, IReadOnlyList<string> Grenades)` (`Team = null` : les deux camps).
  - `sealed record LoadoutPreference(string? Primary, string? Secondary, bool AwpOptIn)`
  - `sealed record LoadoutRequest(PlayerId Player, TeamSide Team, LoadoutPreference? Preference)`
  - `sealed record Loadout(string? Primary, string Secondary, ArmorKind Armor, bool DefuseKit, bool Zeus, IReadOnlyList<string> Grenades)`

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Loadouts/WeaponCatalogTests.cs`
```csharp
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Tests.Loadouts;

public class WeaponCatalogTests
{
    [Theory]
    [InlineData("weapon_ak47")]
    [InlineData("weapon_m4a1")]
    [InlineData("weapon_m4a1_silencer")]
    [InlineData("weapon_awp")]
    [InlineData("weapon_mp9")]
    [InlineData("weapon_negev")]
    public void Primaries_AreKnown(string id)
    {
        Assert.True(WeaponCatalog.IsPrimary(id));
        Assert.False(WeaponCatalog.IsSecondary(id));
    }

    [Theory]
    [InlineData("weapon_glock")]
    [InlineData("weapon_usp_silencer")]
    [InlineData("weapon_deagle")]
    [InlineData("weapon_revolver")]
    [InlineData("weapon_elite")]
    public void Secondaries_AreKnown(string id)
    {
        Assert.True(WeaponCatalog.IsSecondary(id));
        Assert.False(WeaponCatalog.IsPrimary(id));
    }

    [Theory]
    [InlineData("weapon_hegrenade")]
    [InlineData("weapon_flashbang")]
    [InlineData("weapon_smokegrenade")]
    [InlineData("weapon_molotov")]
    [InlineData("weapon_incgrenade")]
    [InlineData("weapon_decoy")]
    public void Grenades_AreKnown(string id) => Assert.True(WeaponCatalog.IsGrenade(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("weapon_ak48")]
    [InlineData("WEAPON_AK47")]
    [InlineData("weapon_knife")]
    [InlineData("weapon_c4")]
    public void UnknownIds_AreRejected(string? id)
    {
        Assert.False(WeaponCatalog.IsPrimary(id));
        Assert.False(WeaponCatalog.IsSecondary(id));
        Assert.False(WeaponCatalog.IsGrenade(id));
    }
}
```

`tests/RetakeV4.Domain.Tests/Loadouts/LoadoutModelTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Tests.Loadouts;

public class LoadoutModelTests
{
    [Fact]
    public void TeamWeapons_For_CombinesSideAndAny_WithoutDuplicates()
    {
        var pool = new TeamWeapons(new[] { "weapon_ak47" }, new[] { "weapon_m4a1" }, new[] { "weapon_awp", "weapon_ak47" });
        Assert.Equal(new[] { "weapon_ak47", "weapon_awp" }, pool.For(TeamSide.T));
        Assert.Equal(new[] { "weapon_m4a1", "weapon_awp", "weapon_ak47" }, pool.For(TeamSide.CT));
    }

    [Fact]
    public void TeamWeapons_Empty_HasNothing() => Assert.Empty(TeamWeapons.Empty.For(TeamSide.T));

    [Fact]
    public void Definition_DefaultFor_PicksTheSide()
    {
        var t = new TeamDefault("weapon_ak47", "weapon_glock");
        var ct = new TeamDefault("weapon_m4a1", "weapon_usp_silencer");
        var definition = new RoundTypeDefinition("FullBuy", ArmorKind.KevlarHelmet, TeamWeapons.Empty, TeamWeapons.Empty, t, ct,
            new AwpSettings(false, 1, 5, 30), new DefuseKitSettings(DefuseKitMode.All, 1, 100, false), new ZeusSettings(false, 20), "Default");
        Assert.Equal(t, definition.DefaultFor(TeamSide.T));
        Assert.Equal(ct, definition.DefaultFor(TeamSide.CT));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Domain.Loadouts` introuvable).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Loadouts/WeaponCatalog.cs`
```csharp
namespace RetakeV4.Domain.Loadouts;

public static class WeaponCatalog
{
    public const string Awp = "weapon_awp";

    private static readonly HashSet<string> Primaries = new(StringComparer.Ordinal)
    {
        "weapon_ak47", "weapon_m4a1", "weapon_m4a1_silencer", "weapon_famas", "weapon_galilar", "weapon_aug", "weapon_sg556",
        "weapon_awp", "weapon_ssg08", "weapon_scar20", "weapon_g3sg1",
        "weapon_mp9", "weapon_mac10", "weapon_mp7", "weapon_mp5sd", "weapon_ump45", "weapon_p90", "weapon_bizon",
        "weapon_nova", "weapon_xm1014", "weapon_mag7", "weapon_sawedoff", "weapon_m249", "weapon_negev",
    };

    private static readonly HashSet<string> Secondaries = new(StringComparer.Ordinal)
    {
        "weapon_glock", "weapon_hkp2000", "weapon_usp_silencer", "weapon_p250", "weapon_fiveseven",
        "weapon_tec9", "weapon_cz75a", "weapon_deagle", "weapon_revolver", "weapon_elite",
    };

    private static readonly HashSet<string> Grenades = new(StringComparer.Ordinal)
    {
        "weapon_hegrenade", "weapon_flashbang", "weapon_smokegrenade", "weapon_molotov", "weapon_incgrenade", "weapon_decoy",
    };

    public static bool IsPrimary(string? id) => id is not null && Primaries.Contains(id);

    public static bool IsSecondary(string? id) => id is not null && Secondaries.Contains(id);

    public static bool IsGrenade(string? id) => id is not null && Grenades.Contains(id);
}
```

`src/RetakeV4.Domain/Loadouts/LoadoutModel.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Loadouts;

public enum ArmorKind
{
    None,
    Kevlar,
    KevlarHelmet,
}

public enum DefuseKitMode
{
    All,
    Quota,
    Chance,
}

public sealed record TeamWeapons(IReadOnlyList<string> T, IReadOnlyList<string> CT, IReadOnlyList<string> Any)
{
    public static TeamWeapons Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    public IReadOnlyList<string> For(TeamSide side) =>
        (side == TeamSide.T ? T : CT).Concat(Any).Distinct(StringComparer.Ordinal).ToList();
}

public sealed record TeamDefault(string? Primary, string Secondary);

public sealed record AwpSettings(bool Enabled, int MaxPerTeam, int MinActivePlayers, double Chance);

public sealed record DefuseKitSettings(DefuseKitMode Mode, int Quota, double Chance, bool GuaranteeMinimum);

public sealed record ZeusSettings(bool Enabled, double Chance);

public sealed record RoundTypeDefinition(
    string Name,
    ArmorKind Armor,
    TeamWeapons Primaries,
    TeamWeapons Secondaries,
    TeamDefault DefaultT,
    TeamDefault DefaultCT,
    AwpSettings Awp,
    DefuseKitSettings DefuseKit,
    ZeusSettings Zeus,
    string GrenadePool)
{
    public TeamDefault DefaultFor(TeamSide side) => side == TeamSide.T ? DefaultT : DefaultCT;
}

public sealed record GrenadeKit(TeamSide? Team, IReadOnlyList<string> Grenades);

public sealed record LoadoutPreference(string? Primary, string? Secondary, bool AwpOptIn);

public sealed record LoadoutRequest(PlayerId Player, TeamSide Team, LoadoutPreference? Preference);

public sealed record Loadout(string? Primary, string Secondary, ArmorKind Armor, bool DefuseKit, bool Zeus, IReadOnlyList<string> Grenades);
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Loadouts tests/RetakeV4.Domain.Tests/Loadouts
git commit -m "feat: catalogue d'armes et modèle d'équipement (Domain)"
```

---

### Task 3: Planificateur d'équipement (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Loadouts/LoadoutPlanner.cs`
- Test: `tests/RetakeV4.Domain.Tests/Loadouts/LoadoutPlannerTests.cs`

**Interfaces:**
- Consumes: modèle de Task 2, `WeaponCatalog.Awp`, `Chance.Roll` (Task 1), `IRandom`, `Shuffle`, `Pick`.
- Produces (dans `static class LoadoutPlanner`) :
  - `IReadOnlyDictionary<PlayerId, Loadout> Plan(RoundTypeDefinition definition, IReadOnlyList<LoadoutRequest> players, IReadOnlyList<GrenadeKit> grenadeKits, IRandom random)`
  - `(string? Primary, string Secondary) ResolveWeapons(RoundTypeDefinition definition, LoadoutRequest request)` — préférence valide pour le camp, sinon défaut.
  - `IReadOnlySet<PlayerId> PickAwpRecipients(AwpSettings settings, IReadOnlyList<LoadoutRequest> players, IRandom random)` — désactivé ou moins de `MinActivePlayers` joueurs ⇒ vide ; sinon par camp, jusqu'à `MaxPerTeam` volontaires mélangés, chacun avec `Chance`.
  - `IReadOnlySet<PlayerId> PickDefuseKits(DefuseKitSettings settings, IReadOnlyList<PlayerId> cts, IRandom random)` — `All` / `Quota` / `Chance`, puis minimum garanti si demandé.
  - `IReadOnlyList<string> PickGrenades(IReadOnlyList<GrenadeKit> kits, TeamSide team, IRandom random)` — kit uniforme parmi ceux du camp ou des deux camps ; aucun ⇒ liste vide.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Loadouts/LoadoutPlannerTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Loadouts;

public class LoadoutPlannerTests
{
    private static readonly RoundTypeDefinition FullBuy = new(
        "FullBuy", ArmorKind.KevlarHelmet,
        new TeamWeapons(new[] { "weapon_ak47" }, new[] { "weapon_m4a1", "weapon_m4a1_silencer" }, new[] { "weapon_ssg08" }),
        new TeamWeapons(new[] { "weapon_glock" }, new[] { "weapon_usp_silencer" }, new[] { "weapon_deagle" }),
        new TeamDefault("weapon_ak47", "weapon_deagle"),
        new TeamDefault("weapon_m4a1", "weapon_deagle"),
        new AwpSettings(true, 1, 5, 30),
        new DefuseKitSettings(DefuseKitMode.All, 1, 100, false),
        new ZeusSettings(false, 20),
        "Default");

    private static readonly RoundTypeDefinition Pistol = FullBuy with
    {
        Name = "Pistol",
        Armor = ArmorKind.Kevlar,
        Primaries = TeamWeapons.Empty,
        DefaultT = new TeamDefault(null, "weapon_glock"),
        DefaultCT = new TeamDefault(null, "weapon_usp_silencer"),
        Awp = new AwpSettings(false, 1, 5, 30),
        DefuseKit = new DefuseKitSettings(DefuseKitMode.Chance, 1, 34.44444, true),
    };

    private static LoadoutRequest Req(int slot, TeamSide team, LoadoutPreference? preference = null) =>
        new(new PlayerId(slot), team, preference);

    private static IReadOnlyList<LoadoutRequest> Lobby(int t, int ct, LoadoutPreference? preference = null) =>
        Enumerable.Range(1, t).Select(i => Req(i, TeamSide.T, preference))
            .Concat(Enumerable.Range(100, ct).Select(i => Req(i, TeamSide.CT, preference)))
            .ToList();

    [Fact]
    public void ResolveWeapons_WithoutPreference_UsesDefaults()
    {
        Assert.Equal(("weapon_m4a1", "weapon_deagle"), LoadoutPlanner.ResolveWeapons(FullBuy, Req(1, TeamSide.CT)));
        Assert.Equal(((string?)null, "weapon_glock"), LoadoutPlanner.ResolveWeapons(Pistol, Req(1, TeamSide.T)));
    }

    [Fact]
    public void ResolveWeapons_ValidPreference_IsUsed()
    {
        var preference = new LoadoutPreference("weapon_m4a1_silencer", "weapon_usp_silencer", false);
        Assert.Equal(("weapon_m4a1_silencer", "weapon_usp_silencer"), LoadoutPlanner.ResolveWeapons(FullBuy, Req(1, TeamSide.CT, preference)));
    }

    [Fact]
    public void ResolveWeapons_PreferenceFromOtherSideOrUnknown_FallsBackToDefault()
    {
        var preference = new LoadoutPreference("weapon_m4a1", "weapon_tec9", false);
        Assert.Equal(("weapon_ak47", "weapon_deagle"), LoadoutPlanner.ResolveWeapons(FullBuy, Req(1, TeamSide.T, preference)));
    }

    [Fact]
    public void ResolveWeapons_AnyPoolWeapon_IsAllowedForBothSides()
    {
        var preference = new LoadoutPreference("weapon_ssg08", null, false);
        Assert.Equal("weapon_ssg08", LoadoutPlanner.ResolveWeapons(FullBuy, Req(1, TeamSide.T, preference)).Primary);
    }

    [Fact]
    public void Awp_Disabled_GivesNone() =>
        Assert.Empty(LoadoutPlanner.PickAwpRecipients(Pistol.Awp, Lobby(3, 3, new LoadoutPreference(null, null, true)), new FixedRandom()));

    [Fact]
    public void Awp_BelowMinimumPlayers_GivesNone() =>
        Assert.Empty(LoadoutPlanner.PickAwpRecipients(FullBuy.Awp, Lobby(2, 2, new LoadoutPreference(null, null, true)), new FixedRandom()));

    [Fact]
    public void Awp_VolunteerWinningTheRoll_GetsIt_OnePerTeam()
    {
        var recipients = LoadoutPlanner.PickAwpRecipients(FullBuy.Awp, Lobby(3, 3, new LoadoutPreference(null, null, true)), new FixedRandom { DoubleValue = 0.0 });
        Assert.Equal(2, recipients.Count);
        Assert.Single(recipients, p => p.Slot < 100);
        Assert.Single(recipients, p => p.Slot >= 100);
    }

    [Fact]
    public void Awp_LosingTheRoll_GivesNone() =>
        Assert.Empty(LoadoutPlanner.PickAwpRecipients(FullBuy.Awp, Lobby(3, 3, new LoadoutPreference(null, null, true)), new FixedRandom { DoubleValue = 0.99 }));

    [Fact]
    public void Awp_NonVolunteers_NeverGetIt() =>
        Assert.Empty(LoadoutPlanner.PickAwpRecipients(FullBuy.Awp, Lobby(3, 3), new FixedRandom { DoubleValue = 0.0 }));

    [Fact]
    public void Kits_All_GivesEveryCt()
    {
        var cts = new[] { new PlayerId(1), new PlayerId(2) };
        Assert.Equal(2, LoadoutPlanner.PickDefuseKits(FullBuy.DefuseKit, cts, new FixedRandom()).Count);
    }

    [Fact]
    public void Kits_Quota_GivesExactlyQuota()
    {
        var settings = new DefuseKitSettings(DefuseKitMode.Quota, 1, 100, false);
        var cts = new[] { new PlayerId(1), new PlayerId(2), new PlayerId(3) };
        Assert.Single(LoadoutPlanner.PickDefuseKits(settings, cts, new FixedRandom()));
    }

    [Fact]
    public void Kits_ChanceAllFailing_WithGuarantee_GivesOne()
    {
        var cts = new[] { new PlayerId(1), new PlayerId(2), new PlayerId(3) };
        Assert.Single(LoadoutPlanner.PickDefuseKits(Pistol.DefuseKit, cts, new FixedRandom { DoubleValue = 0.99 }));
    }

    [Fact]
    public void Kits_ChanceAllFailing_WithoutGuarantee_GivesNone()
    {
        var settings = Pistol.DefuseKit with { GuaranteeMinimum = false };
        Assert.Empty(LoadoutPlanner.PickDefuseKits(settings, new[] { new PlayerId(1) }, new FixedRandom { DoubleValue = 0.99 }));
    }

    [Fact]
    public void Kits_NoCt_GivesNone_EvenWithGuarantee() =>
        Assert.Empty(LoadoutPlanner.PickDefuseKits(Pistol.DefuseKit, Array.Empty<PlayerId>(), new FixedRandom()));

    [Fact]
    public void Grenades_PicksAmongSideAndSharedKits()
    {
        var kits = new[]
        {
            new GrenadeKit(null, new[] { "weapon_flashbang" }),
            new GrenadeKit(TeamSide.CT, new[] { "weapon_incgrenade" }),
            new GrenadeKit(TeamSide.T, new[] { "weapon_molotov" }),
        };
        Assert.Equal(new[] { "weapon_molotov" }, LoadoutPlanner.PickGrenades(kits, TeamSide.T, new FixedRandom(1)));
        Assert.Equal(new[] { "weapon_incgrenade" }, LoadoutPlanner.PickGrenades(kits, TeamSide.CT, new FixedRandom(1)));
    }

    [Fact]
    public void Grenades_NoEligibleKit_GivesNothing() =>
        Assert.Empty(LoadoutPlanner.PickGrenades(new[] { new GrenadeKit(TeamSide.CT, new[] { "weapon_flashbang" }) }, TeamSide.T, new FixedRandom()));

    [Fact]
    public void Plan_BuildsFullLoadouts_KitsOnlyForCts()
    {
        var plan = LoadoutPlanner.Plan(FullBuy, Lobby(2, 2), Array.Empty<GrenadeKit>(), new FixedRandom());
        Assert.Equal(4, plan.Count);
        Assert.All(plan.Where(p => p.Key.Slot < 100), p => Assert.False(p.Value.DefuseKit));
        Assert.All(plan.Where(p => p.Key.Slot >= 100), p => Assert.True(p.Value.DefuseKit));
        Assert.All(plan.Values, l => Assert.Equal(ArmorKind.KevlarHelmet, l.Armor));
        Assert.All(plan.Values, l => Assert.False(l.Zeus));
        Assert.Equal("weapon_ak47", plan[new PlayerId(1)].Primary);
    }

    [Fact]
    public void Plan_AwpRecipient_GetsAwpAsPrimary()
    {
        var plan = LoadoutPlanner.Plan(FullBuy, Lobby(3, 3, new LoadoutPreference(null, null, true)), Array.Empty<GrenadeKit>(), new FixedRandom { DoubleValue = 0.0 });
        Assert.Equal(2, plan.Values.Count(l => l.Primary == WeaponCatalog.Awp));
    }

    [Fact]
    public void Plan_ZeusEnabledAtFullChance_GivesZeus()
    {
        var definition = FullBuy with { Zeus = new ZeusSettings(true, 100) };
        var plan = LoadoutPlanner.Plan(definition, Lobby(1, 1), Array.Empty<GrenadeKit>(), new FixedRandom());
        Assert.All(plan.Values, l => Assert.True(l.Zeus));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`LoadoutPlanner` introuvable).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Loadouts/LoadoutPlanner.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Loadouts;

public static class LoadoutPlanner
{
    public static IReadOnlyDictionary<PlayerId, Loadout> Plan(
        RoundTypeDefinition definition, IReadOnlyList<LoadoutRequest> players, IReadOnlyList<GrenadeKit> grenadeKits, IRandom random)
    {
        var awp = PickAwpRecipients(definition.Awp, players, random);
        var cts = players.Where(p => p.Team == TeamSide.CT).Select(p => p.Player).ToList();
        var kits = PickDefuseKits(definition.DefuseKit, cts, random);
        return players.ToDictionary(
            p => p.Player,
            p => Build(definition, p, awp.Contains(p.Player), kits.Contains(p.Player), grenadeKits, random));
    }

    public static (string? Primary, string Secondary) ResolveWeapons(RoundTypeDefinition definition, LoadoutRequest request)
    {
        var fallback = definition.DefaultFor(request.Team);
        var preference = request.Preference;
        var primary = preference?.Primary is { } p && definition.Primaries.For(request.Team).Contains(p) ? p : fallback.Primary;
        var secondary = preference?.Secondary is { } s && definition.Secondaries.For(request.Team).Contains(s) ? s : fallback.Secondary;
        return (primary, secondary);
    }

    public static IReadOnlySet<PlayerId> PickAwpRecipients(AwpSettings settings, IReadOnlyList<LoadoutRequest> players, IRandom random)
    {
        var recipients = new HashSet<PlayerId>();
        if (!settings.Enabled || players.Count < settings.MinActivePlayers)
        {
            return recipients;
        }
        foreach (var side in new[] { TeamSide.T, TeamSide.CT })
        {
            var volunteers = random.Shuffle(players.Where(p => p.Team == side && p.Preference?.AwpOptIn == true).Select(p => p.Player));
            foreach (var volunteer in volunteers.Take(Math.Max(0, settings.MaxPerTeam)))
            {
                if (Chance.Roll(settings.Chance, random))
                {
                    recipients.Add(volunteer);
                }
            }
        }
        return recipients;
    }

    public static IReadOnlySet<PlayerId> PickDefuseKits(DefuseKitSettings settings, IReadOnlyList<PlayerId> cts, IRandom random)
    {
        var chosen = settings.Mode switch
        {
            DefuseKitMode.All => cts.ToHashSet(),
            DefuseKitMode.Quota => random.Shuffle(cts).Take(Math.Max(0, settings.Quota)).ToHashSet(),
            _ => cts.Where(_ => Chance.Roll(settings.Chance, random)).ToHashSet(),
        };
        if (settings.GuaranteeMinimum && chosen.Count == 0 && cts.Count > 0)
        {
            chosen.Add(random.Pick(cts));
        }
        return chosen;
    }

    public static IReadOnlyList<string> PickGrenades(IReadOnlyList<GrenadeKit> kits, TeamSide team, IRandom random)
    {
        var eligible = kits.Where(k => k.Team is null || k.Team == team).ToList();
        return eligible.Count == 0 ? Array.Empty<string>() : random.Pick(eligible).Grenades;
    }

    private static Loadout Build(
        RoundTypeDefinition definition, LoadoutRequest request, bool awp, bool kit, IReadOnlyList<GrenadeKit> grenadeKits, IRandom random)
    {
        var (primary, secondary) = ResolveWeapons(definition, request);
        var zeus = definition.Zeus.Enabled && Chance.Roll(definition.Zeus.Chance, random);
        return new Loadout(
            awp ? WeaponCatalog.Awp : primary,
            secondary,
            definition.Armor,
            kit,
            zeus,
            PickGrenades(grenadeKits, request.Team, random));
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Loadouts/LoadoutPlanner.cs tests/RetakeV4.Domain.Tests/Loadouts/LoadoutPlannerTests.cs
git commit -m "feat: planificateur d'équipement (armes, AWP, kits, Zeus, grenades)"
```

---

### Task 4: Définitions complètes des types de round dans `roundtypes.json`

**Files:**
- Create: `src/RetakeV4/Modules/RoundTypes/RoundTypeDefinitionConfig.cs`, `RoundTypeDefaults.cs`, `RoundTypeDefinitionValidation.cs`
- Modify: `src/RetakeV4/Modules/RoundTypes/RoundTypesConfig.cs` (supprimer l'ancien `RoundTypeDefinitionConfig`, défauts via `RoundTypeDefaults`, `ToDefinitions()`), `RoundTypesConfigValidator.cs` (nettoyage de chaque définition), `RoundTypesModule.cs`
- Modify: `src/RetakeV4.Domain/RoundTypes/RoundTypeStep.cs`, `src/RetakeV4.Domain/Rounds/PreparationContext.cs`
- Test: `tests/RetakeV4.Domain.Tests/RoundTypes/RoundTypeSelectorTests.cs`, `tests/RetakeV4.Integration.Tests/Modules/RoundTypes/RoundTypeDefinitionValidationTests.cs`

**Interfaces:**
- Consumes: modèle Loadouts (Task 2), `WeaponCatalog` (Task 2).
- Produces:
  - `PreparationContext.RoundTypeDefinition` (`RoundTypeDefinition?`, init).
  - `RoundTypeStep(RoundTypeRules rules, IReadOnlyDictionary<string, RoundTypeDefinition> definitions, IRandom random)` : écrit `RoundType` **et** `RoundTypeDefinition` (null si nom inconnu).
  - Config (namespace `RetakeV4.Modules.RoundTypes`) : `WeaponPoolConfig`, `DefaultWeaponsConfig`, `TeamDefaultsConfig`, `AwpConfig`, `DefuseKitConfig`, `ZeusConfig`, `RoundTypeDefinitionConfig` (avec `RoundTypeDefinition ToDomain()`), `static class RoundTypeDefaults { Pistol(); Mid(); FullBuy(); }` (valeurs V3), `static class RoundTypeDefinitionValidation { RoundTypeDefinitionConfig Clean(RoundTypeDefinitionConfig definition, string file, List<ConfigIssue> issues); }`.
  - `RoundTypesConfig.ToDefinitions()` → `IReadOnlyDictionary<string, RoundTypeDefinition>`.
  - `RoundTypesConfig.Version` passe à **2** (nouveaux champs).

- [ ] **Step 1: Écrire les tests Domain qui échouent**

Dans `tests/RetakeV4.Domain.Tests/RoundTypes/RoundTypeSelectorTests.cs`, ajouter `using RetakeV4.Domain.Loadouts;` et remplacer `Step_WritesRoundTypeIntoContext` par :
```csharp
    private static RoundTypeDefinition Definition(string name) => new(
        name, ArmorKind.Kevlar, TeamWeapons.Empty, TeamWeapons.Empty,
        new TeamDefault(null, "weapon_glock"), new TeamDefault(null, "weapon_usp_silencer"),
        new AwpSettings(false, 1, 5, 30), new DefuseKitSettings(DefuseKitMode.All, 1, 100, false), new ZeusSettings(false, 20), "Default");

    [Fact]
    public void Step_WritesRoundTypeAndDefinitionIntoContext()
    {
        var definitions = new Dictionary<string, RoundTypeDefinition> { ["Mid"] = Definition("Mid") };
        var step = new RoundTypeStep(Default, definitions, new FixedRandom());
        var result = step.Execute(new PreparationContext(4) { RoundsPlayed = 3 });
        Assert.Equal("Mid", result.RoundType);
        Assert.Equal("Mid", result.RoundTypeDefinition?.Name);
        Assert.Equal(PreparationOrder.RoundType, step.Order);
        Assert.Equal("round_type", step.Name);
    }

    [Fact]
    public void Step_UnknownDefinition_LeavesDefinitionNull()
    {
        var step = new RoundTypeStep(Default, new Dictionary<string, RoundTypeDefinition>(), new FixedRandom());
        Assert.Null(step.Execute(new PreparationContext(1)).RoundTypeDefinition);
    }
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (constructeur à 3 paramètres et `RoundTypeDefinition` du contexte introuvables).

- [ ] **Step 2: Implémentation Domain**

Dans `src/RetakeV4.Domain/Rounds/PreparationContext.cs`, ajouter `using RetakeV4.Domain.Loadouts;` et la propriété :
```csharp
    public RoundTypeDefinition? RoundTypeDefinition { get; init; }
```

`src/RetakeV4.Domain/RoundTypes/RoundTypeStep.cs` devient :
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.RoundTypes;

public sealed class RoundTypeStep : IPreparationStep
{
    private readonly RoundTypeRules _rules;
    private readonly IReadOnlyDictionary<string, RoundTypeDefinition> _definitions;
    private readonly IRandom _random;

    public RoundTypeStep(RoundTypeRules rules, IReadOnlyDictionary<string, RoundTypeDefinition> definitions, IRandom random)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(random);
        _rules = rules;
        _definitions = definitions;
        _random = random;
    }

    public string Name => "round_type";

    public int Order => PreparationOrder.RoundType;

    public PreparationContext Execute(PreparationContext context)
    {
        var roundType = RoundTypeSelector.Select(_rules, context.RoundsPlayed, _random);
        return context with
        {
            RoundType = roundType,
            RoundTypeDefinition = _definitions.GetValueOrDefault(roundType),
        };
    }
}
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo` → Expected: `Failed: 0` (la solution entière ne compile pas encore : `RoundTypesModule` utilise l'ancien constructeur, corrigé au Step 5).

- [ ] **Step 3: Écrire les tests de validation qui échouent**

`tests/RetakeV4.Integration.Tests/Modules/RoundTypes/RoundTypeDefinitionValidationTests.cs`
```csharp
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Modules.RoundTypes;

namespace RetakeV4.Integration.Tests.Modules.RoundTypes;

public class RoundTypeDefinitionValidationTests
{
    private static RoundTypeDefinitionConfig Clean(RoundTypeDefinitionConfig definition, List<ConfigIssue> issues) =>
        RoundTypeDefinitionValidation.Clean(definition, "roundtypes.json", issues);

    [Theory]
    [MemberData(nameof(DefaultDefinitions))]
    public void V3Defaults_AreValid(RoundTypeDefinitionConfig definition)
    {
        var issues = new List<ConfigIssue>();
        Clean(definition, issues);
        Assert.Empty(issues);
    }

    public static IEnumerable<object[]> DefaultDefinitions() => new[]
    {
        new object[] { RoundTypeDefaults.Pistol() },
        new object[] { RoundTypeDefaults.Mid() },
        new object[] { RoundTypeDefaults.FullBuy() },
    };

    [Fact]
    public void V3Defaults_MatchV3Loadouts()
    {
        var fullBuy = RoundTypeDefaults.FullBuy().ToDomain();
        Assert.Equal(new TeamDefault("weapon_ak47", "weapon_deagle"), fullBuy.DefaultFor(TeamSide.T));
        Assert.Equal(new TeamDefault("weapon_m4a1", "weapon_deagle"), fullBuy.DefaultFor(TeamSide.CT));
        Assert.True(fullBuy.Awp.Enabled);
        var pistol = RoundTypeDefaults.Pistol().ToDomain();
        Assert.Equal(ArmorKind.Kevlar, pistol.Armor);
        Assert.Equal(new TeamDefault(null, "weapon_usp_silencer"), pistol.DefaultFor(TeamSide.CT));
        Assert.Equal(DefuseKitMode.Chance, pistol.DefuseKit.Mode);
        Assert.True(pistol.DefuseKit.GuaranteeMinimum);
        Assert.Equal("weapon_mac10", RoundTypeDefaults.Mid().ToDomain().DefaultFor(TeamSide.T).Primary);
    }

    [Fact]
    public void UnknownOrMisplacedWeapons_AreRemoved()
    {
        var definition = RoundTypeDefaults.FullBuy() with
        {
            Primaries = new WeaponPoolConfig { T = new[] { "weapon_ak47", "weapon_ak48", "weapon_glock" } },
        };
        var issues = new List<ConfigIssue>();
        var cleaned = Clean(definition, issues);
        Assert.Equal(new[] { "weapon_ak47" }, cleaned.Primaries.T);
        Assert.Equal(2, issues.Count(i => i.Key.Contains("Primaries")));
    }

    [Fact]
    public void DefaultOutsideOfPool_IsReplacedByFirstPoolWeapon()
    {
        var definition = RoundTypeDefaults.FullBuy() with
        {
            Defaults = new TeamDefaultsConfig
            {
                T = new DefaultWeaponsConfig { Primary = "weapon_m4a1", Secondary = "weapon_usp_silencer" },
                CT = RoundTypeDefaults.FullBuy().Defaults.CT,
            },
        };
        var issues = new List<ConfigIssue>();
        var cleaned = Clean(definition, issues);
        Assert.Equal("weapon_ak47", cleaned.Defaults.T.Primary);
        Assert.Equal("weapon_glock", cleaned.Defaults.T.Secondary);
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public void OutOfRangeNumbers_AreClamped()
    {
        var definition = RoundTypeDefaults.FullBuy() with
        {
            Awp = new AwpConfig { Enabled = true, MaxPerTeam = -1, MinActivePlayers = -3, Chance = 150 },
            DefuseKit = new DefuseKitConfig { Mode = DefuseKitMode.Quota, Quota = -2, Chance = -10 },
            Zeus = new ZeusConfig { Enabled = true, Chance = 101 },
        };
        var issues = new List<ConfigIssue>();
        var cleaned = Clean(definition, issues);
        Assert.Equal(0, cleaned.Awp.MaxPerTeam);
        Assert.Equal(0, cleaned.Awp.MinActivePlayers);
        Assert.Equal(100, cleaned.Awp.Chance);
        Assert.Equal(0, cleaned.DefuseKit.Quota);
        Assert.Equal(0, cleaned.DefuseKit.Chance);
        Assert.Equal(100, cleaned.Zeus.Chance);
        Assert.Equal(6, issues.Count);
    }

    [Fact]
    public void NullSections_AreReplacedByDefaults()
    {
        var definition = RoundTypeDefaults.Mid() with { Primaries = null!, Defaults = null!, Awp = null!, GrenadePool = " " };
        var issues = new List<ConfigIssue>();
        var cleaned = Clean(definition, issues);
        Assert.NotNull(cleaned.Primaries);
        Assert.NotNull(cleaned.Defaults.T);
        Assert.NotNull(cleaned.Awp);
        Assert.Equal("Default", cleaned.GrenadePool);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void RoundTypesConfig_ExposesDefinitionsByName()
    {
        var definitions = new RoundTypesConfig().ToDefinitions();
        Assert.Equal(new[] { "Pistol", "Mid", "FullBuy" }, definitions.Keys);
        Assert.Equal("FullBuy", definitions["FullBuy"].Name);
    }
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`RoundTypeDefaults`, `RoundTypeDefinitionValidation`, `WeaponPoolConfig`… introuvables).

- [ ] **Step 4: Implémenter la config des définitions**

`src/RetakeV4/Modules/RoundTypes/RoundTypeDefinitionConfig.cs`
```csharp
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.RoundTypes;

public sealed record WeaponPoolConfig
{
    public IReadOnlyList<string> T { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CT { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Any { get; init; } = Array.Empty<string>();

    public TeamWeapons ToDomain() => new(T, CT, Any);
}

public sealed record DefaultWeaponsConfig
{
    public string? Primary { get; init; }

    public string Secondary { get; init; } = "weapon_deagle";

    public TeamDefault ToDomain() => new(Primary, Secondary);
}

public sealed record TeamDefaultsConfig
{
    public DefaultWeaponsConfig T { get; init; } = new();

    public DefaultWeaponsConfig CT { get; init; } = new();
}

public sealed record AwpConfig
{
    public bool Enabled { get; init; }

    public int MaxPerTeam { get; init; } = 1;

    public int MinActivePlayers { get; init; } = 5;

    public double Chance { get; init; } = 30;

    public AwpSettings ToDomain() => new(Enabled, MaxPerTeam, MinActivePlayers, Chance);
}

public sealed record DefuseKitConfig
{
    public DefuseKitMode Mode { get; init; } = DefuseKitMode.All;

    public int Quota { get; init; } = 1;

    public double Chance { get; init; } = 100;

    public bool GuaranteeMinimum { get; init; }

    public DefuseKitSettings ToDomain() => new(Mode, Quota, Chance, GuaranteeMinimum);
}

public sealed record ZeusConfig
{
    public bool Enabled { get; init; }

    public double Chance { get; init; } = 20;

    public ZeusSettings ToDomain() => new(Enabled, Chance);
}

public sealed record RoundTypeDefinitionConfig
{
    public string Name { get; init; } = string.Empty;

    public ArmorKind Armor { get; init; } = ArmorKind.KevlarHelmet;

    public WeaponPoolConfig Primaries { get; init; } = new();

    public WeaponPoolConfig Secondaries { get; init; } = new();

    public TeamDefaultsConfig Defaults { get; init; } = new();

    public AwpConfig Awp { get; init; } = new();

    public DefuseKitConfig DefuseKit { get; init; } = new();

    public ZeusConfig Zeus { get; init; } = new();

    public string GrenadePool { get; init; } = "Default";

    public RoundTypeDefinition ToDomain() => new(
        Name, Armor, Primaries.ToDomain(), Secondaries.ToDomain(),
        Defaults.T.ToDomain(), Defaults.CT.ToDomain(),
        Awp.ToDomain(), DefuseKit.ToDomain(), Zeus.ToDomain(), GrenadePool);
}
```

`src/RetakeV4/Modules/RoundTypes/RoundTypeDefaults.cs`
```csharp
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.RoundTypes;

public static class RoundTypeDefaults
{
    private static readonly WeaponPoolConfig V3Secondaries = new()
    {
        T = new[] { "weapon_glock", "weapon_tec9" },
        CT = new[] { "weapon_usp_silencer", "weapon_hkp2000", "weapon_fiveseven" },
        Any = new[] { "weapon_deagle", "weapon_p250", "weapon_cz75a", "weapon_elite", "weapon_revolver" },
    };

    public static RoundTypeDefinitionConfig Pistol() => new()
    {
        Name = "Pistol",
        Armor = ArmorKind.Kevlar,
        Secondaries = V3Secondaries,
        Defaults = new TeamDefaultsConfig
        {
            T = new DefaultWeaponsConfig { Secondary = "weapon_glock" },
            CT = new DefaultWeaponsConfig { Secondary = "weapon_usp_silencer" },
        },
        DefuseKit = new DefuseKitConfig { Mode = DefuseKitMode.Chance, Chance = 34.44444, GuaranteeMinimum = true },
    };

    public static RoundTypeDefinitionConfig Mid() => new()
    {
        Name = "Mid",
        Primaries = new WeaponPoolConfig
        {
            T = new[] { "weapon_mac10", "weapon_galilar", "weapon_sg556" },
            CT = new[] { "weapon_mp9", "weapon_famas", "weapon_aug" },
            Any = new[] { "weapon_p90", "weapon_mp5sd", "weapon_ump45", "weapon_bizon", "weapon_mp7" },
        },
        Secondaries = V3Secondaries,
        Defaults = new TeamDefaultsConfig
        {
            T = new DefaultWeaponsConfig { Primary = "weapon_mac10", Secondary = "weapon_deagle" },
            CT = new DefaultWeaponsConfig { Primary = "weapon_mp9", Secondary = "weapon_deagle" },
        },
    };

    public static RoundTypeDefinitionConfig FullBuy() => new()
    {
        Name = "FullBuy",
        Primaries = new WeaponPoolConfig
        {
            T = new[] { "weapon_ak47", "weapon_galilar", "weapon_sg556", "weapon_mac10", "weapon_g3sg1", "weapon_sawedoff" },
            CT = new[] { "weapon_m4a1", "weapon_m4a1_silencer", "weapon_famas", "weapon_aug", "weapon_mp9", "weapon_scar20", "weapon_mag7" },
            Any = new[] { "weapon_mp7", "weapon_mp5sd", "weapon_ump45", "weapon_p90", "weapon_bizon", "weapon_ssg08", "weapon_nova", "weapon_xm1014", "weapon_m249", "weapon_negev" },
        },
        Secondaries = V3Secondaries,
        Defaults = new TeamDefaultsConfig
        {
            T = new DefaultWeaponsConfig { Primary = "weapon_ak47", Secondary = "weapon_deagle" },
            CT = new DefaultWeaponsConfig { Primary = "weapon_m4a1", Secondary = "weapon_deagle" },
        },
        Awp = new AwpConfig { Enabled = true },
    };
}
```

`src/RetakeV4/Modules/RoundTypes/RoundTypeDefinitionValidation.cs`
```csharp
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.RoundTypes;

public static class RoundTypeDefinitionValidation
{
    public static RoundTypeDefinitionConfig Clean(RoundTypeDefinitionConfig definition, string file, List<ConfigIssue> issues)
    {
        var key = $"RoundTypes[{definition.Name}]";
        var primaries = CleanPool(definition.Primaries, WeaponCatalog.IsPrimary, $"{key}.Primaries", file, issues);
        var secondaries = CleanPool(definition.Secondaries, WeaponCatalog.IsSecondary, $"{key}.Secondaries", file, issues);
        var defaults = definition.Defaults ?? Missing(new TeamDefaultsConfig(), $"{key}.Defaults", file, issues);
        var context = new PoolContext(primaries.ToDomain(), secondaries.ToDomain(), key, file, issues);
        return definition with
        {
            Primaries = primaries,
            Secondaries = secondaries,
            Defaults = new TeamDefaultsConfig
            {
                T = CleanDefault(defaults.T, TeamSide.T, context),
                CT = CleanDefault(defaults.CT, TeamSide.CT, context),
            },
            Awp = CleanAwp(definition.Awp ?? Missing(new AwpConfig(), $"{key}.Awp", file, issues), key, file, issues),
            DefuseKit = CleanKit(definition.DefuseKit ?? Missing(new DefuseKitConfig(), $"{key}.DefuseKit", file, issues), key, file, issues),
            Zeus = CleanZeus(definition.Zeus ?? Missing(new ZeusConfig(), $"{key}.Zeus", file, issues), key, file, issues),
            GrenadePool = string.IsNullOrWhiteSpace(definition.GrenadePool)
                ? Missing("Default", $"{key}.GrenadePool", file, issues)
                : definition.GrenadePool,
        };
    }

    private sealed record PoolContext(TeamWeapons Primaries, TeamWeapons Secondaries, string Key, string File, List<ConfigIssue> Issues);

    private static WeaponPoolConfig CleanPool(WeaponPoolConfig? pool, Func<string?, bool> isValid, string key, string file, List<ConfigIssue> issues)
    {
        var source = pool ?? Missing(new WeaponPoolConfig(), key, file, issues);
        IReadOnlyList<string> Keep(IReadOnlyList<string>? weapons, string side) =>
            (weapons ?? Array.Empty<string>()).Where(w =>
            {
                if (isValid(w))
                {
                    return true;
                }
                issues.Add(new ConfigIssue(file, $"{key}.{side}", $"'{w}' is not a valid weapon for this slot; removed"));
                return false;
            }).ToList();
        return new WeaponPoolConfig { T = Keep(source.T, "T"), CT = Keep(source.CT, "CT"), Any = Keep(source.Any, "Any") };
    }

    private static DefaultWeaponsConfig CleanDefault(DefaultWeaponsConfig? weapons, TeamSide side, PoolContext context)
    {
        var key = $"{context.Key}.Defaults.{side}";
        var source = weapons ?? Missing(new DefaultWeaponsConfig(), key, context.File, context.Issues);
        var primaryPool = context.Primaries.For(side);
        var secondaryPool = context.Secondaries.For(side);
        var primary = source.Primary is null || primaryPool.Contains(source.Primary)
            ? source.Primary
            : Replace(source.Primary, primaryPool.FirstOrDefault(), $"{key}.Primary", context);
        var secondary = secondaryPool.Contains(source.Secondary) || secondaryPool.Count == 0
            ? source.Secondary
            : Replace(source.Secondary, secondaryPool[0], $"{key}.Secondary", context)!;
        return new DefaultWeaponsConfig { Primary = primary, Secondary = secondary };
    }

    private static string? Replace(string? invalid, string? replacement, string key, PoolContext context)
    {
        context.Issues.Add(new ConfigIssue(context.File, key, $"'{invalid}' is not in the pool; using '{replacement ?? "none"}'"));
        return replacement;
    }

    private static AwpConfig CleanAwp(AwpConfig awp, string key, string file, List<ConfigIssue> issues) => awp with
    {
        MaxPerTeam = AtLeastZero(awp.MaxPerTeam, $"{key}.Awp.MaxPerTeam", file, issues),
        MinActivePlayers = AtLeastZero(awp.MinActivePlayers, $"{key}.Awp.MinActivePlayers", file, issues),
        Chance = Percent(awp.Chance, $"{key}.Awp.Chance", file, issues),
    };

    private static DefuseKitConfig CleanKit(DefuseKitConfig kit, string key, string file, List<ConfigIssue> issues) => kit with
    {
        Quota = AtLeastZero(kit.Quota, $"{key}.DefuseKit.Quota", file, issues),
        Chance = Percent(kit.Chance, $"{key}.DefuseKit.Chance", file, issues),
    };

    private static ZeusConfig CleanZeus(ZeusConfig zeus, string key, string file, List<ConfigIssue> issues) =>
        zeus with { Chance = Percent(zeus.Chance, $"{key}.Zeus.Chance", file, issues) };

    private static int AtLeastZero(int value, string key, string file, List<ConfigIssue> issues)
    {
        if (value >= 0)
        {
            return value;
        }
        issues.Add(new ConfigIssue(file, key, $"{value} is negative; using 0"));
        return 0;
    }

    private static double Percent(double value, string key, string file, List<ConfigIssue> issues)
    {
        var clamped = Math.Clamp(value, 0d, 100d);
        if (!clamped.Equals(value))
        {
            issues.Add(new ConfigIssue(file, key, $"{value} is outside 0-100; using {clamped}"));
        }
        return clamped;
    }

    private static T Missing<T>(T replacement, string key, string file, List<ConfigIssue> issues)
    {
        issues.Add(new ConfigIssue(file, key, "missing; using defaults"));
        return replacement;
    }
}
```

Dans `src/RetakeV4/Modules/RoundTypes/RoundTypesConfig.cs` :
- supprimer l'ancien `RoundTypeDefinitionConfig` (déplacé) ;
- constructeur : `public RoundTypesConfig() => Version = 2;`
- valeur par défaut de `RoundTypes` :
```csharp
    public IReadOnlyList<RoundTypeDefinitionConfig> RoundTypes { get; init; } = new[]
    {
        RoundTypeDefaults.Pistol(),
        RoundTypeDefaults.Mid(),
        RoundTypeDefaults.FullBuy(),
    };
```
- ajouter :
```csharp
    public IReadOnlyDictionary<string, RoundTypeDefinition> ToDefinitions() =>
        RoundTypes.ToDictionary(r => r.Name, r => r.ToDomain(), StringComparer.Ordinal);
```
(avec `using RetakeV4.Domain.Loadouts;`).

Dans `src/RetakeV4/Modules/RoundTypes/RoundTypesConfigValidator.cs`, `CleanRoundTypes` ajoute la définition nettoyée : remplacer `kept.Add(roundType);` par `kept.Add(RoundTypeDefinitionValidation.Clean(roundType, file, issues));`.

Dans `src/RetakeV4/Modules/RoundTypes/RoundTypesModule.cs`, remplacer l'enregistrement de l'étape par :
```csharp
        context.Hooks.PreparationStep(new RoundTypeStep(_config.ToRules(), _config.ToDefinitions(), SystemRandom.Shared));
```

- [ ] **Step 5: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0` (les tests existants de `RoundTypesConfigValidatorTests` passent : `Def(name)` construit une définition vide mais valide — pools vides, défaut secondaire conservé).

- [ ] **Step 6: Commit**

```bash
git add src/RetakeV4.Domain/RoundTypes/RoundTypeStep.cs src/RetakeV4.Domain/Rounds/PreparationContext.cs src/RetakeV4/Modules/RoundTypes tests/RetakeV4.Domain.Tests/RoundTypes tests/RetakeV4.Integration.Tests/Modules/RoundTypes
git commit -m "feat: définitions complètes des types de round (armes, armure, AWP, kits, Zeus, grenades)"
```

---

### Task 5: Module `Allocation` (grenades, application de l'équipement)

**Files:**
- Create: `src/RetakeV4/Modules/Allocation/GrenadesConfig.cs`, `GrenadesConfigValidator.cs`, `AllocationConfig.cs`, `LoadoutApplier.cs`, `AllocationModule.cs`
- Modify: `src/RetakeV4/RetakeV4Plugin.cs`
- Test: `tests/RetakeV4.Integration.Tests/Modules/Allocation/GrenadesConfigTests.cs`

**Interfaces:**
- Consumes: `LoadoutPlanner.Plan`, `GrenadeKit`, `Loadout`, `ArmorKind`, `WeaponCatalog.IsGrenade` (Tasks 2-3) ; `PreparationContext.RoundTypeDefinition` (Task 4) ; `PlayerQueries`, `ModuleHooks`, `DelegatePreparationStep`, `PreparationOrder.Loadout`.
- Produces:
  - `enum GrenadeTeam { Any, T, CT }`, `sealed record GrenadeKitConfig { GrenadeTeam Team; IReadOnlyList<string> Grenades; GrenadeKit ToDomain(); }`
  - `sealed record GrenadesConfig : ModuleConfig` (fichier `grenades.json`, Version 1) avec `IReadOnlyDictionary<string, IReadOnlyList<GrenadeKitConfig>> Pools` (pool `Default` = les 16 kits V3) et `IReadOnlyList<GrenadeKit> KitsFor(string pool)` (pool inconnu ⇒ liste vide).
  - `sealed class GrenadesConfigValidator : IConfigValidator<GrenadesConfig>` (grenades inconnues et entrées `null` retirées, `Pools` absent ⇒ défauts).
  - `sealed record AllocationConfig : ModuleConfig` (fichier `allocation.json`, Version 1 ; aucun champ propre en 2b).
  - `internal static class LoadoutApplier { void Apply(CCSPlayerController player, Loadout loadout); }` — retire tout sauf couteaux/baïonnettes, remet l'armure à zéro, donne grenades, secondaire, primaire, Zeus, kit (CT), armure, puis `slot3`, `slot2`, `slot1`.
  - Module `Allocation` (dépend de `Core`, `RoundTypes`) : étape `loadout` (Order 50).

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/Modules/Allocation/GrenadesConfigTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Modules.Allocation;

namespace RetakeV4.Integration.Tests.Modules.Allocation;

public class GrenadesConfigTests
{
    private static readonly GrenadesConfig Defaults = new();
    private readonly GrenadesConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid_AndContainTheSixteenV3Kits()
    {
        var result = _validator.Validate(Defaults, Defaults, "grenades.json");
        Assert.Empty(result.Issues);
        var kits = result.Config.KitsFor("Default");
        Assert.Equal(16, kits.Count);
        Assert.Equal(6, kits.Count(k => k.Team is null));
        Assert.Equal(7, kits.Count(k => k.Team == TeamSide.CT));
        Assert.Equal(3, kits.Count(k => k.Team == TeamSide.T));
        Assert.Contains(kits, k => k.Grenades.Count == 0);
    }

    [Fact]
    public void UnknownPool_GivesNoKits() => Assert.Empty(Defaults.KitsFor("Nope"));

    [Fact]
    public void UnknownGrenades_AndNullKits_AreRemoved()
    {
        var pools = new Dictionary<string, IReadOnlyList<GrenadeKitConfig>>
        {
            ["Default"] = new GrenadeKitConfig?[]
            {
                null,
                new GrenadeKitConfig { Team = GrenadeTeam.T, Grenades = new[] { "weapon_molotov", "weapon_nuke" } },
            }!,
        };
        var result = _validator.Validate(Defaults with { Pools = pools }, Defaults, "grenades.json");
        var kit = Assert.Single(result.Config.KitsFor("Default"));
        Assert.Equal(new[] { "weapon_molotov" }, kit.Grenades);
        Assert.Equal(TeamSide.T, kit.Team);
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void MissingPools_FallBackToDefaults()
    {
        var result = _validator.Validate(Defaults with { Pools = null! }, Defaults, "grenades.json");
        Assert.Equal(16, result.Config.KitsFor("Default").Count);
        Assert.Single(result.Issues);
    }
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Modules.Allocation` introuvable).

- [ ] **Step 2: Implémenter la config des grenades**

`src/RetakeV4/Modules/Allocation/GrenadesConfig.cs`
```csharp
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

public enum GrenadeTeam
{
    Any,
    T,
    CT,
}

public sealed record GrenadeKitConfig
{
    public GrenadeTeam Team { get; init; } = GrenadeTeam.Any;

    public IReadOnlyList<string> Grenades { get; init; } = Array.Empty<string>();

    public GrenadeKit ToDomain() => new(
        Team switch { GrenadeTeam.T => TeamSide.T, GrenadeTeam.CT => TeamSide.CT, _ => null },
        Grenades);
}

public sealed record GrenadesConfig : ModuleConfig
{
    private const string Smoke = "weapon_smokegrenade";
    private const string Flash = "weapon_flashbang";
    private const string He = "weapon_hegrenade";
    private const string Molotov = "weapon_molotov";
    private const string Incendiary = "weapon_incgrenade";

    public GrenadesConfig() => Version = 1;

    public IReadOnlyDictionary<string, IReadOnlyList<GrenadeKitConfig>> Pools { get; init; } =
        new Dictionary<string, IReadOnlyList<GrenadeKitConfig>> { ["Default"] = V3Kits() };

    public IReadOnlyList<GrenadeKit> KitsFor(string pool) =>
        Pools.TryGetValue(pool, out var kits) ? kits.Select(k => k.ToDomain()).ToList() : Array.Empty<GrenadeKit>();

    private static IReadOnlyList<GrenadeKitConfig> V3Kits() => new[]
    {
        Kit(GrenadeTeam.Any), Kit(GrenadeTeam.Any, Smoke), Kit(GrenadeTeam.Any, Flash), Kit(GrenadeTeam.Any, He),
        Kit(GrenadeTeam.Any, Smoke, Flash), Kit(GrenadeTeam.Any, He, Flash),
        Kit(GrenadeTeam.CT, Flash, Flash), Kit(GrenadeTeam.CT, Smoke, He), Kit(GrenadeTeam.CT, Incendiary), Kit(GrenadeTeam.CT, He),
        Kit(GrenadeTeam.CT, Flash), Kit(GrenadeTeam.CT, Incendiary, Flash), Kit(GrenadeTeam.CT, Smoke, Incendiary, Flash),
        Kit(GrenadeTeam.T, Molotov), Kit(GrenadeTeam.T, Molotov, Flash), Kit(GrenadeTeam.T, Smoke, Flash),
    };

    private static GrenadeKitConfig Kit(GrenadeTeam team, params string[] grenades) => new() { Team = team, Grenades = grenades };
}
```

`src/RetakeV4/Modules/Allocation/GrenadesConfigValidator.cs`
```csharp
using RetakeV4.Configuration;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

public sealed class GrenadesConfigValidator : IConfigValidator<GrenadesConfig>
{
    public ValidationResult<GrenadesConfig> Validate(GrenadesConfig config, GrenadesConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        if (config.Pools is null)
        {
            issues.Add(new ConfigIssue(file, nameof(GrenadesConfig.Pools), "missing; using defaults"));
            return new ValidationResult<GrenadesConfig>(config with { Pools = defaults.Pools }, issues);
        }
        var pools = config.Pools.ToDictionary(
            p => p.Key,
            p => CleanPool(p.Key, p.Value, file, issues));
        return new ValidationResult<GrenadesConfig>(config with { Pools = pools }, issues);
    }

    private static IReadOnlyList<GrenadeKitConfig> CleanPool(string name, IReadOnlyList<GrenadeKitConfig>? kits, string file, List<ConfigIssue> issues)
    {
        var kept = new List<GrenadeKitConfig>();
        foreach (var kit in kits ?? Array.Empty<GrenadeKitConfig>())
        {
            if (kit is null)
            {
                issues.Add(new ConfigIssue(file, $"Pools[{name}]", "null kit removed"));
                continue;
            }
            var grenades = (kit.Grenades ?? Array.Empty<string>()).Where(WeaponCatalog.IsGrenade).ToList();
            if (grenades.Count != (kit.Grenades?.Count ?? 0))
            {
                issues.Add(new ConfigIssue(file, $"Pools[{name}]", "unknown grenade(s) removed from a kit"));
            }
            kept.Add(kit with { Grenades = grenades });
        }
        return kept;
    }
}
```

`src/RetakeV4/Modules/Allocation/AllocationConfig.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Allocation;

public sealed record AllocationConfig : ModuleConfig
{
    public AllocationConfig() => Version = 1;
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo` → Expected: `Failed: 0`.

- [ ] **Step 3: Implémenter l'application et le module**

`src/RetakeV4/Modules/Allocation/LoadoutApplier.cs`
```csharp
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

internal static class LoadoutApplier
{
    private static readonly string[] SlotOrder = { "slot3", "slot2", "slot1" };

    public static void Apply(CCSPlayerController player, Loadout loadout)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid || pawn.ItemServices is null || pawn.WeaponServices is null)
        {
            return;
        }
        Strip(pawn);
        var items = new CCSPlayer_ItemServices(pawn.ItemServices.Handle);
        foreach (var grenade in loadout.Grenades)
        {
            player.GiveNamedItem(grenade);
        }
        player.GiveNamedItem(loadout.Secondary);
        if (loadout.Primary is { } primary)
        {
            player.GiveNamedItem(primary);
        }
        if (loadout.Zeus)
        {
            player.GiveNamedItem(CsItem.Taser);
        }
        if (loadout.DefuseKit)
        {
            items.HasDefuser = true;
        }
        GiveArmor(player, items, loadout.Armor);
        foreach (var slot in SlotOrder)
        {
            player.ExecuteClientCommand(slot);
        }
    }

    private static void Strip(CCSPlayerPawn pawn)
    {
        var weapons = pawn.WeaponServices!.MyWeapons
            .Select(handle => handle.Value)
            .Where(weapon => weapon is { IsValid: true } && !IsKnife(weapon.DesignerName))
            .ToList();
        foreach (var weapon in weapons)
        {
            weapon!.Remove();
        }
        pawn.ArmorValue = 0;
        new CCSPlayer_ItemServices(pawn.ItemServices!.Handle).HasHelmet = false;
    }

    private static bool IsKnife(string designerName) =>
        designerName.Contains("knife", StringComparison.Ordinal) || designerName.Contains("bayonet", StringComparison.Ordinal);

    private static void GiveArmor(CCSPlayerController player, CCSPlayer_ItemServices items, ArmorKind armor)
    {
        switch (armor)
        {
            case ArmorKind.Kevlar:
                player.GiveNamedItem(CsItem.Kevlar);
                break;
            case ArmorKind.KevlarHelmet:
                player.GiveNamedItem(CsItem.AssaultSuit);
                items.HasHelmet = true;
                break;
        }
    }
}
```

`src/RetakeV4/Modules/Allocation/AllocationModule.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Modules.Allocation;

public sealed class AllocationModule : IRetakeModule
{
    private readonly IRandom _random = SystemRandom.Shared;
    private readonly HashSet<string> _reportedMissingPools = new(StringComparer.Ordinal);
    private AllocationConfig _config = new();
    private GrenadesConfig _grenades = new();
    private ModuleContext? _context;

    public string Name => "Allocation";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core", "RoundTypes" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Allocation module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var grenades = store.Load("grenades.json", new GrenadesConfig(), new GrenadesConfigValidator());
        ConfigLogging.Report(logger, grenades.Issues);
        _grenades = grenades.Config;
        var result = store.Load("allocation.json", new AllocationConfig());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        context.Hooks.PreparationStep(new DelegatePreparationStep("loadout", PreparationOrder.Loadout, AssignLoadouts));
    }

    public void Unload() => _context = null;

    private PreparationContext AssignLoadouts(PreparationContext context)
    {
        if (context.RoundTypeDefinition is not { } definition)
        {
            return context;
        }
        var players = PlayerQueries.Humans()
            .Select(p => (Controller: p, Side: PlayerQueries.SideOf(p)))
            .Where(p => p.Side is not null)
            .ToList();
        var requests = players.Select(p => new LoadoutRequest(new PlayerId(p.Controller.Slot), p.Side!.Value, null)).ToList();
        var plan = LoadoutPlanner.Plan(definition, requests, GrenadeKits(definition.GrenadePool), _random);
        foreach (var (controller, _) in players)
        {
            LoadoutApplier.Apply(controller, plan[new PlayerId(controller.Slot)]);
        }
        if (_config.Debug)
        {
            Context.Logger.LogInformation("Round {Round}: {Count} loadout(s) applied for {RoundType}", context.RoundNumber, plan.Count, definition.Name);
        }
        return context;
    }

    private IReadOnlyList<GrenadeKit> GrenadeKits(string pool)
    {
        var kits = _grenades.KitsFor(pool);
        if (kits.Count == 0 && _reportedMissingPools.Add(pool))
        {
            Context.Logger.LogWarning("Grenade pool {Pool} is empty or missing in grenades.json: no grenades will be given", pool);
        }
        return kits;
    }
}
```

`src/RetakeV4/RetakeV4Plugin.cs` : `using RetakeV4.Modules.Allocation;` et ajouter `new AllocationModule(),` après `new SpawnsModule(),` dans `CreateModules`.

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`. Si `MyWeapons` n'expose pas `Select` directement (type `NetworkedVector<CHandle<CBasePlayerWeapon>>`), itérer avec `foreach` pour construire la liste ; ledger la ruling.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4/Modules/Allocation src/RetakeV4/RetakeV4Plugin.cs tests/RetakeV4.Integration.Tests/Modules/Allocation
git commit -m "feat: module Allocation (grenades, application de l'équipement par type de round)"
```

---

### Task 6: InstaDefuse — suivi des menaces et politique (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/InstaDefuse/ThreatState.cs`, `src/RetakeV4.Domain/InstaDefuse/InstaDefusePolicy.cs`
- Test: `tests/RetakeV4.Domain.Tests/InstaDefuse/ThreatStateTests.cs`, `tests/RetakeV4.Domain.Tests/InstaDefuse/InstaDefusePolicyTests.cs`

**Interfaces:**
- Consumes: `Vec3` (phase 1).
- Produces:
  - `sealed record ThreatState(int HeInFlight, int MolotovInFlight, ImmutableHashSet<int> NearInfernos)` avec `static Empty`, `GrenadeThrown(string weapon)` (`hegrenade` ; `molotov`/`incgrenade`, préfixe `weapon_` accepté), `HeDetonated()`, `MolotovDetonated()` (plancher 0), `InfernoStarted(int id, Vec3 fire, Vec3? bomb, float maxDistance)`, `InfernoEnded(int id)`.
  - `sealed record InstaDefuseRules(bool RequireNoTAlive, bool BlockOnHe, bool BlockOnMolotov, bool BlockOnInferno, bool ForceExplodeIfNoTime)`
  - `sealed record DefuseSituation(bool TerroristsAlive, float SecondsUntilExplosion, float DefuseLength, bool DefuserHasKit)`
  - `enum ThreatKind { He, Molotov, Inferno }`
  - `abstract record InstaDefuseDecision` avec `NotApplicable`, `Blocked(ThreatKind Threat)`, `NotEnoughTime(float MissingSeconds, bool ForceExplode)`, `Allowed(float SecondsLeft)`.
  - `static class InstaDefusePolicy { InstaDefuseDecision Evaluate(InstaDefuseRules rules, ThreatState threats, DefuseSituation situation); }` — durée de défuse = `DefuseLength` si 5 ou 10, sinon 5 (kit) / 10 (sans kit).

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/InstaDefuse/ThreatStateTests.cs`
```csharp
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.InstaDefuse;

namespace RetakeV4.Domain.Tests.InstaDefuse;

public class ThreatStateTests
{
    [Theory]
    [InlineData("hegrenade")]
    [InlineData("weapon_hegrenade")]
    public void HeThrown_IsTracked_UntilDetonation(string weapon)
    {
        var state = ThreatState.Empty.GrenadeThrown(weapon);
        Assert.Equal(1, state.HeInFlight);
        Assert.Equal(0, state.HeDetonated().HeInFlight);
    }

    [Theory]
    [InlineData("molotov")]
    [InlineData("incgrenade")]
    [InlineData("weapon_molotov")]
    public void FireGrenadeThrown_IsTracked_UntilDetonation(string weapon)
    {
        var state = ThreatState.Empty.GrenadeThrown(weapon);
        Assert.Equal(1, state.MolotovInFlight);
        Assert.Equal(0, state.MolotovDetonated().MolotovInFlight);
    }

    [Fact]
    public void OtherGrenades_AreIgnored() =>
        Assert.Equal(ThreatState.Empty, ThreatState.Empty.GrenadeThrown("flashbang"));

    [Fact]
    public void Detonations_NeverGoBelowZero()
    {
        var state = ThreatState.Empty.HeDetonated().MolotovDetonated();
        Assert.Equal(0, state.HeInFlight);
        Assert.Equal(0, state.MolotovInFlight);
    }

    [Fact]
    public void Inferno_NearTheBomb_IsTracked_UntilItEnds()
    {
        var state = ThreatState.Empty.InfernoStarted(7, new Vec3(100, 0, 0), new Vec3(0, 0, 0), 250f);
        Assert.Contains(7, state.NearInfernos);
        Assert.Empty(state.InfernoEnded(7).NearInfernos);
    }

    [Fact]
    public void Inferno_FarFromTheBomb_OrWithoutBomb_IsIgnored()
    {
        Assert.Empty(ThreatState.Empty.InfernoStarted(7, new Vec3(300, 0, 0), new Vec3(0, 0, 0), 250f).NearInfernos);
        Assert.Empty(ThreatState.Empty.InfernoStarted(7, new Vec3(0, 0, 0), null, 250f).NearInfernos);
    }
}
```

`tests/RetakeV4.Domain.Tests/InstaDefuse/InstaDefusePolicyTests.cs`
```csharp
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.InstaDefuse;

namespace RetakeV4.Domain.Tests.InstaDefuse;

public class InstaDefusePolicyTests
{
    private static readonly InstaDefuseRules Rules = new(RequireNoTAlive: true, BlockOnHe: true, BlockOnMolotov: true, BlockOnInferno: true, ForceExplodeIfNoTime: true);

    private static DefuseSituation Situation(bool tAlive = false, float secondsLeft = 20f, float defuseLength = 10f, bool kit = false) =>
        new(tAlive, secondsLeft, defuseLength, kit);

    [Fact]
    public void TerroristsAlive_IsNotApplicable() =>
        Assert.IsType<InstaDefuseDecision.NotApplicable>(InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(tAlive: true)));

    [Fact]
    public void TerroristsAlive_WithoutRequirement_IsEvaluated() =>
        Assert.IsType<InstaDefuseDecision.Allowed>(InstaDefusePolicy.Evaluate(Rules with { RequireNoTAlive = false }, ThreatState.Empty, Situation(tAlive: true)));

    [Fact]
    public void EnoughTime_IsAllowed_WithSecondsLeft()
    {
        var decision = Assert.IsType<InstaDefuseDecision.Allowed>(InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(secondsLeft: 12.5f)));
        Assert.Equal(12.5f, decision.SecondsLeft);
    }

    [Theory]
    [InlineData("hegrenade", ThreatKind.He)]
    [InlineData("molotov", ThreatKind.Molotov)]
    public void GrenadeInFlight_Blocks(string weapon, ThreatKind expected)
    {
        var decision = InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty.GrenadeThrown(weapon), Situation());
        Assert.Equal(expected, Assert.IsType<InstaDefuseDecision.Blocked>(decision).Threat);
    }

    [Fact]
    public void FireNearBomb_Blocks()
    {
        var threats = ThreatState.Empty.InfernoStarted(1, new Vec3(0, 0, 0), new Vec3(10, 0, 0), 250f);
        Assert.Equal(ThreatKind.Inferno, Assert.IsType<InstaDefuseDecision.Blocked>(InstaDefusePolicy.Evaluate(Rules, threats, Situation())).Threat);
    }

    [Fact]
    public void DisabledBlocks_AreIgnored()
    {
        var rules = Rules with { BlockOnHe = false, BlockOnMolotov = false, BlockOnInferno = false };
        var threats = ThreatState.Empty.GrenadeThrown("hegrenade").GrenadeThrown("molotov")
            .InfernoStarted(1, new Vec3(0, 0, 0), new Vec3(0, 0, 0), 250f);
        Assert.IsType<InstaDefuseDecision.Allowed>(InstaDefusePolicy.Evaluate(rules, threats, Situation()));
    }

    [Fact]
    public void NotEnoughTime_ReportsMissingSeconds_AndForcesExplosionWhenConfigured()
    {
        var decision = Assert.IsType<InstaDefuseDecision.NotEnoughTime>(InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(secondsLeft: 7f)));
        Assert.Equal(3f, decision.MissingSeconds, 3);
        Assert.True(decision.ForceExplode);
    }

    [Fact]
    public void NotEnoughTime_WithoutForceExplode_OnlyReports()
    {
        var decision = InstaDefusePolicy.Evaluate(Rules with { ForceExplodeIfNoTime = false }, ThreatState.Empty, Situation(secondsLeft: 7f));
        Assert.False(Assert.IsType<InstaDefuseDecision.NotEnoughTime>(decision).ForceExplode);
    }

    [Fact]
    public void Kit_ShortensDefuse_WhenEngineLengthIsUnusual()
    {
        var withKit = InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(secondsLeft: 7f, defuseLength: 0f, kit: true));
        var withoutKit = InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(secondsLeft: 7f, defuseLength: 0f, kit: false));
        Assert.IsType<InstaDefuseDecision.Allowed>(withKit);
        Assert.IsType<InstaDefuseDecision.NotEnoughTime>(withoutKit);
    }

    [Fact]
    public void EngineDefuseLength_IsTrusted_WhenStandard()
    {
        var decision = InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(secondsLeft: 7f, defuseLength: 5f, kit: false));
        Assert.IsType<InstaDefuseDecision.Allowed>(decision);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Domain.InstaDefuse` introuvable).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/InstaDefuse/ThreatState.cs`
```csharp
using System.Collections.Immutable;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.InstaDefuse;

public sealed record ThreatState(int HeInFlight, int MolotovInFlight, ImmutableHashSet<int> NearInfernos)
{
    private const string WeaponPrefix = "weapon_";

    public static ThreatState Empty { get; } = new(0, 0, ImmutableHashSet<int>.Empty);

    public ThreatState GrenadeThrown(string weapon) => Normalize(weapon) switch
    {
        "hegrenade" => this with { HeInFlight = HeInFlight + 1 },
        "molotov" or "incgrenade" => this with { MolotovInFlight = MolotovInFlight + 1 },
        _ => this,
    };

    public ThreatState HeDetonated() => this with { HeInFlight = Math.Max(0, HeInFlight - 1) };

    public ThreatState MolotovDetonated() => this with { MolotovInFlight = Math.Max(0, MolotovInFlight - 1) };

    public ThreatState InfernoStarted(int id, Vec3 fire, Vec3? bomb, float maxDistance) =>
        bomb is { } bombPosition && (fire - bombPosition).Length <= maxDistance
            ? this with { NearInfernos = NearInfernos.Add(id) }
            : this;

    public ThreatState InfernoEnded(int id) => this with { NearInfernos = NearInfernos.Remove(id) };

    private static string Normalize(string weapon) =>
        weapon.StartsWith(WeaponPrefix, StringComparison.Ordinal) ? weapon[WeaponPrefix.Length..] : weapon;
}
```

`src/RetakeV4.Domain/InstaDefuse/InstaDefusePolicy.cs`
```csharp
namespace RetakeV4.Domain.InstaDefuse;

public sealed record InstaDefuseRules(bool RequireNoTAlive, bool BlockOnHe, bool BlockOnMolotov, bool BlockOnInferno, bool ForceExplodeIfNoTime);

public sealed record DefuseSituation(bool TerroristsAlive, float SecondsUntilExplosion, float DefuseLength, bool DefuserHasKit);

public enum ThreatKind
{
    He,
    Molotov,
    Inferno,
}

public abstract record InstaDefuseDecision
{
    public sealed record NotApplicable : InstaDefuseDecision;

    public sealed record Blocked(ThreatKind Threat) : InstaDefuseDecision;

    public sealed record NotEnoughTime(float MissingSeconds, bool ForceExplode) : InstaDefuseDecision;

    public sealed record Allowed(float SecondsLeft) : InstaDefuseDecision;
}

public static class InstaDefusePolicy
{
    private const float KitDefuseSeconds = 5f;
    private const float NoKitDefuseSeconds = 10f;

    public static InstaDefuseDecision Evaluate(InstaDefuseRules rules, ThreatState threats, DefuseSituation situation)
    {
        if (rules.RequireNoTAlive && situation.TerroristsAlive)
        {
            return new InstaDefuseDecision.NotApplicable();
        }
        if (ActiveThreat(rules, threats) is { } threat)
        {
            return new InstaDefuseDecision.Blocked(threat);
        }
        var defuseLength = situation.DefuseLength is KitDefuseSeconds or NoKitDefuseSeconds
            ? situation.DefuseLength
            : situation.DefuserHasKit ? KitDefuseSeconds : NoKitDefuseSeconds;
        var spare = situation.SecondsUntilExplosion - defuseLength;
        return spare < 0f
            ? new InstaDefuseDecision.NotEnoughTime(-spare, rules.ForceExplodeIfNoTime)
            : new InstaDefuseDecision.Allowed(situation.SecondsUntilExplosion);
    }

    private static ThreatKind? ActiveThreat(InstaDefuseRules rules, ThreatState threats)
    {
        if (rules.BlockOnHe && threats.HeInFlight > 0)
        {
            return ThreatKind.He;
        }
        if (rules.BlockOnMolotov && threats.MolotovInFlight > 0)
        {
            return ThreatKind.Molotov;
        }
        return rules.BlockOnInferno && threats.NearInfernos.Count > 0 ? ThreatKind.Inferno : null;
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/InstaDefuse tests/RetakeV4.Domain.Tests/InstaDefuse
git commit -m "feat: suivi des menaces et politique InstaDefuse (Domain)"
```

---

### Task 7: Plant (règle Domain, événement `BombPlanted` et module)

**Files:**
- Create: `src/RetakeV4.Domain/Plant/PlantRules.cs`
- Modify: `src/RetakeV4.Domain/Events/RoundEvents.cs`
- Create: `src/RetakeV4/Modules/Plant/PlantConfig.cs`, `PlantConfigValidator.cs`, `PlantModule.cs`
- Modify: `src/RetakeV4/RetakeV4Plugin.cs`, `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`
- Test: `tests/RetakeV4.Domain.Tests/Plant/PlantRulesTests.cs`, `tests/RetakeV4.Integration.Tests/Modules/Plant/PlantConfigValidatorTests.cs`

**Interfaces:**
- Consumes: `PreparationContext.Planter`/`Site`, `PreparationOrder.Plant` ; `RoundPrepared`, `RoundPhaseChanged`, `MapStarted` ; `PlayerQueries`, `GameRulesAccessor`, `ModuleHooks`.
- Produces:
  - `static class PlantRules { const int MinimumPlayers = 2; bool CanAutoPlant(bool isWarmup, int playersOnTeams, PlayerId? planter, BombSite? site); }`
  - Événement Domain `sealed record BombPlanted(BombSite? Site, PlayerId? Planter)`.
  - Config `plant.json` : `enum PlantMode { AutoPlant, FastPlant }`, `PlantConfig` (Version 1, `Mode = AutoPlant`, `PlantCheckSeconds = 5f`), `PlantConfigValidator` (`PlantCheckSeconds` ≥ 0).
  - Module `Plant` (dépend de `Core`, `Spawns`) : AutoPlant au freeze end (code V3 : `planted_c4`, gamerules, événement `bomb_planted` natif), FastPlant (C4 au planteur à l'étape Order 60, plant accéléré, vérification après `PlantCheckSeconds`, victoire CT sinon), publication de `BombPlanted`.
  - Clés lang : `plant.fast.instructions` (`{0}` secondes), `plant.failed` (`{0}` nom du planteur).

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Plant/PlantRulesTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Plant;

namespace RetakeV4.Domain.Tests.Plant;

public class PlantRulesTests
{
    [Fact]
    public void CanAutoPlant_WithPlanterSiteAndPlayers() =>
        Assert.True(PlantRules.CanAutoPlant(false, 2, new PlayerId(1), BombSite.A));

    [Fact]
    public void CanAutoPlant_NotDuringWarmup() =>
        Assert.False(PlantRules.CanAutoPlant(true, 5, new PlayerId(1), BombSite.A));

    [Fact]
    public void CanAutoPlant_NotWithFewerThanTwoPlayers() =>
        Assert.False(PlantRules.CanAutoPlant(false, 1, new PlayerId(1), BombSite.A));

    [Fact]
    public void CanAutoPlant_NotWithoutPlanter() =>
        Assert.False(PlantRules.CanAutoPlant(false, 4, null, BombSite.B));

    [Fact]
    public void CanAutoPlant_NotWithoutSite() =>
        Assert.False(PlantRules.CanAutoPlant(false, 4, new PlayerId(1), null));
}
```

`tests/RetakeV4.Integration.Tests/Modules/Plant/PlantConfigValidatorTests.cs`
```csharp
using RetakeV4.Modules.Plant;

namespace RetakeV4.Integration.Tests.Modules.Plant;

public class PlantConfigValidatorTests
{
    private static readonly PlantConfig Defaults = new();

    [Fact]
    public void Defaults_AreAutoPlantWithFiveSecondCheck()
    {
        var result = new PlantConfigValidator().Validate(Defaults, Defaults, "plant.json");
        Assert.Empty(result.Issues);
        Assert.Equal(PlantMode.AutoPlant, result.Config.Mode);
        Assert.Equal(5f, result.Config.PlantCheckSeconds);
    }

    [Fact]
    public void NegativeCheck_FallsBackToDefault()
    {
        var result = new PlantConfigValidator().Validate(Defaults with { PlantCheckSeconds = -1f }, Defaults, "plant.json");
        Assert.Equal(5f, result.Config.PlantCheckSeconds);
        Assert.Single(result.Issues);
    }
}
```

Ajouter à `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs` (nouvelle théorie) :
```csharp
    [Theory]
    [InlineData("plant.fast.instructions")]
    [InlineData("plant.failed")]
    public void Phase2bPlantKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }
```

Run: `dotnet test RetakeV4.sln --nologo`
Expected: échec de compilation (`PlantRules`, `RetakeV4.Modules.Plant` introuvables).

- [ ] **Step 2: Implémentation Domain**

`src/RetakeV4.Domain/Plant/PlantRules.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Plant;

public static class PlantRules
{
    public const int MinimumPlayers = 2;

    public static bool CanAutoPlant(bool isWarmup, int playersOnTeams, PlayerId? planter, BombSite? site) =>
        !isWarmup && playersOnTeams >= MinimumPlayers && planter is not null && site is not null;
}
```

Dans `src/RetakeV4.Domain/Events/RoundEvents.cs`, ajouter `using RetakeV4.Domain.Common;` et :
```csharp
public sealed record BombPlanted(BombSite? Site, PlayerId? Planter);
```

- [ ] **Step 3: Implémenter config, validateur, module et textes**

`src/RetakeV4/Modules/Plant/PlantConfig.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Plant;

public enum PlantMode
{
    AutoPlant,
    FastPlant,
}

public sealed record PlantConfig : ModuleConfig
{
    public PlantConfig() => Version = 1;

    public PlantMode Mode { get; init; } = PlantMode.AutoPlant;

    public float PlantCheckSeconds { get; init; } = 5f;
}
```

`src/RetakeV4/Modules/Plant/PlantConfigValidator.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Plant;

public sealed class PlantConfigValidator : IConfigValidator<PlantConfig>
{
    public ValidationResult<PlantConfig> Validate(PlantConfig config, PlantConfig defaults, string file) =>
        config.PlantCheckSeconds >= 0f
            ? new ValidationResult<PlantConfig>(config, Array.Empty<ConfigIssue>())
            : new ValidationResult<PlantConfig>(
                config with { PlantCheckSeconds = defaults.PlantCheckSeconds },
                new[] { new ConfigIssue(file, nameof(PlantConfig.PlantCheckSeconds), "must be >= 0 (0 disables the check); using default") });
}
```

`src/RetakeV4/Modules/Plant/PlantModule.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Plant;
using RetakeV4.Domain.Rounds;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace RetakeV4.Modules.Plant;

public sealed class PlantModule : IRetakeModule
{
    private PlantConfig _config = new();
    private ModuleContext? _context;
    private PreparationContext? _prepared;
    private Timer? _plantCheck;

    public string Name => "Plant";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core", "Spawns" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Plant module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("plant.json", new PlantConfig(), new PlantConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.OnBus<RoundPrepared>(e => _prepared = e.Context);
        hooks.PreparationStep(new DelegatePreparationStep("plant", PreparationOrder.Plant, GiveBombForFastPlant));
        hooks.OnEvent<EventRoundFreezeEnd>("freeze_end", _ => OnFreezeEnd());
        hooks.OnEvent<EventBombBeginplant>("bomb_beginplant", _ => SpeedUpPlant());
        hooks.OnEvent<EventBombPlanted>("bomb_planted", _ => OnBombPlanted());
        hooks.OnBus<RoundPhaseChanged>(e =>
        {
            if (e.To == RoundPhase.PostRound)
            {
                StopPlantCheck();
            }
        });
        hooks.OnBus<MapStarted>(_ =>
        {
            _plantCheck = null;
            _prepared = null;
        });
    }

    public void Unload()
    {
        StopPlantCheck();
        _context = null;
    }

    private PreparationContext GiveBombForFastPlant(PreparationContext context)
    {
        if (_config.Mode != PlantMode.FastPlant || context.Planter is not { } planter)
        {
            return context;
        }
        var player = Utilities.GetPlayerFromSlot(planter.Slot);
        if (player is not { IsValid: true })
        {
            return context;
        }
        player.GiveNamedItem("weapon_c4");
        player.ExecuteClientCommand("slot5");
        if (_config.PlantCheckSeconds > 0f)
        {
            player.PrintToCenter(Context.Text.For(player, "plant.fast.instructions", _config.PlantCheckSeconds));
        }
        return context;
    }

    private void OnFreezeEnd()
    {
        if (GameRulesAccessor.IsWarmup())
        {
            return;
        }
        if (_config.Mode == PlantMode.AutoPlant)
        {
            AutoPlant(_prepared);
            return;
        }
        StartPlantCheck(_prepared);
    }

    private void AutoPlant(PreparationContext? prepared)
    {
        var playersOnTeams = PlayerQueries.Humans().Count(p => PlayerQueries.SideOf(p) is not null);
        if (!PlantRules.CanAutoPlant(false, playersOnTeams, prepared?.Planter, prepared?.Site))
        {
            Context.Logger.LogDebug("Auto plant skipped (planter {Planter}, site {Site}, players {Players})", prepared?.Planter, prepared?.Site, playersOnTeams);
            return;
        }
        var planter = Utilities.GetPlayerFromSlot(prepared!.Planter!.Value.Slot);
        var pawn = planter?.PlayerPawn.Value;
        if (planter is not { IsValid: true } || pawn is not { IsValid: true } || pawn.AbsOrigin is null || pawn.TeamNum != (byte)CsTeam.Terrorist)
        {
            Context.Logger.LogWarning("Auto plant skipped: planter is no longer a valid living terrorist");
            return;
        }
        if (CreatePlantedBomb(pawn, prepared.Site!.Value))
        {
            FireBombPlantedEvent(planter, prepared.Site.Value);
        }
    }

    private bool CreatePlantedBomb(CCSPlayerPawn pawn, BombSite site)
    {
        var bomb = Utilities.CreateEntityByName<CPlantedC4>("planted_c4");
        if (bomb?.AbsOrigin is null)
        {
            Context.Logger.LogError("Auto plant failed: planted_c4 could not be created");
            return false;
        }
        bomb.AbsOrigin.X = pawn.AbsOrigin!.X;
        bomb.AbsOrigin.Y = pawn.AbsOrigin.Y;
        bomb.AbsOrigin.Z = pawn.AbsOrigin.Z;
        bomb.HasExploded = false;
        bomb.BombSite = (int)site;
        bomb.BombTicking = true;
        bomb.CannotBeDefused = false;
        bomb.DispatchSpawn();
        var rules = GameRulesAccessor.Get();
        if (rules is not null)
        {
            rules.BombPlanted = true;
            rules.BombDefused = false;
        }
        return true;
    }

    private static void FireBombPlantedEvent(CCSPlayerController planter, BombSite site)
    {
        var gameEvent = NativeAPI.CreateEvent("bomb_planted", true);
        NativeAPI.SetEventPlayerController(gameEvent, "userid", planter.Handle);
        NativeAPI.SetEventInt(gameEvent, "site", (int)site);
        NativeAPI.FireEvent(gameEvent, false);
    }

    private void SpeedUpPlant()
    {
        if (_config.Mode != PlantMode.FastPlant)
        {
            return;
        }
        var bomb = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").FirstOrDefault();
        if (bomb is null)
        {
            return;
        }
        bomb.BombPlacedAnimation = false;
        bomb.ArmedTime = 0f;
    }

    private void StartPlantCheck(PreparationContext? prepared)
    {
        if (_config.PlantCheckSeconds <= 0f || prepared?.Planter is null)
        {
            return;
        }
        StopPlantCheck();
        _plantCheck = Context.Plugin.AddTimer(_config.PlantCheckSeconds, () =>
        {
            _plantCheck = null;
            _context?.Guard.Run(Name, "plant_check", () => CheckPlanted(prepared.Planter.Value));
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void CheckPlanted(PlayerId planter)
    {
        if (Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").Any())
        {
            return;
        }
        var name = Utilities.GetPlayerFromSlot(planter.Slot)?.PlayerName ?? "?";
        Context.Text.ChatAll("plant.failed", name);
        GameRulesAccessor.Get()?.TerminateRound(1f, RoundEndReason.CTsWin);
    }

    private void StopPlantCheck()
    {
        _plantCheck?.Kill();
        _plantCheck = null;
    }

    private void OnBombPlanted()
    {
        StopPlantCheck();
        Context.Bus.Publish(new BombPlanted(_prepared?.Site, _prepared?.Planter));
    }
}
```

Lang : ajouter à `src/RetakeV4/lang/en.json`
```json
  "plant.fast.instructions": "You have {0} seconds to plant the bomb!",
  "plant.failed": "{0} did not plant the bomb in time: Counter-Terrorists win the round."
```
et à `src/RetakeV4/lang/fr.json`
```json
  "plant.fast.instructions": "Tu as {0} secondes pour poser la bombe !",
  "plant.failed": "{0} n'a pas posé la bombe à temps : les CT gagnent le round."
```

`src/RetakeV4/RetakeV4Plugin.cs` : `using RetakeV4.Modules.Plant;` et `new PlantModule(),` après `new AllocationModule(),`.

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Plant src/RetakeV4.Domain/Events/RoundEvents.cs src/RetakeV4/Modules/Plant src/RetakeV4/RetakeV4Plugin.cs src/RetakeV4/lang tests/RetakeV4.Domain.Tests/Plant tests/RetakeV4.Integration.Tests/Modules/Plant tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs
git commit -m "feat: module Plant (AutoPlant, FastPlant, vérification du plant, événement BombPlanted)"
```

---

### Task 8: Module `InstaDefuse`

**Files:**
- Create: `src/RetakeV4/Modules/InstaDefuse/InstaDefuseConfig.cs`, `InstaDefuseConfigValidator.cs`, `InstaDefuseModule.cs`
- Modify: `src/RetakeV4/RetakeV4Plugin.cs`, `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`
- Test: `tests/RetakeV4.Integration.Tests/Modules/InstaDefuse/InstaDefuseConfigValidatorTests.cs`

**Interfaces:**
- Consumes: `ThreatState`, `InstaDefusePolicy`, `InstaDefuseRules`, `DefuseSituation`, `InstaDefuseDecision`, `ThreatKind` (Task 6) ; `Vec3` ; `GameRulesAccessor`, `ModuleHooks`, `RoundPhaseChanged`.
- Produces:
  - Config `instadefuse.json` : `InstaDefuseConfig` (Version 1 ; `RequireNoTAlive`, `BlockOnHe`, `BlockOnMolotov`, `BlockOnInferno`, `ForceExplodeIfNoTime`, `ChatNotification` = true ; `InfernoDistance` = 250) avec `InstaDefuseRules ToRules()`, et `InstaDefuseConfigValidator` (`InfernoDistance` ≥ 0).
  - Module `InstaDefuse` (dépend de `Core`) : suivi des grenades et feux, heure de plant, décision au `bomb_begindefuse`, application au frame suivant (`DefuseCountDown = 0` ou `C4Blow = 1`), messages chat.
  - Clés lang : `instadefuse.blocked.he`, `instadefuse.blocked.molotov`, `instadefuse.blocked.inferno`, `instadefuse.not_enough_time` (`{0}` nom, `{1}` secondes), `instadefuse.success` (`{0}` nom, `{1}` secondes).

- [ ] **Step 1: Écrire le test qui échoue**

`tests/RetakeV4.Integration.Tests/Modules/InstaDefuse/InstaDefuseConfigValidatorTests.cs`
```csharp
using RetakeV4.Modules.InstaDefuse;

namespace RetakeV4.Integration.Tests.Modules.InstaDefuse;

public class InstaDefuseConfigValidatorTests
{
    private static readonly InstaDefuseConfig Defaults = new();

    [Fact]
    public void Defaults_MatchV3()
    {
        var result = new InstaDefuseConfigValidator().Validate(Defaults, Defaults, "instadefuse.json");
        Assert.Empty(result.Issues);
        Assert.Equal(250f, result.Config.InfernoDistance);
        var rules = result.Config.ToRules();
        Assert.True(rules.RequireNoTAlive && rules.BlockOnHe && rules.BlockOnMolotov && rules.BlockOnInferno && rules.ForceExplodeIfNoTime);
    }

    [Fact]
    public void NegativeInfernoDistance_FallsBackToDefault()
    {
        var result = new InstaDefuseConfigValidator().Validate(Defaults with { InfernoDistance = -5f }, Defaults, "instadefuse.json");
        Assert.Equal(250f, result.Config.InfernoDistance);
        Assert.Single(result.Issues);
    }
}
```

Ajouter à `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs` :
```csharp
    [Theory]
    [InlineData("instadefuse.blocked.he")]
    [InlineData("instadefuse.blocked.molotov")]
    [InlineData("instadefuse.blocked.inferno")]
    [InlineData("instadefuse.not_enough_time")]
    [InlineData("instadefuse.success")]
    public void Phase2bInstaDefuseKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Modules.InstaDefuse` introuvable).

- [ ] **Step 2: Implémenter config, validateur, module et textes**

`src/RetakeV4/Modules/InstaDefuse/InstaDefuseConfig.cs`
```csharp
using RetakeV4.Configuration;
using RetakeV4.Domain.InstaDefuse;

namespace RetakeV4.Modules.InstaDefuse;

public sealed record InstaDefuseConfig : ModuleConfig
{
    public InstaDefuseConfig() => Version = 1;

    public bool RequireNoTAlive { get; init; } = true;

    public bool BlockOnHe { get; init; } = true;

    public bool BlockOnMolotov { get; init; } = true;

    public bool BlockOnInferno { get; init; } = true;

    public float InfernoDistance { get; init; } = 250f;

    public bool ForceExplodeIfNoTime { get; init; } = true;

    public bool ChatNotification { get; init; } = true;

    public InstaDefuseRules ToRules() => new(RequireNoTAlive, BlockOnHe, BlockOnMolotov, BlockOnInferno, ForceExplodeIfNoTime);
}
```

`src/RetakeV4/Modules/InstaDefuse/InstaDefuseConfigValidator.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.InstaDefuse;

public sealed class InstaDefuseConfigValidator : IConfigValidator<InstaDefuseConfig>
{
    public ValidationResult<InstaDefuseConfig> Validate(InstaDefuseConfig config, InstaDefuseConfig defaults, string file) =>
        config.InfernoDistance >= 0f
            ? new ValidationResult<InstaDefuseConfig>(config, Array.Empty<ConfigIssue>())
            : new ValidationResult<InstaDefuseConfig>(
                config with { InfernoDistance = defaults.InfernoDistance },
                new[] { new ConfigIssue(file, nameof(InstaDefuseConfig.InfernoDistance), "must be >= 0; using default") });
}
```

`src/RetakeV4/Modules/InstaDefuse/InstaDefuseModule.cs`
```csharp
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.InstaDefuse;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Modules.InstaDefuse;

public sealed class InstaDefuseModule : IRetakeModule
{
    private InstaDefuseConfig _config = new();
    private ModuleContext? _context;
    private ThreatState _threats = ThreatState.Empty;
    private float _bombPlantedAt = float.NaN;
    private bool _bombTicking;

    public string Name => "InstaDefuse";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("InstaDefuse module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("instadefuse.json", new InstaDefuseConfig(), new InstaDefuseConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.OnEvent<EventGrenadeThrown>("grenade_thrown", e => _threats = _threats.GrenadeThrown(e.Weapon));
        hooks.OnEvent<EventHegrenadeDetonate>("hegrenade_detonate", _ => _threats = _threats.HeDetonated());
        hooks.OnEvent<EventMolotovDetonate>("molotov_detonate", _ => _threats = _threats.MolotovDetonated());
        hooks.OnEvent<EventInfernoStartburn>("inferno_startburn", e =>
            _threats = _threats.InfernoStarted(e.Entityid, new Vec3(e.X, e.Y, e.Z), BombPosition(), _config.InfernoDistance));
        hooks.OnEvent<EventInfernoExtinguish>("inferno_extinguish", e => _threats = _threats.InfernoEnded(e.Entityid));
        hooks.OnEvent<EventInfernoExpire>("inferno_expire", e => _threats = _threats.InfernoEnded(e.Entityid));
        hooks.OnEvent<EventBombPlanted>("bomb_planted", _ =>
        {
            _bombPlantedAt = Server.CurrentTime;
            _bombTicking = true;
        });
        hooks.OnEvent<EventBombDefused>("bomb_defused", _ => _bombTicking = false);
        hooks.OnEvent<EventBombExploded>("bomb_exploded", _ => _bombTicking = false);
        hooks.OnEvent<EventBombBegindefuse>("bomb_begindefuse", OnBeginDefuse);
        hooks.OnBus<RoundPhaseChanged>(e =>
        {
            if (e.To == RoundPhase.Preparing)
            {
                Reset();
            }
        });
    }

    public void Unload() => _context = null;

    private void Reset()
    {
        _threats = ThreatState.Empty;
        _bombPlantedAt = float.NaN;
        _bombTicking = false;
    }

    private void OnBeginDefuse(EventBombBegindefuse e)
    {
        if (GameRulesAccessor.IsWarmup() || !_bombTicking || e.Userid is not { IsValid: true, PawnIsAlive: true } defuser)
        {
            return;
        }
        var bomb = FindPlantedBomb();
        if (bomb is null || bomb.CannotBeDefused)
        {
            return;
        }
        var situation = new DefuseSituation(
            TerroristsAlive(),
            bomb.TimerLength - (Server.CurrentTime - _bombPlantedAt),
            bomb.DefuseLength,
            defuser.PawnHasDefuser);
        Apply(InstaDefusePolicy.Evaluate(_config.ToRules(), _threats, situation), defuser.PlayerName);
    }

    private void Apply(InstaDefuseDecision decision, string defuserName)
    {
        switch (decision)
        {
            case InstaDefuseDecision.Blocked blocked:
                Notify($"instadefuse.blocked.{blocked.Threat.ToString().ToLowerInvariant()}");
                break;
            case InstaDefuseDecision.NotEnoughTime notEnough:
                Notify("instadefuse.not_enough_time", defuserName, Seconds(notEnough.MissingSeconds));
                if (notEnough.ForceExplode)
                {
                    OnNextFrame("force_explode", bomb => bomb.C4Blow = 1f);
                }
                break;
            case InstaDefuseDecision.Allowed allowed:
                OnNextFrame("instant_defuse", bomb =>
                {
                    bomb.DefuseCountDown = 0f;
                    Notify("instadefuse.success", defuserName, Seconds(allowed.SecondsLeft));
                });
                break;
        }
    }

    private void OnNextFrame(string stage, Action<CPlantedC4> action) =>
        Server.NextFrame(() => _context?.Guard.Run(Name, stage, () =>
        {
            if (FindPlantedBomb() is { } bomb)
            {
                action(bomb);
            }
        }));

    private void Notify(string key, params object[] args)
    {
        if (_config.ChatNotification)
        {
            Context.Text.ChatAll(key, args);
        }
    }

    private static string Seconds(float seconds) => seconds.ToString("0.000", CultureInfo.InvariantCulture);

    private static CPlantedC4? FindPlantedBomb() =>
        Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault(b => b.IsValid);

    private static Vec3? BombPosition() =>
        FindPlantedBomb()?.AbsOrigin is { } origin ? new Vec3(origin.X, origin.Y, origin.Z) : null;

    private static bool TerroristsAlive() =>
        Utilities.GetPlayers().Any(p => p is { IsValid: true, PawnIsAlive: true } && (CsTeam)p.TeamNum == CsTeam.Terrorist);
}
```

Lang : ajouter à `src/RetakeV4/lang/en.json`
```json
  "instadefuse.blocked.he": "Instant defuse impossible: an HE grenade is in the air.",
  "instadefuse.blocked.molotov": "Instant defuse impossible: a molotov is in the air.",
  "instadefuse.blocked.inferno": "Instant defuse impossible: fire is burning near the bomb.",
  "instadefuse.not_enough_time": "{0} cannot instadefuse: {1}s missing.",
  "instadefuse.success": "{0} instadefused with {1}s left."
```
et à `src/RetakeV4/lang/fr.json`
```json
  "instadefuse.blocked.he": "Instadefuse impossible : une HE est en l'air.",
  "instadefuse.blocked.molotov": "Instadefuse impossible : une molotov est en l'air.",
  "instadefuse.blocked.inferno": "Instadefuse impossible : du feu brûle près de la bombe.",
  "instadefuse.not_enough_time": "{0} ne peut pas instadefuse : il manque {1}s.",
  "instadefuse.success": "{0} a instadefuse avec {1}s restantes."
```

`src/RetakeV4/RetakeV4Plugin.cs` : `using RetakeV4.Modules.InstaDefuse;` et `new InstaDefuseModule(),` après `new PlantModule(),`.

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0` partout (y compris les 5 `Phase2bInstaDefuseKeys_ArePresent`).

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Modules/InstaDefuse src/RetakeV4/RetakeV4Plugin.cs src/RetakeV4/lang tests/RetakeV4.Integration.Tests/Modules/InstaDefuse tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs
git commit -m "feat: module InstaDefuse (menaces, temps restant, défuse instantané ou explosion forcée)"
```

---

### Task 9: Checklist en jeu phase 2b, documentation et vérification finale

**Files:**
- Modify: `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`

- [ ] **Step 1: Checklist**

Ajouter à `docs/CHECKLIST-INGAME.md` :
```markdown

## Phase 2b — Armes, plant, InstaDefuse
- [ ] Log `… loaded with modules: Core, RoundTypes, Teams, Spawns, Allocation, Plant, InstaDefuse` ; `grenades.json`, `allocation.json`, `plant.json`, `instadefuse.json` créés ; `roundtypes.json` (version 2) contient les pools d'armes.
- [ ] Round Pistol : T glock, CT usp-s, kevlar sans casque, couteau conservé ; environ 1 CT sur 3 a un kit et **au moins un** CT en a un.
- [ ] Round Mid : T mac-10 + deagle, CT mp9 + deagle, kevlar + casque ; tous les CT ont un kit.
- [ ] Round FullBuy : T ak-47 + deagle, CT m4a4 + deagle, kevlar + casque ; aucune AWP (pas encore de préférences, phase 3).
- [ ] Chaque joueur a un kit de grenades aléatoire cohérent avec son camp (molotov côté T, incendiaire côté CT) ; certains n'en ont aucune.
- [ ] Un couteau personnalisé / une baïonnette est conservé ; aucune C4 dans les inventaires (AutoPlant).
- [ ] AutoPlant : au freeze end, la bombe est posée sous les pieds du planteur (T en zone de plant), le compte à rebours de 40 s démarre, l'annonce « bombe posée » apparaît.
- [ ] `plant.json` en `FastPlant` : seul le planteur reçoit la C4, message central, le plant est instantané ; s'il ne pose pas en 5 s : message et victoire CT.
- [ ] InstaDefuse : tous les T morts, un CT commence à défuser → défuse immédiat et message « … a instadefuse avec Xs restantes ».
- [ ] Un T encore en vie → défuse normal, aucun message.
- [ ] HE ou molotov lancée juste avant le défuse → message de blocage correspondant, défuse normal.
- [ ] Molotov qui brûle près de la bombe → message « du feu brûle près de la bombe ».
- [ ] Défuse commencé avec moins de 10 s (sans kit) → message « il manque Xs » et la bombe explose immédiatement.
```

- [ ] **Step 2: CLAUDE.md**

Dans la section Règles de `CLAUDE.md`, ajouter :
```markdown
- Tout callback `Server.NextFrame` ou timer d'un module passe par `context.Guard.Run` (via `_context?.Guard`, le module a pu être déchargé entre-temps).
- Les définitions de types de round (armes, armure, AWP, kits, Zeus, pool de grenades) vivent dans `roundtypes.json` et arrivent aux modules via `PreparationContext.RoundTypeDefinition`.
```

- [ ] **Step 3: Vérification finale**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo && dotnet test tests/RetakeV4.Domain.Tests --nologo -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`
puis (outil PowerShell) `pwsh -NoProfile -File scripts/package-dev.ps1`
Expected: 0 warning, `Failed: 0`, couverture Domain ≥ 80 %, `Package ready`.

- [ ] **Step 4: Commit**

```bash
git add docs/CHECKLIST-INGAME.md CLAUDE.md
git commit -m "docs: checklist en jeu phase 2b et guide du dépôt"
```
