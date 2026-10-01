# RetakeV4 phase 5a — Choix des armes par le menu d'achat natif — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter les modes d'allocation `NativeBuy` et `Both` : le menu d'achat CS2 sert à choisir ses armes (la préférence est mise à jour, rien n'est jamais acheté pour de vrai), avec un message de rappel périodique.

**Architecture:** La résolution d'un achat (jetons de la commande `buy`, defindex de `item_pickup`, objets gérés automatiquement), la décision (arme autorisée pour le round, volontariat AWP) et les cvars par mode sont dans le Domain et testées. Un composant isolé `NativeBuySelector` (module Allocation) intercepte `buy` en Pre : un achat résolu est bloqué et appliqué comme un choix du menu ; un achat ambigu (identifiant numérique) passe, l'arme est capturée à `item_pickup`, retirée, l'argent remis et les armes du round restaurées (méthode d'agora).

**Tech Stack:** C# / .NET 10, CounterStrikeSharp.API 1.0.370, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-30-retake-v4-design.md` (section 5.4)

**Découpage de la phase 5 :** 5a (ce plan) = NativeBuy/Both + message de rappel. 5b = API publique `RetakeV4.Contracts`, CI/release, `ConfigExporter`, documentation et `MIGRATION-V3.md`.

## Global Constraints

- CounterStrikeSharp.API 1.0.370, .NET 10, `dotnet build RetakeV4.sln -c Release` : 0 warning.
- `RetakeV4.Domain` sans CounterStrikeSharp, couverture de lignes ≥ 80 %.
- Modules sans référence croisée ; handlers et timers via `context.Hooks` (gardés, libérés au déchargement).
- Textes joueurs via `lang/*.json` (en + fr synchronisés).
- Spec 5.4 : « Un achat ne donne jamais d'arme directement. Les cvars d'achat (`mp_buytime`, `mp_buy_anywhere`) sont réglées selon le mode. » ; message de rappel `HowToMessage` à intervalle en minutes, timer tué avant toute recréation.

## Décisions de conception

1. **Mode** : `allocation.json` → `Mode` = `Menu` (défaut), `NativeBuy` ou `Both`. `Menu` garde le comportement actuel. `NativeBuy` : pas de menu d'armes (`!guns` répond par le message de rappel, pas d'ouverture automatique). `Both` : les deux chemins.
2. **Cvars** : `Menu` → `mp_buy_anywhere 0`, `mp_buytime 0`, `mp_maxmoney 0` (comme `retake.cfg`). `NativeBuy`/`Both` → `mp_buy_anywhere 1`, `mp_buytime 9999`, `mp_maxmoney 16000`, et chaque joueur a 16000 $ à chaque round et après chaque capture. Appliquées au chargement et à chaque round préparé (le `retake.cfg` exécuté au changement de map les remettrait à 0).
3. **Cible du choix** : un achat met à jour la préférence de l'équipe du joueur pour le type du round en cours ; il s'applique tout de suite pendant le freeze time (vivant), sinon au round suivant — exactement comme un choix du menu (`ApplySelection`).
4. **AWP** : acheter l'AWP rend le joueur volontaire (sans le désinscrire s'il l'est déjà).
5. **Objets gérés** (armure, kit, grenades, Zeus) : achat bloqué avec un message. S'ils passent par la capture (identifiant numérique), l'objet capturé est retiré.
6. **Capture** : fenêtre de 2 s après un `buy` non résolu ; après la capture, toutes les armes principales et secondaires du joueur sont retirées puis celles de son loadout du round sont redonnées (le joueur ne perd jamais son arme s'il « achète » celle qu'il tient). Pas de commande `slotN` envoyée.
7. **Rappel** : `HowToIntervalMinutes` (défaut 5, 0 = désactivé, max 120), message propre au mode.

## Review Focus

1. Achat avec un identifiant numérique (menu d'achat CS2) → passe, capturé à `item_pickup`, préférence mise à jour, arme achetée retirée, loadout restauré (Task 1 : `NumericPayload_IsCaptured` ; checklist).
2. Achat de grenade, armure, kit ou Zeus → bloqué avec un message (Task 1 : `AutoManagedItems_AreBlocked`).
3. Achat d'une arme absente du pool du round → refusé, préférence inchangée (Task 1 : `WeaponOutsideThePool_IsNotAvailable`).
4. « Achat » de l'arme déjà tenue → le joueur la garde (checklist).
5. Mode `Menu` → menu d'achat fermé par les cvars, comportement inchangé (Task 1 : `MenuMode_ClosesTheBuyMenu`).

---

## File Structure

```
src/RetakeV4.Domain/Loadouts/NativeBuy.cs, WeaponCatalog.cs                 (Task 1)
src/RetakeV4/Modules/Allocation/AllocationConfig.cs, AllocationConfigValidator.cs,
    Modules/ModuleHooks.cs, lang/*.json                                     (Task 2)
src/RetakeV4/Modules/Allocation/NativeBuySelector.cs, LoadoutApplier.cs,
    AllocationModule.cs                                                     (Task 3)
docs/CHECKLIST-INGAME.md, CLAUDE.md                                         (Task 4)
```

---

### Task 1: Résolution et décision d'un achat natif (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Loadouts/NativeBuy.cs`
- Modify: `src/RetakeV4.Domain/Loadouts/WeaponCatalog.cs`
- Test: `tests/RetakeV4.Domain.Tests/Loadouts/NativeBuyTests.cs`

**Interfaces:**
- Consumes: `WeaponCatalog`, `WeaponMenu.IsAllowed`, `WeaponMenuSelection`, `WeaponSlot`, `RoundTypeDefinition`, `TeamSide`.
- Produces:
  - `WeaponCatalog.Guns` (`IReadOnlyCollection<string>` : principales + secondaires).
  - `enum BuyRequestKind { Weapon, AutoManaged, Capture }`, `sealed record BuyRequest(BuyRequestKind Kind, string? Weapon = null)`.
  - `static class NativeBuyResolver` : `Normalize(string)`, `Resolve(IEnumerable<string> arguments)` → `BuyRequest`, `FromPickup(long defindex, string? item)` → `string?` (id `weapon_*`), `IsAutoManaged(string?)`.
  - `enum BuyOutcome { SetWeapon, AwpVolunteer, NotAvailable }`, `sealed record BuyDecision(BuyOutcome Outcome, WeaponMenuSelection? Selection = null)`, `static NativeBuy.Decide(string weapon, TeamSide team, RoundTypeDefinition? current)`, `NativeBuy.Cash = 16000`.
  - `enum AllocationMode { Menu, NativeBuy, Both }`, `static class AllocationModes` : `UsesMenu`, `UsesNativeBuy`, `Cvars(AllocationMode)` → `IReadOnlyList<(string Name, string Value)>`, `HowToKey(AllocationMode)`.

- [ ] **Step 1: Écrire le test qui échoue**

`tests/RetakeV4.Domain.Tests/Loadouts/NativeBuyTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Tests.Loadouts;

public class NativeBuyTests
{
    private static readonly RoundTypeDefinition FullBuy = new(
        "FullBuy",
        ArmorKind.KevlarHelmet,
        new TeamWeapons(new[] { "weapon_ak47", "weapon_galilar" }, new[] { "weapon_m4a1", "weapon_m4a1_silencer" }, Array.Empty<string>()),
        new TeamWeapons(new[] { "weapon_glock", "weapon_tec9" }, new[] { "weapon_usp_silencer" }, new[] { "weapon_deagle" }),
        new TeamDefault("weapon_ak47", "weapon_glock"),
        new TeamDefault("weapon_m4a1", "weapon_usp_silencer"),
        new AwpSettings(true, 1, 0, 1.0),
        new DefuseKitSettings(DefuseKitMode.All, 0, 1.0, false),
        new ZeusSettings(false, 0),
        "default");

    [Theory]
    [InlineData("weapon_M4A1_Silencer", "m4a1silencer")]
    [InlineData(" AK-47 ", "ak47")]
    [InlineData("item_kevlar", "itemkevlar")]
    public void Normalize_KeepsLettersAndDigits(string raw, string expected) => Assert.Equal(expected, NativeBuyResolver.Normalize(raw));

    [Theory]
    [InlineData("ak47", "weapon_ak47")]
    [InlineData("weapon_m4a1_silencer", "weapon_m4a1_silencer")]
    [InlineData("m4a1s", "weapon_m4a1_silencer")]
    [InlineData("usp", "weapon_usp_silencer")]
    [InlineData("p2000", "weapon_hkp2000")]
    [InlineData("galil", "weapon_galilar")]
    [InlineData("deagle", "weapon_deagle")]
    [InlineData("awp", "weapon_awp")]
    public void NamedWeapons_AreResolved(string token, string expected) =>
        Assert.Equal(new BuyRequest(BuyRequestKind.Weapon, expected), NativeBuyResolver.Resolve(new[] { token }));

    [Theory]
    [InlineData("hegrenade")]
    [InlineData("item_assaultsuit")]
    [InlineData("vesthelm")]
    [InlineData("defuser")]
    [InlineData("weapon_taser")]
    public void AutoManagedItems_AreBlocked(string token) =>
        Assert.Equal(BuyRequestKind.AutoManaged, NativeBuyResolver.Resolve(new[] { token }).Kind);

    [Fact]
    public void NumericPayload_IsCaptured() =>
        Assert.Equal(BuyRequestKind.Capture, NativeBuyResolver.Resolve(new[] { "unused", "12" }).Kind);

    [Fact]
    public void UnknownOrEmptyPayload_IsCaptured()
    {
        Assert.Equal(BuyRequestKind.Capture, NativeBuyResolver.Resolve(new[] { "something_new" }).Kind);
        Assert.Equal(BuyRequestKind.Capture, NativeBuyResolver.Resolve(new[] { "buy", " " }).Kind);
    }

    [Theory]
    [InlineData(60, null, "weapon_m4a1_silencer")]
    [InlineData(7, "weapon_glock", "weapon_ak47")]
    [InlineData(0, "weapon_usp_silencer", "weapon_usp_silencer")]
    [InlineData(999, "weapon_mp9", "weapon_mp9")]
    [InlineData(44, "weapon_hegrenade", null)]
    public void Pickups_PreferTheDefindex(long defindex, string? item, string? expected) =>
        Assert.Equal(expected, NativeBuyResolver.FromPickup(defindex, item));

    [Fact]
    public void AutoManagedPickups_AreRecognised()
    {
        Assert.True(NativeBuyResolver.IsAutoManaged("weapon_flashbang"));
        Assert.False(NativeBuyResolver.IsAutoManaged("weapon_ak47"));
        Assert.False(NativeBuyResolver.IsAutoManaged(null));
    }

    [Fact]
    public void WeaponInThePool_UpdatesTheCurrentRoundPreference()
    {
        var decision = NativeBuy.Decide("weapon_galilar", TeamSide.T, FullBuy);
        Assert.Equal(new BuyDecision(BuyOutcome.SetWeapon, new WeaponMenuSelection(TeamSide.T, "FullBuy", WeaponSlot.Primary, "weapon_galilar")), decision);
        Assert.Equal(WeaponSlot.Secondary, NativeBuy.Decide("weapon_deagle", TeamSide.CT, FullBuy).Selection?.Slot);
    }

    [Fact]
    public void WeaponOutsideThePool_IsNotAvailable()
    {
        Assert.Equal(BuyOutcome.NotAvailable, NativeBuy.Decide("weapon_m4a1", TeamSide.T, FullBuy).Outcome);
        Assert.Equal(BuyOutcome.NotAvailable, NativeBuy.Decide("weapon_nova", TeamSide.CT, FullBuy).Outcome);
        Assert.Equal(BuyOutcome.NotAvailable, NativeBuy.Decide("weapon_ak47", TeamSide.T, null).Outcome);
    }

    [Fact]
    public void Awp_MakesAVolunteer() =>
        Assert.Equal(BuyOutcome.AwpVolunteer, NativeBuy.Decide(WeaponCatalog.Awp, TeamSide.T, null).Outcome);

    [Fact]
    public void MenuMode_ClosesTheBuyMenu()
    {
        Assert.Contains(("mp_buytime", "0"), AllocationModes.Cvars(AllocationMode.Menu));
        Assert.Contains(("mp_buy_anywhere", "1"), AllocationModes.Cvars(AllocationMode.NativeBuy));
        Assert.Contains(("mp_maxmoney", "16000"), AllocationModes.Cvars(AllocationMode.Both));
        Assert.True(AllocationModes.UsesMenu(AllocationMode.Both));
        Assert.False(AllocationModes.UsesMenu(AllocationMode.NativeBuy));
        Assert.False(AllocationModes.UsesNativeBuy(AllocationMode.Menu));
        Assert.Equal("allocation.howto.native", AllocationModes.HowToKey(AllocationMode.NativeBuy));
    }
}
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`NativeBuyResolver`, `NativeBuy`, `AllocationModes` introuvables).

- [ ] **Step 2: Implémentation**

Dans `src/RetakeV4.Domain/Loadouts/WeaponCatalog.cs`, ajouter après les ensembles :
```csharp
    public static IReadOnlyCollection<string> Guns { get; } = Primaries.Concat(Secondaries).ToList();
```

`src/RetakeV4.Domain/Loadouts/NativeBuy.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Loadouts;

public enum BuyRequestKind
{
    Weapon,
    AutoManaged,
    Capture,
}

public sealed record BuyRequest(BuyRequestKind Kind, string? Weapon = null);

// Turns the CS2 "buy" command and "item_pickup" event into a weapon id (method and tables from agora's CommandAllocator).
public static class NativeBuyResolver
{
    private static readonly HashSet<string> AutoManaged = new(StringComparer.Ordinal)
    {
        "vest", "vesthelm", "assaultsuit", "kevlar", "itemkevlar", "itemassaultsuit", "helmet", "defuser", "itemdefuser",
        "hegrenade", "incgrenade", "molotov", "flashbang", "smokegrenade", "decoy", "taser",
    };

    private static readonly IReadOnlyDictionary<string, string> ExtraAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["m4a1s"] = "weapon_m4a1_silencer",
        ["m4a4"] = "weapon_m4a1",
        ["usp"] = "weapon_usp_silencer",
        ["usps"] = "weapon_usp_silencer",
        ["p2000"] = "weapon_hkp2000",
        ["galil"] = "weapon_galilar",
        ["sg553"] = "weapon_sg556",
        ["cz75"] = "weapon_cz75a",
        ["r8"] = "weapon_revolver",
        ["dualberettas"] = "weapon_elite",
        ["mac"] = "weapon_mac10",
        ["ump"] = "weapon_ump45",
        ["mp5"] = "weapon_mp5sd",
        ["scout"] = "weapon_ssg08",
    };

    private static readonly IReadOnlyDictionary<string, string> Aliases = WeaponCatalog.Guns
        .ToDictionary(Normalize, id => id, StringComparer.Ordinal)
        .Concat(ExtraAliases)
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<long, string> ByDefindex = new Dictionary<long, string>
    {
        [1] = "weapon_deagle", [2] = "weapon_elite", [3] = "weapon_fiveseven", [4] = "weapon_glock", [7] = "weapon_ak47",
        [8] = "weapon_aug", [9] = "weapon_awp", [10] = "weapon_famas", [11] = "weapon_g3sg1", [13] = "weapon_galilar",
        [14] = "weapon_m249", [16] = "weapon_m4a1", [17] = "weapon_mac10", [19] = "weapon_p90", [23] = "weapon_mp5sd",
        [24] = "weapon_ump45", [25] = "weapon_xm1014", [26] = "weapon_bizon", [27] = "weapon_mag7", [28] = "weapon_negev",
        [29] = "weapon_sawedoff", [30] = "weapon_tec9", [32] = "weapon_hkp2000", [33] = "weapon_mp7", [34] = "weapon_mp9",
        [35] = "weapon_nova", [36] = "weapon_p250", [38] = "weapon_scar20", [39] = "weapon_sg556", [40] = "weapon_ssg08",
        [60] = "weapon_m4a1_silencer", [61] = "weapon_usp_silencer", [63] = "weapon_cz75a", [64] = "weapon_revolver",
    };

    public static string Normalize(string raw)
    {
        var token = raw.Trim().ToLowerInvariant();
        if (token.StartsWith("weapon_", StringComparison.Ordinal))
        {
            token = token["weapon_".Length..];
        }
        return new string(token.Where(char.IsLetterOrDigit).ToArray());
    }

    // Order matters: anything auto-managed is refused, a numeric (buy-menu) or unknown payload is captured at pickup.
    public static BuyRequest Resolve(IEnumerable<string> arguments)
    {
        var tokens = arguments
            .SelectMany(a => a.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(Normalize)
            .Where(t => t.Length > 0 && t != "buy")
            .ToList();
        if (tokens.Any(AutoManaged.Contains))
        {
            return new BuyRequest(BuyRequestKind.AutoManaged);
        }
        if (tokens.Count == 0 || tokens.Any(t => t.All(char.IsDigit)))
        {
            return new BuyRequest(BuyRequestKind.Capture);
        }
        var weapon = tokens.Select(t => Aliases.GetValueOrDefault(t)).FirstOrDefault(w => w is not null);
        return weapon is null ? new BuyRequest(BuyRequestKind.Capture) : new BuyRequest(BuyRequestKind.Weapon, weapon);
    }

    public static string? FromPickup(long defindex, string? item)
    {
        if (ByDefindex.TryGetValue(defindex, out var weapon))
        {
            return weapon;
        }
        return item is null ? null : Aliases.GetValueOrDefault(Normalize(item));
    }

    public static bool IsAutoManaged(string? item) => item is not null && AutoManaged.Contains(Normalize(item));
}

public enum BuyOutcome
{
    SetWeapon,
    AwpVolunteer,
    NotAvailable,
}

public sealed record BuyDecision(BuyOutcome Outcome, WeaponMenuSelection? Selection = null);

public static class NativeBuy
{
    public const int Cash = 16000;

    private static readonly BuyDecision NotAvailable = new(BuyOutcome.NotAvailable);

    public static BuyDecision Decide(string weapon, TeamSide team, RoundTypeDefinition? current)
    {
        if (weapon == WeaponCatalog.Awp)
        {
            return new BuyDecision(BuyOutcome.AwpVolunteer);
        }
        WeaponSlot? slot = WeaponCatalog.IsPrimary(weapon) ? WeaponSlot.Primary : WeaponCatalog.IsSecondary(weapon) ? WeaponSlot.Secondary : null;
        if (current is null || slot is null)
        {
            return NotAvailable;
        }
        var selection = new WeaponMenuSelection(team, current.Name, slot.Value, weapon);
        return WeaponMenu.IsAllowed(selection, new[] { current }) ? new BuyDecision(BuyOutcome.SetWeapon, selection) : NotAvailable;
    }
}

public enum AllocationMode
{
    Menu,
    NativeBuy,
    Both,
}

public static class AllocationModes
{
    public static bool UsesMenu(AllocationMode mode) => mode != AllocationMode.NativeBuy;

    public static bool UsesNativeBuy(AllocationMode mode) => mode != AllocationMode.Menu;

    // The buy menu is only open when it is used to choose weapons; money is fixed because nothing is really bought.
    public static IReadOnlyList<(string Name, string Value)> Cvars(AllocationMode mode) => UsesNativeBuy(mode)
        ? new[] { ("mp_buy_anywhere", "1"), ("mp_buytime", "9999"), ("mp_maxmoney", NativeBuy.Cash.ToString(System.Globalization.CultureInfo.InvariantCulture)) }
        : new[] { ("mp_buy_anywhere", "0"), ("mp_buytime", "0"), ("mp_maxmoney", "0") };

    public static string HowToKey(AllocationMode mode) => mode switch
    {
        AllocationMode.Menu => "allocation.howto.menu",
        AllocationMode.NativeBuy => "allocation.howto.native",
        _ => "allocation.howto.both",
    };
}
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4.Domain/Loadouts tests/RetakeV4.Domain.Tests/Loadouts/NativeBuyTests.cs
git commit -m "feat: résolution et décision d'un achat natif, modes d'allocation (Domain)"
```

---

### Task 2: Configuration du mode, timer de module et textes

**Files:**
- Modify: `src/RetakeV4/Modules/Allocation/AllocationConfig.cs`, `src/RetakeV4/Modules/Allocation/AllocationConfigValidator.cs`, `src/RetakeV4/Modules/ModuleHooks.cs`, `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`
- Test: `tests/RetakeV4.Integration.Tests/Modules/Allocation/AllocationConfigValidatorTests.cs`, `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs`

**Interfaces:**
- Consumes: `AllocationMode` (Task 1).
- Produces:
  - `AllocationConfig` version 4 : `AllocationMode Mode = Menu`, `int HowToIntervalMinutes = 5`.
  - `AllocationConfigValidator` : `Mode` inconnu → `Menu` ; `HowToIntervalMinutes` hors 0..120 → 5.
  - `ModuleHooks.RepeatTimer(string stage, float intervalSeconds, Action handler)` (gardé, tué au déchargement).
  - Clés lang `allocation.buy.auto_managed`, `allocation.buy.not_available`, `allocation.buy.awp_volunteer`, `allocation.howto.menu`, `allocation.howto.native`, `allocation.howto.both`.

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `tests/RetakeV4.Integration.Tests/Modules/Allocation/AllocationConfigValidatorTests.cs` (avec `using RetakeV4.Domain.Loadouts;`) :
```csharp
    [Fact]
    public void Defaults_UseTheMenuMode_AndAFiveMinuteReminder()
    {
        Assert.Equal(AllocationMode.Menu, Defaults.Mode);
        Assert.Equal(5, Defaults.HowToIntervalMinutes);
    }

    [Fact]
    public void UnknownMode_FallsBackToMenu()
    {
        var result = _validator.Validate(Defaults with { Mode = (AllocationMode)9 }, Defaults, "allocation.json");
        Assert.Equal(AllocationMode.Menu, result.Config.Mode);
        Assert.Single(result.Issues);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public void OutOfRangeReminder_FallsBack(int minutes)
    {
        var result = _validator.Validate(Defaults with { HowToIntervalMinutes = minutes }, Defaults, "allocation.json");
        Assert.Equal(5, result.Config.HowToIntervalMinutes);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void DisabledReminder_IsValid() =>
        Assert.Empty(_validator.Validate(Defaults with { HowToIntervalMinutes = 0, Mode = AllocationMode.Both }, Defaults, "allocation.json").Issues);
```

Ajouter à `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs` (dans la classe) :
```csharp
    [Theory]
    [InlineData("allocation.buy.auto_managed")]
    [InlineData("allocation.buy.not_available")]
    [InlineData("allocation.buy.awp_volunteer")]
    [InlineData("allocation.howto.menu")]
    [InlineData("allocation.howto.native")]
    [InlineData("allocation.howto.both")]
    public void Phase5aKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`AllocationConfig.Mode` introuvable).

- [ ] **Step 2: Implémentation**

Dans `src/RetakeV4/Modules/Allocation/AllocationConfig.cs`, ajouter `using RetakeV4.Domain.Loadouts;` et remplacer le record `AllocationConfig` par :
```csharp
public sealed record AllocationConfig : ModuleConfig
{
    public AllocationConfig() => Version = 4;

    public DatabaseConfig Database { get; init; } = new();

    public bool AutoOpenMenu { get; init; } = true;

    public AllocationMode Mode { get; init; } = AllocationMode.Menu;

    public int HowToIntervalMinutes { get; init; } = 5;
}
```

Dans `src/RetakeV4/Modules/Allocation/AllocationConfigValidator.cs`, ajouter `using RetakeV4.Domain.Loadouts;` et remplacer `return new ValidationResult<AllocationConfig>(config with { Database = database }, issues);` par :
```csharp
        var mode = config.Mode;
        if (!Enum.IsDefined(mode))
        {
            issues.Add(new ConfigIssue(file, nameof(AllocationConfig.Mode), "must be Menu, NativeBuy or Both; using Menu"));
            mode = AllocationMode.Menu;
        }
        var reminder = config.HowToIntervalMinutes;
        if (reminder is < 0 or > MaxReminderMinutes)
        {
            issues.Add(new ConfigIssue(file, nameof(AllocationConfig.HowToIntervalMinutes), $"must be between 0 and {MaxReminderMinutes}; using default"));
            reminder = defaults.HowToIntervalMinutes;
        }
        return new ValidationResult<AllocationConfig>(config with { Database = database, Mode = mode, HowToIntervalMinutes = reminder }, issues);
```
et la constante `private const int MaxReminderMinutes = 120;` en tête de classe.

Dans `src/RetakeV4/Modules/ModuleHooks.cs`, ajouter `using CounterStrikeSharp.API.Modules.Timers;` et, après `OnPlayerButtons` :
```csharp
    public void RepeatTimer(string stage, float intervalSeconds, Action handler)
    {
        var timer = _plugin.AddTimer(intervalSeconds, () => _guard.Run(_module, stage, handler), TimerFlags.REPEAT);
        _registrations.Track(timer.Kill);
    }
```

Lang : ajouter à `src/RetakeV4/lang/en.json`
```json
  "allocation.buy.auto_managed": "Armor, kit, grenades and Zeus are handed out automatically.",
  "allocation.buy.not_available": "This weapon is not available for this round.",
  "allocation.buy.awp_volunteer": "You now volunteer for the AWP.",
  "allocation.howto.menu": "Type !guns to choose your weapons.",
  "allocation.howto.native": "Use the buy menu (B) to choose your weapons: nothing is bought, your choice is saved.",
  "allocation.howto.both": "Type !guns or use the buy menu (B) to choose your weapons."
```
et à `src/RetakeV4/lang/fr.json`
```json
  "allocation.buy.auto_managed": "Armure, kit, grenades et Zeus sont donnés automatiquement.",
  "allocation.buy.not_available": "Cette arme n'est pas disponible pour ce round.",
  "allocation.buy.awp_volunteer": "Tu es maintenant volontaire pour l'AWP.",
  "allocation.howto.menu": "Tape !guns pour choisir tes armes.",
  "allocation.howto.native": "Utilise le menu d'achat (B) pour choisir tes armes : rien n'est acheté, ton choix est enregistré.",
  "allocation.howto.both": "Tape !guns ou utilise le menu d'achat (B) pour choisir tes armes."
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Modules/Allocation/AllocationConfig.cs src/RetakeV4/Modules/Allocation/AllocationConfigValidator.cs src/RetakeV4/Modules/ModuleHooks.cs src/RetakeV4/lang tests/RetakeV4.Integration.Tests
git commit -m "feat: mode d'allocation et rappel configurables, timer répétitif de module, textes de l'achat natif"
```

---

### Task 3: Sélecteur d'achat natif et branchement

**Files:**
- Create: `src/RetakeV4/Modules/Allocation/NativeBuySelector.cs`
- Modify: `src/RetakeV4/Modules/Allocation/LoadoutApplier.cs`, `src/RetakeV4/Modules/Allocation/AllocationModule.cs`

**Interfaces:**
- Consumes: `NativeBuyResolver`, `NativeBuy`, `AllocationModes`, `BuyDecision` (Task 1) ; `AllocationConfig.Mode/HowToIntervalMinutes`, `ModuleHooks.RepeatTimer` (Task 2) ; `ApplySelection`, `_lastPlan`, `PreferenceService` (phase 3b).
- Produces:
  - `LoadoutApplier.ReplaceGuns(CCSPlayerController, Loadout)` : retire toutes les armes principales et secondaires, redonne celles du loadout, sans commande `slotN`.
  - `LoadoutApplier.ResetCash(CCSPlayerController)`.
  - `internal sealed class NativeBuySelector(ModuleContext context, Func<RoundTypeDefinition?> current, Action<CCSPlayerController, BuyDecision> decided, Action<CCSPlayerController> restoreGuns)` : `OnBuy(CCSPlayerController?, CommandInfo)` → `HookResult`, `OnItemPickup(EventItemPickup)`, `ClearPending()`.

Code adaptateur : vérifié par build, tests existants et checklist ; la logique est testée en Task 1.

- [ ] **Step 1: Applicateur**

Dans `src/RetakeV4/Modules/Allocation/LoadoutApplier.cs`, ajouter après `SwapWeapons` :
```csharp
    // After a captured native buy: every gun goes, the round's guns come back (the bought one never stays).
    public static void ReplaceGuns(CCSPlayerController player, Loadout loadout)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid || pawn.WeaponServices is null)
        {
            return;
        }
        var guns = pawn.WeaponServices.MyWeapons
            .Select(handle => handle.Value)
            .Where(weapon => weapon is { IsValid: true } && (WeaponCatalog.IsPrimary(weapon.DesignerName) || WeaponCatalog.IsSecondary(weapon.DesignerName)))
            .ToList();
        foreach (var weapon in guns)
        {
            weapon!.Remove();
        }
        player.GiveNamedItem(loadout.Secondary);
        if (loadout.Primary is { } primary)
        {
            player.GiveNamedItem(primary);
        }
    }

    public static void ResetCash(CCSPlayerController player)
    {
        if (player.InGameMoneyServices is not { } money)
        {
            return;
        }
        money.Account = NativeBuy.Cash;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
    }
```
(ajouter `using CounterStrikeSharp.API;` si absent).

- [ ] **Step 2: Sélecteur**

`src/RetakeV4/Modules/Allocation/NativeBuySelector.cs`
```csharp
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using RetakeV4.Adapters;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

// The CS2 buy menu as a weapon picker (agora's method): a resolved buy is blocked and treated as a choice; an ambiguous one
// (numeric buy-menu id) goes through, and the bought item is identified at item_pickup, removed, and the round's guns restored.
internal sealed class NativeBuySelector
{
    private static readonly TimeSpan CaptureWindow = TimeSpan.FromSeconds(2);

    private readonly ModuleContext _context;
    private readonly Func<RoundTypeDefinition?> _current;
    private readonly Action<CCSPlayerController, BuyDecision> _decided;
    private readonly Action<CCSPlayerController> _restoreGuns;
    private readonly Dictionary<int, DateTimeOffset> _pending = new();

    public NativeBuySelector(
        ModuleContext context, Func<RoundTypeDefinition?> current, Action<CCSPlayerController, BuyDecision> decided, Action<CCSPlayerController> restoreGuns)
    {
        _context = context;
        _current = current;
        _decided = decided;
        _restoreGuns = restoreGuns;
    }

    public HookResult OnBuy(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true } || PlayerQueries.SideOf(player) is not { } side || GameRulesAccessor.IsWarmup())
        {
            return HookResult.Continue;
        }
        var arguments = Enumerable.Range(1, Math.Max(0, command.ArgCount - 1)).Select(command.GetArg).Append(command.ArgString);
        var request = NativeBuyResolver.Resolve(arguments);
        switch (request.Kind)
        {
            case BuyRequestKind.AutoManaged:
                Alert(player, "allocation.buy.auto_managed");
                return HookResult.Handled;
            case BuyRequestKind.Weapon when request.Weapon is { } weapon:
                _decided(player, NativeBuy.Decide(weapon, side, _current()));
                return HookResult.Handled;
            default:
                _pending[player.Slot] = DateTimeOffset.UtcNow + CaptureWindow;
                return HookResult.Continue;
        }
    }

    public void OnItemPickup(EventItemPickup e)
    {
        if (e.Userid is not { IsValid: true } player || !_pending.Remove(player.Slot, out var until) || DateTimeOffset.UtcNow > until)
        {
            return;
        }
        if (PlayerQueries.SideOf(player) is not { } side)
        {
            return;
        }
        RemoveItem(player, e.Item);
        LoadoutApplier.ResetCash(player);
        if (NativeBuyResolver.FromPickup(e.Defindex, e.Item) is { } weapon)
        {
            _decided(player, NativeBuy.Decide(weapon, side, _current()));
        }
        else if (NativeBuyResolver.IsAutoManaged(e.Item))
        {
            Alert(player, "allocation.buy.auto_managed");
        }
        _restoreGuns(player);
    }

    public void ClearPending() => _pending.Clear();

    private static void RemoveItem(CCSPlayerController player, string? item)
    {
        if (item is null || player.PlayerPawn.Value is not { IsValid: true, WeaponServices: { } services })
        {
            return;
        }
        var bought = NativeBuyResolver.Normalize(item);
        var matching = services.MyWeapons
            .Select(handle => handle.Value)
            .Where(weapon => weapon is { IsValid: true } && NativeBuyResolver.Normalize(weapon.DesignerName) == bought)
            .ToList();
        foreach (var weapon in matching)
        {
            weapon!.Remove();
        }
    }

    private void Alert(CCSPlayerController player, string key) =>
        _context.Bus.Publish(new HudAlert(new PlayerId(player.Slot), HudText.Of(key)));
}
```

- [ ] **Step 3: Branchement**

Dans `src/RetakeV4/Modules/Allocation/AllocationModule.cs` :
- ajouter le champ `private NativeBuySelector? _nativeBuy;` ;
- dans `Load`, remplacer la boucle d'enregistrement des alias `!guns` par :
```csharp
        foreach (var alias in GunsAliases)
        {
            hooks.Command($"css_{alias}", "Opens the weapon menu", (player, _) => OnGunsCommand(player));
        }
        if (AllocationModes.UsesNativeBuy(_config.Mode))
        {
            var nativeBuy = new NativeBuySelector(context, () => _current, OnNativeBuy, RestoreGuns);
            _nativeBuy = nativeBuy;
            hooks.CommandListener("buy", nativeBuy.OnBuy, HookMode.Pre);
            hooks.OnEvent<EventItemPickup>("item_pickup", nativeBuy.OnItemPickup);
        }
        if (_config.HowToIntervalMinutes > 0)
        {
            hooks.RepeatTimer("howto", _config.HowToIntervalMinutes * 60f, () => Context.Text.ChatAll(AllocationModes.HowToKey(_config.Mode)));
        }
        ApplyBuyCvars();
```
- remplacer le début de `OnGunsCommand` par :
```csharp
    private void OnGunsCommand(CCSPlayerController? player)
    {
        if (player is not { IsValid: true } || player.SteamID == 0)
        {
            return;
        }
        if (!AllocationModes.UsesMenu(_config.Mode))
        {
            Context.Text.Chat(player, AllocationModes.HowToKey(_config.Mode));
            return;
        }
        _menuSeen.Add(player.SteamID);
        OpenMenu(player, refreshOnly: false);
    }
```
- dans `AutoOpenMenus`, remplacer la garde `if (!_config.AutoOpenMenu || _current is not { } current)` par `if (!_config.AutoOpenMenu || !AllocationModes.UsesMenu(_config.Mode) || _current is not { } current)` ;
- dans `AssignLoadouts`, après la boucle d'application des loadouts (avant les alertes AWP), ajouter :
```csharp
        if (_nativeBuy is not null)
        {
            _nativeBuy.ClearPending();
            ApplyBuyCvars();
            foreach (var (controller, _) in players)
            {
                LoadoutApplier.ResetCash(controller);
            }
        }
```
- dans `Unload`, ajouter `_nativeBuy = null;` ;
- ajouter les méthodes :
```csharp
    private void OnNativeBuy(CCSPlayerController player, BuyDecision decision)
    {
        if (_preferences is not { } preferences)
        {
            return;
        }
        var id = new PlayerId(player.Slot);
        switch (decision.Outcome)
        {
            case BuyOutcome.SetWeapon when decision.Selection is { } selection:
                ApplySelection(player, preferences, selection);
                break;
            case BuyOutcome.AwpVolunteer:
                if (!preferences.IsAwpVolunteer(player.SteamID))
                {
                    preferences.ToggleAwp(player.SteamID);
                }
                Context.Bus.Publish(new HudAlert(id, HudText.Of("allocation.buy.awp_volunteer")));
                break;
            default:
                Context.Bus.Publish(new HudAlert(id, HudText.Of("allocation.buy.not_available")));
                break;
        }
    }

    private void RestoreGuns(CCSPlayerController player)
    {
        if (player.PawnIsAlive && _lastPlan.TryGetValue(new PlayerId(player.Slot), out var loadout))
        {
            LoadoutApplier.ReplaceGuns(player, loadout);
        }
    }

    private void ApplyBuyCvars()
    {
        foreach (var (name, value) in AllocationModes.Cvars(_config.Mode))
        {
            Server.ExecuteCommand($"{name} {value}");
        }
    }
```

- [ ] **Step 4: Vérifier**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4/Modules/Allocation
git commit -m "feat: choix des armes par le menu d'achat natif (NativeBuy/Both), cvars par mode et rappel périodique"
```

---

### Task 4: Checklist en jeu phase 5a, documentation et vérification finale

**Files:**
- Modify: `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`

- [ ] **Step 1: Checklist**

Ajouter à `docs/CHECKLIST-INGAME.md` :
```markdown

## Phase 5a — Achat natif (NativeBuy / Both)
- [ ] `Mode: Menu` (défaut) : menu d'achat fermé (`mp_buytime 0`), `!guns` inchangé, rappel « Tape !guns… » toutes les 5 minutes.
- [ ] `Mode: NativeBuy` : `!guns` répond par le rappel ; pas d'ouverture automatique du menu ; 16000 $ affichés à chaque round.
- [ ] Freeze time, vivant : acheter une arme du pool dans le menu d'achat (B) → l'arme est remplacée immédiatement, alerte « Armes mises à jour. », argent revenu à 16000 $, aucune arme en double au sol.
- [ ] « Acheter » l'arme qu'on tient déjà → on la garde.
- [ ] Round live : acheter une arme → « Enregistré, utilisé dès le prochain round. », l'arme achetée est retirée, on garde ses armes du round.
- [ ] Acheter une arme hors du pool du round (ex. M4A4 en T) → « Cette arme n'est pas disponible pour ce round. », préférence inchangée.
- [ ] Acheter l'AWP → « Tu es maintenant volontaire pour l'AWP. », visible dans `!guns` (mode Both).
- [ ] Acheter une grenade, l'armure ou le kit → « … donnés automatiquement. », rien n'est ajouté.
- [ ] `css_buy ak47` dans la console (commande nommée) : traité sans passer par la capture.
- [ ] `Mode: Both` : `!guns` et le menu d'achat modifient la même préférence.
- [ ] `HowToIntervalMinutes: 0` : aucun rappel ; changement de map puis `css_plugins reload RetakeV4` : un seul rappel par intervalle (pas de timer en double).
- [ ] Mode FastPlant + achat pendant le freeze time : le poseur garde la C4.
```

- [ ] **Step 2: CLAUDE.md**

Dans la section Règles de `CLAUDE.md`, ajouter :
```markdown
- Achat natif : un achat n'équipe jamais rien. La résolution (`NativeBuyResolver`) et la décision (`NativeBuy.Decide`) sont dans le Domain ; `NativeBuySelector` applique via le même chemin que le menu d'armes. Timers de module uniquement via `hooks.RepeatTimer`.
```

- [ ] **Step 3: Vérification finale**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo && dotnet test tests/RetakeV4.Domain.Tests --nologo -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`
puis (outil PowerShell) `pwsh -NoProfile -File scripts/package-dev.ps1`
Expected: 0 warning, `Failed: 0`, couverture Domain ≥ 80 %, `Package ready`.

- [ ] **Step 4: Commit**

```bash
git add docs/CHECKLIST-INGAME.md CLAUDE.md
git commit -m "docs: checklist en jeu phase 5a et règles de l'achat natif"
```
