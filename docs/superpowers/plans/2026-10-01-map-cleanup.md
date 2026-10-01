# Module MapCleanup — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un module RetakeV4 qui ouvre les portes et casse vitres et aérations à chaque round, sans rien toucher d'autre, avec des corrections par map faites dans un éditeur en jeu.

**Architecture:** Toute décision (classification, corrections, clés d'entité, tirage, plan de passe, entité visée, menu de l'éditeur) est dans `RetakeV4.Domain/MapCleanup` et testée ; `Modules/MapCleanup` lit les entités CS2 des 5 classes candidates, envoie `Open`/`Break`, gère l'étape de préparation, la vérification de fin de freeze et l'éditeur (menu HUD). Les corrections vivent dans `configs/plugins/RetakeV4/mapcleanup/<map>.json`.

**Tech Stack:** C# / .NET 10, CounterStrikeSharp 1.0.370, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-01-map-cleanup-design.md`

## Global Constraints

- Build : `dotnet build RetakeV4.sln -c Release`, 0 warning.
- Domain sans CounterStrikeSharp, couverture ≥ 80 %.
- Entrées envoyées : `Open` (Door), `Break` (Window, Vent). Jamais `Kill`, `Remove` ni autre.
- Textes joueurs via `lang/en.json` + `lang/fr.json` (clés `mapcleanup.*`, `admin.menu.cleanup`).
- Défauts de config uniquement dans `MapCleanupConfig`.
- Tout hasard via `IRandom` ; callbacks `NextFrame`/timer via `context.Guard.Run`.
- Actions d'administration : le module Admin vérifie la permission et publie `MapCleanupEditorRequested` ; MapCleanup l'applique.
- Version 4.4.0. Commits conventionnels terminés par `Claude-Session: https://claude.ai/code/session_01HDPHjTUQicVkaLyXFv7jpM`.

## Rulings sur la spec

- **Entité visée :** CounterStrikeSharp n'expose pas de tracé de rayon sans gamedata. L'éditeur choisit l'entité candidate dont le centre est le plus proche de l'axe du regard, dans un cône de 12° et à 1500 unités maximum (`AimPicker`, Domain). Coût si faux : une entité derrière un mur peut être choisie ; le panneau affiche classe et modèle, l'admin voit l'erreur.
- **Modèle seulement :** le matériau n'est pas lisible simplement ; la classification utilise la classe et le nom du modèle.
- **Candidats :** seules les 5 classes `func_door`, `func_door_rotating`, `prop_door_rotating`, `func_breakable`, `func_breakable_surf` sont lues, par la passe comme par l'éditeur. Une correction ne peut donc viser que ces classes, ce qui empêche de toucher le décor par erreur.
- **Surbrillance :** hors de ce plan (optionnelle dans la spec) ; le panneau suffit pour identifier l'entité.
- **Vérification de fin de freeze :** elle renvoie la même action aux cibles encore valides (`Open` sur une porte ouverte est sans effet ; une vitre cassée n'existe plus ou a 0 PV).

## Review Focus

1. Deux entités sans nom, même classe, à la même position arrondie : la seconde garde la même clé → une correction s'applique aux deux (comportement attendu, documenté).
2. Fichier de corrections édité à la main avec une catégorie inconnue : la ligne est ignorée, les autres s'appliquent, un avertissement est logué.
3. Map sans aucune entité candidate : la passe ne fait rien, aucune erreur.
4. Éditeur ouvert pendant un changement de map : la session se ferme sans sauvegarde, pas d'exception.
5. `DoorOpenChancePercent` à 0 : aucune porte ouverte, vitres et aérations quand même cassées.

Chaque point a son test dans la tâche propriétaire (1 : tâche 1 ; 2 : tâche 2 ; 3 et 5 : tâche 2 ; 4 : tâche 5).

---

### Task 1: Classification et clés d'entité (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/MapCleanup/CleanupKind.cs`, `EntityFacts.cs`, `CleanupClassifier.cs`, `EntityKey.cs`
- Test: `tests/RetakeV4.Domain.Tests/MapCleanup/CleanupClassifierTests.cs`, `EntityKeyTests.cs`

**Interfaces:**
- Produces : `enum CleanupKind { Ignore, Door, Window, Vent }` ; `record EntityFacts(string ClassName, string? ModelName, string? TargetName, Vec3 Origin)` ; `static CleanupClassifier.Detect(EntityFacts) : CleanupKind` ; `static CleanupClassifier.Classify(EntityFacts, string key, IReadOnlyDictionary<string, CleanupKind> overrides) : CleanupKind` ; `static CleanupClassifier.CandidateClasses : IReadOnlyList<string>` ; `static EntityKey.For(EntityFacts, bool nameIsUnique) : string`.

- [ ] **Step 1: Tests**

```csharp
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class CleanupClassifierTests
{
    private static EntityFacts Facts(string cls, string? model = null) => new(cls, model, null, new Vec3(0, 0, 0));

    [Theory]
    [InlineData("func_door", null, CleanupKind.Door)]
    [InlineData("func_door_rotating", null, CleanupKind.Door)]
    [InlineData("prop_door_rotating", "models/props/de_inferno/door.vmdl", CleanupKind.Door)]
    [InlineData("func_breakable_surf", null, CleanupKind.Window)]
    [InlineData("func_breakable", "models/props_windows/glass_pane.vmdl", CleanupKind.Window)]
    [InlineData("func_breakable", "maps/de_mirage/WINDOW_frame.vmdl", CleanupKind.Window)]
    [InlineData("func_breakable", "models/props/de_nuke/vent_cover.vmdl", CleanupKind.Vent)]
    [InlineData("func_breakable", "models/props/metal_grate01.vmdl", CleanupKind.Vent)]
    [InlineData("func_breakable", "models/props/crate.vmdl", CleanupKind.Ignore)]
    [InlineData("func_breakable", null, CleanupKind.Ignore)]
    [InlineData("prop_physics", "models/props/glass_bottle.vmdl", CleanupKind.Ignore)]
    [InlineData("prop_physics_multiplayer", "models/vent.vmdl", CleanupKind.Ignore)]
    [InlineData("func_brush", "models/glass.vmdl", CleanupKind.Ignore)]
    [InlineData("prop_dynamic", "models/door.vmdl", CleanupKind.Ignore)]
    public void Detect_IsConservative(string cls, string? model, CleanupKind expected)
    {
        Assert.Equal(expected, CleanupClassifier.Detect(Facts(cls, model)));
    }

    [Fact]
    public void Override_AlwaysWins()
    {
        var overrides = new Dictionary<string, CleanupKind> { ["name:door_a"] = CleanupKind.Ignore, ["name:box"] = CleanupKind.Vent };
        Assert.Equal(CleanupKind.Ignore, CleanupClassifier.Classify(Facts("func_door"), "name:door_a", overrides));
        Assert.Equal(CleanupKind.Vent, CleanupClassifier.Classify(Facts("func_breakable", "crate"), "name:box", overrides));
        Assert.Equal(CleanupKind.Door, CleanupClassifier.Classify(Facts("func_door"), "name:other", overrides));
    }

    [Fact]
    public void CandidateClasses_AreTheFiveKnownClasses()
    {
        Assert.Equal(new[] { "func_breakable", "func_breakable_surf", "func_door", "func_door_rotating", "prop_door_rotating" },
            CleanupClassifier.CandidateClasses.Order());
    }
}

public class EntityKeyTests
{
    [Fact]
    public void UniqueName_IsUsed()
    {
        Assert.Equal("name:mid_door", EntityKey.For(new EntityFacts("func_door", null, "mid_door", new Vec3(1, 2, 3)), nameIsUnique: true));
    }

    [Fact]
    public void NoNameOrSharedName_FallsBackToClassAndRoundedPosition()
    {
        var facts = new EntityFacts("func_breakable", null, "window", new Vec3(10.4f, -3.6f, 64.5f));
        Assert.Equal("pos:func_breakable@10,-4,65", EntityKey.For(facts, nameIsUnique: false));
        Assert.Equal("pos:func_breakable@10,-4,65", EntityKey.For(facts with { TargetName = null }, nameIsUnique: true));
    }

    // Two unnamed entities of the same class at the same rounded spot share a key: a correction applies to both.
    [Fact]
    public void SameRoundedSpot_SharesTheKey()
    {
        var a = new EntityFacts("func_breakable", null, null, new Vec3(10.2f, 0, 0));
        var b = new EntityFacts("func_breakable", null, null, new Vec3(9.8f, 0, 0));
        Assert.Equal(EntityKey.For(a, false), EntityKey.For(b, false));
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/RetakeV4.Domain.Tests --filter "CleanupClassifierTests|EntityKeyTests"` — Expected: FAIL (compilation).

- [ ] **Step 3: Implementation**

```csharp
// CleanupKind.cs
namespace RetakeV4.Domain.MapCleanup;

public enum CleanupKind
{
    Ignore,
    Door,
    Window,
    Vent,
}
```

```csharp
// EntityFacts.cs
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.MapCleanup;

public sealed record EntityFacts(string ClassName, string? ModelName, string? TargetName, Vec3 Origin);
```

```csharp
// CleanupClassifier.cs
namespace RetakeV4.Domain.MapCleanup;

// Conservative on purpose: anything not clearly a door, a window or a vent is never touched (the old plugin broke physics
// props and map brushes by guessing from class names).
public static class CleanupClassifier
{
    public static IReadOnlyList<string> CandidateClasses { get; } = new[]
    {
        "func_door", "func_door_rotating", "prop_door_rotating", "func_breakable", "func_breakable_surf",
    };

    private static readonly string[] DoorClasses = { "func_door", "func_door_rotating", "prop_door_rotating" };
    private static readonly string[] WindowTokens = { "glass", "window" };
    private static readonly string[] VentTokens = { "vent", "grate" };

    public static CleanupKind Classify(EntityFacts facts, string key, IReadOnlyDictionary<string, CleanupKind> overrides) =>
        overrides.TryGetValue(key, out var kind) ? kind : Detect(facts);

    public static CleanupKind Detect(EntityFacts facts)
    {
        var cls = facts.ClassName.ToLowerInvariant();
        if (DoorClasses.Contains(cls))
        {
            return CleanupKind.Door;
        }
        if (cls == "func_breakable_surf")
        {
            return CleanupKind.Window;
        }
        if (cls != "func_breakable" || string.IsNullOrEmpty(facts.ModelName))
        {
            return CleanupKind.Ignore;
        }
        var model = facts.ModelName.ToLowerInvariant();
        if (WindowTokens.Any(model.Contains))
        {
            return CleanupKind.Window;
        }
        return VentTokens.Any(model.Contains) ? CleanupKind.Vent : CleanupKind.Ignore;
    }
}
```

```csharp
// EntityKey.cs
using System.Globalization;

namespace RetakeV4.Domain.MapCleanup;

// Stable across rounds: entities are recreated at every round restart, so the entity index cannot be used.
public static class EntityKey
{
    public static string For(EntityFacts facts, bool nameIsUnique)
    {
        if (nameIsUnique && !string.IsNullOrWhiteSpace(facts.TargetName))
        {
            return $"name:{facts.TargetName}";
        }
        var o = facts.Origin;
        return string.Create(CultureInfo.InvariantCulture,
            $"pos:{facts.ClassName.ToLowerInvariant()}@{MathF.Round(o.X)},{MathF.Round(o.Y)},{MathF.Round(o.Z)}");
    }
}
```

(Si `MathF.Round` produit `-0`, normaliser : `Round(v) + 0f` ; le test `-3.6 → -4` reste valide.)

- [ ] **Step 4: Run** même commande — Expected: PASS.
- [ ] **Step 5: Commit** `feat: classification prudente des entités de nettoyage et clé stable (Domain)`

### Task 2: Corrections, tirage et plan de passe (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/MapCleanup/CleanupOverrides.cs`, `DoorRoll.cs`, `CleanupPlan.cs`
- Test: `tests/RetakeV4.Domain.Tests/MapCleanup/CleanupOverridesTests.cs`, `CleanupPlanTests.cs`

**Interfaces:**
- Consumes : Task 1.
- Produces :
  - `record CleanupOverride(string Key, CleanupKind Kind, string Note)` ; `static CleanupOverridesFormat.Parse(string json) : (IReadOnlyList<CleanupOverride> Overrides, IReadOnlyList<string> Issues, bool Invalid)` ; `static CleanupOverridesFormat.Serialize(IEnumerable<CleanupOverride>) : string` ; `static CleanupOverridesFormat.ToMap(IEnumerable<CleanupOverride>) : IReadOnlyDictionary<string, CleanupKind>`.
  - `static DoorRoll.ShouldOpen(int chancePercent, IRandom random) : bool`.
  - `record CleanupSettings(bool OpenDoors, int DoorOpenChancePercent, bool BreakWindows, bool BreakVents, int MaxEntities)` ; `record CleanupCandidate(int Handle, EntityFacts Facts, string Key)` ; `enum CleanupAction { Open, Break }` ; `record CleanupTarget(int Handle, CleanupKind Kind, CleanupAction Action)` ; `static CleanupPlan.Build(IReadOnlyList<CleanupCandidate>, IReadOnlyDictionary<string, CleanupKind>, CleanupSettings, IRandom) : IReadOnlyList<CleanupTarget>`.

- [ ] **Step 1: Tests**

```csharp
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class CleanupOverridesTests
{
    [Fact]
    public void RoundTrip()
    {
        var list = new[] { new CleanupOverride("name:a", CleanupKind.Ignore, "func_door"), new CleanupOverride("pos:func_breakable@1,2,3", CleanupKind.Vent, "x") };
        var (parsed, issues, invalid) = CleanupOverridesFormat.Parse(CleanupOverridesFormat.Serialize(list));
        Assert.False(invalid);
        Assert.Empty(issues);
        Assert.Equal(list, parsed);
    }

    [Fact]
    public void UnknownKind_LineIgnored_OthersKept()
    {
        var json = "[{\"Key\":\"a\",\"Kind\":\"Door\",\"Note\":\"\"},{\"Key\":\"b\",\"Kind\":\"Banana\",\"Note\":\"\"},{\"Key\":\"\",\"Kind\":\"Vent\"}]";
        var (parsed, issues, invalid) = CleanupOverridesFormat.Parse(json);
        Assert.False(invalid);
        Assert.Equal(new[] { "a" }, parsed.Select(o => o.Key));
        Assert.Equal(2, issues.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    public void CorruptFile_IsReported(string json)
    {
        var (parsed, _, invalid) = CleanupOverridesFormat.Parse(json);
        Assert.True(invalid);
        Assert.Empty(parsed);
    }

    [Fact]
    public void ToMap_LastWins()
    {
        var map = CleanupOverridesFormat.ToMap(new[] { new CleanupOverride("a", CleanupKind.Door, ""), new CleanupOverride("a", CleanupKind.Vent, "") });
        Assert.Equal(CleanupKind.Vent, map["a"]);
    }
}

public class CleanupPlanTests
{
    private static readonly CleanupSettings All = new(true, 100, true, true, 512);
    private static CleanupCandidate C(int handle, string cls, string? model = null) =>
        new(handle, new EntityFacts(cls, model, null, new Vec3(handle, 0, 0)), $"k{handle}");

    [Fact]
    public void DoorsOpen_WindowsAndVentsBreak_RestIgnored()
    {
        var plan = CleanupPlan.Build(new[] { C(1, "func_door"), C(2, "func_breakable_surf"), C(3, "func_breakable", "vent"), C(4, "func_breakable", "crate") },
            new Dictionary<string, CleanupKind>(), All, new FixedRandom());
        Assert.Equal(new[] { (1, CleanupAction.Open), (2, CleanupAction.Break), (3, CleanupAction.Break) }, plan.Select(t => (t.Handle, t.Action)));
    }

    [Fact]
    public void DisabledCategories_AreSkipped_AndZeroChanceOpensNoDoor()
    {
        var settings = All with { BreakVents = false, DoorOpenChancePercent = 0 };
        var plan = CleanupPlan.Build(new[] { C(1, "func_door"), C(2, "func_breakable_surf"), C(3, "func_breakable", "vent") },
            new Dictionary<string, CleanupKind>(), settings, new FixedRandom());
        Assert.Equal(new[] { 2 }, plan.Select(t => t.Handle));
    }

    [Fact]
    public void Overrides_AreApplied()
    {
        var plan = CleanupPlan.Build(new[] { C(1, "func_door"), C(2, "func_breakable", "crate") },
            new Dictionary<string, CleanupKind> { ["k1"] = CleanupKind.Ignore, ["k2"] = CleanupKind.Window }, All, new FixedRandom());
        Assert.Equal(new[] { (2, CleanupAction.Break) }, plan.Select(t => (t.Handle, t.Action)));
    }

    [Fact]
    public void MaxEntities_CapsThePass()
    {
        var plan = CleanupPlan.Build(Enumerable.Range(1, 10).Select(i => C(i, "func_breakable_surf")).ToList(),
            new Dictionary<string, CleanupKind>(), All with { MaxEntities = 3 }, new FixedRandom());
        Assert.Equal(3, plan.Count);
    }

    [Fact]
    public void NoCandidates_EmptyPlan()
    {
        Assert.Empty(CleanupPlan.Build(Array.Empty<CleanupCandidate>(), new Dictionary<string, CleanupKind>(), All, new FixedRandom()));
    }

    [Theory]
    [InlineData(0, 0.0, false)]
    [InlineData(100, 0.999, true)]
    [InlineData(50, 0.49, true)]
    [InlineData(50, 0.5, false)]
    public void DoorRoll_UsesTheChance(int chance, double draw, bool expected)
    {
        Assert.Equal(expected, DoorRoll.ShouldOpen(chance, new FixedRandom { DoubleValue = draw }));
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/RetakeV4.Domain.Tests --filter "CleanupOverridesTests|CleanupPlanTests"` — FAIL (compilation).

- [ ] **Step 3: Implementation**

```csharp
// CleanupOverrides.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetakeV4.Domain.MapCleanup;

public sealed record CleanupOverride(string Key, CleanupKind Kind, string Note);

public static class CleanupOverridesFormat
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed record Row(string? Key, string? Kind, string? Note);

    public static string Serialize(IEnumerable<CleanupOverride> overrides) =>
        JsonSerializer.Serialize(overrides.Select(o => new Row(o.Key, o.Kind.ToString(), o.Note)).ToList(), Options);

    // Invalid = the whole file is unreadable (kept aside by the caller); a bad line is only skipped and reported.
    public static (IReadOnlyList<CleanupOverride> Overrides, IReadOnlyList<string> Issues, bool Invalid) Parse(string json)
    {
        List<Row>? rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<Row>>(json, Options);
        }
        catch (JsonException)
        {
            return (Array.Empty<CleanupOverride>(), Array.Empty<string>(), true);
        }
        var kept = new List<CleanupOverride>();
        var issues = new List<string>();
        foreach (var (row, index) in (rows ?? new List<Row>()).Select((r, i) => (r, i)))
        {
            if (string.IsNullOrWhiteSpace(row?.Key))
            {
                issues.Add($"entry {index}: no key");
                continue;
            }
            if (!Enum.TryParse<CleanupKind>(row.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
            {
                issues.Add($"entry {index} ({row.Key}): unknown kind '{row.Kind}'");
                continue;
            }
            kept.Add(new CleanupOverride(row.Key, kind, row.Note ?? string.Empty));
        }
        return (kept, issues, false);
    }

    public static IReadOnlyDictionary<string, CleanupKind> ToMap(IEnumerable<CleanupOverride> overrides)
    {
        var map = new Dictionary<string, CleanupKind>(StringComparer.Ordinal);
        foreach (var o in overrides)
        {
            map[o.Key] = o.Kind;
        }
        return map;
    }
}
```

```csharp
// DoorRoll.cs
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.MapCleanup;

public static class DoorRoll
{
    public static bool ShouldOpen(int chancePercent, IRandom random) =>
        chancePercent >= 100 || (chancePercent > 0 && random.NextDouble() * 100 < chancePercent);
}
```

```csharp
// CleanupPlan.cs
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.MapCleanup;

public sealed record CleanupSettings(bool OpenDoors, int DoorOpenChancePercent, bool BreakWindows, bool BreakVents, int MaxEntities);

public sealed record CleanupCandidate(int Handle, EntityFacts Facts, string Key);

public enum CleanupAction
{
    Open,
    Break,
}

public sealed record CleanupTarget(int Handle, CleanupKind Kind, CleanupAction Action);

public static class CleanupPlan
{
    public static IReadOnlyList<CleanupTarget> Build(IReadOnlyList<CleanupCandidate> candidates,
        IReadOnlyDictionary<string, CleanupKind> overrides, CleanupSettings settings, IRandom random)
    {
        var targets = new List<CleanupTarget>();
        foreach (var candidate in candidates)
        {
            if (targets.Count >= settings.MaxEntities)
            {
                break;
            }
            var kind = CleanupClassifier.Classify(candidate.Facts, candidate.Key, overrides);
            var target = kind switch
            {
                CleanupKind.Door when settings.OpenDoors && DoorRoll.ShouldOpen(settings.DoorOpenChancePercent, random)
                    => new CleanupTarget(candidate.Handle, kind, CleanupAction.Open),
                CleanupKind.Window when settings.BreakWindows => new CleanupTarget(candidate.Handle, kind, CleanupAction.Break),
                CleanupKind.Vent when settings.BreakVents => new CleanupTarget(candidate.Handle, kind, CleanupAction.Break),
                _ => null,
            };
            if (target is not null)
            {
                targets.Add(target);
            }
        }
        return targets;
    }
}
```

- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat: corrections par map, tirage des portes et plan de passe du nettoyage (Domain)`

### Task 3: Entité visée et menu de l'éditeur (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/MapCleanup/AimPicker.cs`, `CleanupEditorMenu.cs`
- Modify: `src/RetakeV4.Domain/Admin/AdminMenu.cs` (entrée + `AdminAction.MapCleanupEditor`, `RetakeCommandKind.Cleanup`)
- Test: `tests/RetakeV4.Domain.Tests/MapCleanup/AimPickerTests.cs`, `CleanupEditorMenuTests.cs`, `tests/RetakeV4.Domain.Tests/Admin/AdminMenuTests.cs` (ajouts)

**Interfaces:**
- Produces :
  - `static AimPicker.Pick(Vec3 eye, Vec3 forward, IReadOnlyList<(int Handle, Vec3 Center)> candidates, float maxDegrees = 12f, float maxDistance = 1500f) : int?`
  - `record CleanupEditorView(string? ClassName, string? ModelName, string? TargetName, CleanupKind? Detected, CleanupKind? Effective, bool HasOverride, bool Dirty)` ; `static CleanupEditorMenu.MenuId = "mapcleanup.editor"` ; `static CleanupEditorMenu.Build(CleanupEditorView) : Menu` ; `static CleanupEditorMenu.Parse(string itemId) : CleanupEditorCommand?` avec `record CleanupEditorCommand(CleanupEditorCommandKind Kind, CleanupKind? Kind2 = null)` et `enum CleanupEditorCommandKind { Pick, Set, Auto, Test, Save, Exit, ExitSave, ExitDiscard }`.
  - `AdminAction.MapCleanupEditor`, `AdminMenu.CleanupId = "cleanup"`, `RetakeCommandKind.Cleanup` (`!retake cleanup`).

- [ ] **Step 1: Tests**

```csharp
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class AimPickerTests
{
    private static readonly Vec3 Eye = new(0, 0, 0);
    private static readonly Vec3 Forward = new(1, 0, 0);

    [Fact]
    public void PicksTheEntityClosestToTheViewAxis()
    {
        var pick = AimPicker.Pick(Eye, Forward, new[] { (1, new Vec3(500, 80, 0)), (2, new Vec3(800, 10, 0)) });
        Assert.Equal(2, pick);
    }

    [Fact]
    public void OutsideTheConeOrTooFarOrBehind_IsNotPicked()
    {
        Assert.Null(AimPicker.Pick(Eye, Forward, new[] { (1, new Vec3(100, 100, 0)), (2, new Vec3(2000, 0, 0)), (3, new Vec3(-300, 0, 0)) }));
    }
}

public class CleanupEditorMenuTests
{
    [Fact]
    public void Build_ListsChoicesAndActions_AndMarksTheEffectiveKind()
    {
        var menu = CleanupEditorMenu.Build(new CleanupEditorView("func_door", "door.vmdl", null, CleanupKind.Door, CleanupKind.Ignore, true, true));
        Assert.Equal(CleanupEditorMenu.MenuId, menu.Id);
        Assert.Contains(menu.Items, i => i.Id == "set:Ignore" && i.IsOn);
        Assert.Contains(menu.Items, i => i.Id == "set:Door" && !i.IsOn);
        Assert.Contains(menu.Items, i => i.Id == "auto");
        Assert.Contains(menu.Items, i => i.Id == "save");
    }

    [Theory]
    [InlineData("pick", CleanupEditorCommandKind.Pick, null)]
    [InlineData("set:Vent", CleanupEditorCommandKind.Set, CleanupKind.Vent)]
    [InlineData("auto", CleanupEditorCommandKind.Auto, null)]
    [InlineData("test", CleanupEditorCommandKind.Test, null)]
    [InlineData("save", CleanupEditorCommandKind.Save, null)]
    [InlineData("exit:save", CleanupEditorCommandKind.ExitSave, null)]
    public void Parse_KnownIds(string id, CleanupEditorCommandKind kind, CleanupKind? value)
    {
        Assert.Equal(new CleanupEditorCommand(kind, value), CleanupEditorMenu.Parse(id));
    }

    [Theory]
    [InlineData("set:Banana")]
    [InlineData("nope")]
    public void Parse_Unknown_IsNull(string id)
    {
        Assert.Null(CleanupEditorMenu.Parse(id));
    }
}
```

Ajouts à `AdminMenuTests` :

```csharp
[Fact]
public void CleanupEntry_OpensTheMapCleanupEditor()
{
    Assert.Contains(AdminMenu.Build().Items, i => i.Id == AdminMenu.CleanupId);
    Assert.Equal(new AdminSelection(AdminAction.MapCleanupEditor), AdminMenu.Parse(AdminMenu.CleanupId));
}

[Theory]
[InlineData("cleanup")]
[InlineData("CLEANUP")]
[InlineData("nettoyage")]
public void RetakeCommand_Cleanup(string argument)
{
    Assert.Equal(RetakeCommandKind.Cleanup, RetakeCommand.Parse(argument));
}
```

- [ ] **Step 2: Run** `dotnet test tests/RetakeV4.Domain.Tests --filter "AimPickerTests|CleanupEditorMenuTests|AdminMenuTests"` — FAIL.

- [ ] **Step 3: Implementation**

```csharp
// AimPicker.cs
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.MapCleanup;

// No engine ray trace without gamedata: the editor targets the candidate closest to the view axis inside a narrow cone.
public static class AimPicker
{
    public static int? Pick(Vec3 eye, Vec3 forward, IReadOnlyList<(int Handle, Vec3 Center)> candidates, float maxDegrees = 12f, float maxDistance = 1500f)
    {
        var axis = forward * (1f / Math.Max(forward.Length, float.Epsilon));
        var minCos = MathF.Cos(maxDegrees * MathF.PI / 180f);
        int? best = null;
        var bestCos = minCos;
        foreach (var (handle, center) in candidates)
        {
            var to = center - eye;
            var distance = to.Length;
            if (distance <= float.Epsilon || distance > maxDistance)
            {
                continue;
            }
            var cos = Vec3.Dot(to, axis) / distance;
            if (cos >= bestCos)
            {
                bestCos = cos;
                best = handle;
            }
        }
        return best;
    }
}
```

```csharp
// CleanupEditorMenu.cs
using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.MapCleanup;

public sealed record CleanupEditorView(string? ClassName, string? ModelName, string? TargetName, CleanupKind? Detected, CleanupKind? Effective, bool HasOverride, bool Dirty);

public enum CleanupEditorCommandKind
{
    Pick,
    Set,
    Auto,
    Test,
    Save,
    Exit,
    ExitSave,
    ExitDiscard,
}

public sealed record CleanupEditorCommand(CleanupEditorCommandKind Kind, CleanupKind? Value = null);

public static class CleanupEditorMenu
{
    public const string MenuId = "mapcleanup.editor";
    private const string SetPrefix = "set:";

    public static Menu Build(CleanupEditorView view)
    {
        var items = new List<MenuItem>
        {
            new("pick", view.ClassName is null
                ? HudText.Of("mapcleanup.editor.none_aimed")
                : HudText.Of("mapcleanup.editor.aimed", view.ClassName, view.ModelName ?? "-", view.TargetName ?? "-"), MenuItemKind.Action),
        };
        if (view.ClassName is not null)
        {
            items.Add(new MenuItem("detected", HudText.Of("mapcleanup.editor.detected", KindKey(view.Detected)), MenuItemKind.Action));
            foreach (var kind in new[] { CleanupKind.Door, CleanupKind.Window, CleanupKind.Vent, CleanupKind.Ignore })
            {
                items.Add(new MenuItem(SetPrefix + kind, HudText.Of($"mapcleanup.kind.{kind.ToString().ToLowerInvariant()}"), MenuItemKind.Choice,
                    IsOn: view.HasOverride && view.Effective == kind));
            }
            items.Add(new MenuItem("auto", HudText.Of("mapcleanup.editor.auto"), MenuItemKind.Choice, IsOn: !view.HasOverride));
        }
        items.Add(new MenuItem("test", HudText.Of("mapcleanup.editor.test"), MenuItemKind.Action));
        items.Add(new MenuItem("save", HudText.Of("mapcleanup.editor.save"), MenuItemKind.Action));
        items.Add(view.Dirty
            ? new MenuItem("exit", HudText.Of("mapcleanup.editor.exit"), MenuItemKind.Submenu, Submenu: new Menu("mapcleanup.exit", HudText.Of("mapcleanup.editor.exit"), new[]
            {
                new MenuItem("exit:save", HudText.Of("mapcleanup.editor.exit_save"), MenuItemKind.Action),
                new MenuItem("exit:discard", HudText.Of("mapcleanup.editor.exit_discard"), MenuItemKind.Action),
            }))
            : new MenuItem("exit:discard", HudText.Of("mapcleanup.editor.exit"), MenuItemKind.Action));
        return new Menu(MenuId, HudText.Of("mapcleanup.editor.title"), items);
    }

    public static CleanupEditorCommand? Parse(string itemId)
    {
        if (itemId.StartsWith(SetPrefix, StringComparison.Ordinal))
        {
            return Enum.TryParse<CleanupKind>(itemId[SetPrefix.Length..], out var kind) && Enum.IsDefined(kind)
                ? new CleanupEditorCommand(CleanupEditorCommandKind.Set, kind)
                : null;
        }
        return itemId switch
        {
            "pick" => new CleanupEditorCommand(CleanupEditorCommandKind.Pick),
            "auto" => new CleanupEditorCommand(CleanupEditorCommandKind.Auto),
            "test" => new CleanupEditorCommand(CleanupEditorCommandKind.Test),
            "save" => new CleanupEditorCommand(CleanupEditorCommandKind.Save),
            "exit" => new CleanupEditorCommand(CleanupEditorCommandKind.Exit),
            "exit:save" => new CleanupEditorCommand(CleanupEditorCommandKind.ExitSave),
            "exit:discard" => new CleanupEditorCommand(CleanupEditorCommandKind.ExitDiscard),
            _ => null,
        };
    }

    private static string KindKey(CleanupKind? kind) => kind is null ? "-" : kind.Value.ToString();
}
```

`AdminMenu.cs` : ajouter `MapCleanupEditor` à `AdminAction`, `public const string CleanupId = "cleanup";`, l'entrée `new MenuItem(CleanupId, HudText.Of("admin.menu.cleanup"), MenuItemKind.Action)` après l'éditeur de spawns, `CleanupId => new AdminSelection(AdminAction.MapCleanupEditor)` dans `Parse`, `Cleanup` dans `RetakeCommandKind` et `"cleanup" or "nettoyage" => RetakeCommandKind.Cleanup` dans `RetakeCommand.Parse`.

- [ ] **Step 4: Run** — PASS (les tests `AdminMenuTests` existants restent verts ; ajuster un test qui compterait les entrées du menu).
- [ ] **Step 5: Commit** `feat: entité visée, menu de l'éditeur de nettoyage, entrée admin et !retake cleanup (Domain)`

### Task 4: Config, magasin des corrections, lang

**Files:**
- Create: `src/RetakeV4/Modules/MapCleanup/MapCleanupConfig.cs`, `MapCleanupConfigValidator.cs`, `CleanupOverrideStore.cs`
- Modify: `src/RetakeV4/lang/en.json`, `fr.json`, `tests/RetakeV4.Integration.Tests/Configuration/ConfigExportTests.cs`, `ModuleCatalogTests.cs` (en tâche 5)
- Test: `tests/RetakeV4.Integration.Tests/Modules/MapCleanup/MapCleanupConfigValidatorTests.cs`, `CleanupOverrideStoreTests.cs`

**Interfaces:**
- Produces : `MapCleanupConfig : ModuleConfig` (`OpenDoors=true`, `DoorOpenChancePercent=100`, `BreakWindows=true`, `BreakVents=true`, `MaxEntitiesPerRound=512`, `FreezeEndCheck=true`, `ToSettings() : CleanupSettings`) ; `CleanupOverrideStore(string directory)` avec `Load(string map) : (IReadOnlyList<CleanupOverride> Overrides, IReadOnlyList<string> Issues)` et `Save(string map, IReadOnlyList<CleanupOverride>)`.

- [ ] **Step 1: Tests**

```csharp
using RetakeV4.Modules.MapCleanup;

namespace RetakeV4.Integration.Tests.Modules.MapCleanup;

public class MapCleanupConfigValidatorTests
{
    private static readonly MapCleanupConfig Defaults = new();
    private readonly MapCleanupConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = _validator.Validate(Defaults, Defaults, "mapcleanup.json");
        Assert.Empty(result.Issues);
        Assert.Equal(100, result.Config.DoorOpenChancePercent);
        Assert.Equal(512, result.Config.MaxEntitiesPerRound);
    }

    [Theory]
    [InlineData(-1, 512, 1)]
    [InlineData(101, 512, 1)]
    [InlineData(50, 0, 1)]
    [InlineData(50, 5000, 1)]
    [InlineData(0, 1, 0)]
    public void OutOfRange_ReplacedByDefault(int chance, int max, int issues)
    {
        var result = _validator.Validate(Defaults with { DoorOpenChancePercent = chance, MaxEntitiesPerRound = max }, Defaults, "mapcleanup.json");
        Assert.Equal(issues, result.Issues.Count);
    }
}

public class CleanupOverrideStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void MissingFile_NoOverride()
    {
        var (overrides, issues) = new CleanupOverrideStore(_dir.Path).Load("de_dust2");
        Assert.Empty(overrides);
        Assert.Empty(issues);
    }

    [Fact]
    public void SaveThenLoad_AndPreviousVersionKept()
    {
        var store = new CleanupOverrideStore(_dir.Path);
        store.Save("de_dust2", new[] { new RetakeV4.Domain.MapCleanup.CleanupOverride("name:a", RetakeV4.Domain.MapCleanup.CleanupKind.Ignore, "") });
        store.Save("de_dust2", new[] { new RetakeV4.Domain.MapCleanup.CleanupOverride("name:b", RetakeV4.Domain.MapCleanup.CleanupKind.Vent, "") });
        Assert.Equal(new[] { "name:b" }, store.Load("de_dust2").Overrides.Select(o => o.Key));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "de_dust2.json.bak")));
    }

    [Fact]
    public void CorruptFile_ReportedAndKeptAside()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "de_dust2.json"), "{ broken");
        var (overrides, issues) = new CleanupOverrideStore(_dir.Path).Load("de_dust2");
        Assert.Empty(overrides);
        Assert.NotEmpty(issues);
        Assert.Single(Directory.GetFiles(_dir.Path, "de_dust2.json.*.invalid.bak"));
    }

    [Fact]
    public void UnsafeMapName_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new CleanupOverrideStore(_dir.Path).Load("../evil"));
    }
}
```

`ConfigExportTests` : ajouter `"mapcleanup.json"` à la liste attendue (ordre alphabétique). `LangFilesTests` : rien à ajouter (synchronisation en/fr déjà testée).

- [ ] **Step 2: Run** `dotnet test tests/RetakeV4.Integration.Tests --filter "MapCleanup"` — FAIL.

- [ ] **Step 3: Implementation**

```csharp
// MapCleanupConfig.cs
using RetakeV4.Configuration;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Modules.MapCleanup;

public sealed record MapCleanupConfig : ModuleConfig
{
    public MapCleanupConfig() => Version = 1;

    public bool OpenDoors { get; init; } = true;

    public int DoorOpenChancePercent { get; init; } = 100;

    public bool BreakWindows { get; init; } = true;

    public bool BreakVents { get; init; } = true;

    public int MaxEntitiesPerRound { get; init; } = 512;

    public bool FreezeEndCheck { get; init; } = true;

    public CleanupSettings ToSettings() => new(OpenDoors, DoorOpenChancePercent, BreakWindows, BreakVents, MaxEntitiesPerRound);
}
```

```csharp
// MapCleanupConfigValidator.cs
using RetakeV4.Configuration;

namespace RetakeV4.Modules.MapCleanup;

public sealed class MapCleanupConfigValidator : IConfigValidator<MapCleanupConfig>
{
    public ValidationResult<MapCleanupConfig> Validate(MapCleanupConfig config, MapCleanupConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var chance = config.DoorOpenChancePercent;
        if (chance is < 0 or > 100)
        {
            issues.Add(new ConfigIssue(file, nameof(config.DoorOpenChancePercent), $"must be 0 to 100; using {defaults.DoorOpenChancePercent}"));
            chance = defaults.DoorOpenChancePercent;
        }
        var max = config.MaxEntitiesPerRound;
        if (max is < 1 or > 4096)
        {
            issues.Add(new ConfigIssue(file, nameof(config.MaxEntitiesPerRound), $"must be 1 to 4096; using {defaults.MaxEntitiesPerRound}"));
            max = defaults.MaxEntitiesPerRound;
        }
        return new ValidationResult<MapCleanupConfig>(config with { DoorOpenChancePercent = chance, MaxEntitiesPerRound = max }, issues);
    }
}
```

```csharp
// CleanupOverrideStore.cs
using RetakeV4.Domain.MapCleanup;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Modules.MapCleanup;

// configs/plugins/RetakeV4/mapcleanup/<map>.json, written like the spawn files: temp file then move, previous version as .bak.
public sealed class CleanupOverrideStore
{
    private readonly string _directory;

    public CleanupOverrideStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public (IReadOnlyList<CleanupOverride> Overrides, IReadOnlyList<string> Issues) Load(string map)
    {
        var path = PathFor(map);
        if (!File.Exists(path))
        {
            return (Array.Empty<CleanupOverride>(), Array.Empty<string>());
        }
        var content = File.ReadAllText(path);
        var (overrides, issues, invalid) = CleanupOverridesFormat.Parse(content);
        if (!invalid)
        {
            return (overrides, issues);
        }
        var copy = $"{path}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.invalid.bak";
        File.WriteAllText(copy, content);
        return (Array.Empty<CleanupOverride>(), new[] { $"{Path.GetFileName(path)} is not valid JSON; kept as {Path.GetFileName(copy)}" });
    }

    public void Save(string map, IReadOnlyList<CleanupOverride> overrides)
    {
        var path = PathFor(map);
        Directory.CreateDirectory(_directory);
        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }
        var temp = path + ".tmp";
        File.WriteAllText(temp, CleanupOverridesFormat.Serialize(overrides));
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(string map) =>
        MapNames.IsSafe(map) ? Path.Combine(_directory, map + ".json") : throw new ArgumentException($"Unsafe map name '{map}'", nameof(map));
}
```

(Vérifier l'espace de noms réel de `MapNames` avec `grep -rn "class MapNames" src` et ajuster le `using`.)

Lang (`en.json` / `fr.json`, mêmes clés) :

| clé | fr | en |
|---|---|---|
| `admin.menu.cleanup` | Nettoyage de map | Map cleanup |
| `mapcleanup.editor.title` | Nettoyage de map | Map cleanup |
| `mapcleanup.editor.aimed` | Visée : {0} ({1}, {2}) | Aimed: {0} ({1}, {2}) |
| `mapcleanup.editor.none_aimed` | Vise une porte, une vitre ou une aération puis valide | Aim at a door, window or vent, then select |
| `mapcleanup.editor.detected` | Détection auto : {0} | Auto detection: {0} |
| `mapcleanup.kind.door` | Porte (ouvrir) | Door (open) |
| `mapcleanup.kind.window` | Vitre (casser) | Window (break) |
| `mapcleanup.kind.vent` | Aération (casser) | Vent (break) |
| `mapcleanup.kind.ignore` | Ne pas toucher | Do not touch |
| `mapcleanup.editor.auto` | Auto (retirer la correction) | Auto (remove the correction) |
| `mapcleanup.editor.test` | Tester ce round | Test this round |
| `mapcleanup.editor.save` | Sauvegarder | Save |
| `mapcleanup.editor.exit` | Quitter l'éditeur | Leave the editor |
| `mapcleanup.editor.exit_save` | Sauvegarder et quitter | Save and leave |
| `mapcleanup.editor.exit_discard` | Quitter sans sauvegarder | Leave without saving |
| `mapcleanup.editor.entered` | Éditeur de nettoyage : {0} correction(s) sur cette map. | Map cleanup editor: {0} correction(s) on this map. |
| `mapcleanup.editor.saved` | {0} correction(s) sauvegardée(s). | {0} correction(s) saved. |
| `mapcleanup.editor.save_failed` | Échec de la sauvegarde : {0} | Save failed: {0} |
| `mapcleanup.editor.tested` | Nettoyage rejoué : {0} entité(s). | Cleanup replayed: {0} entity(ies). |
| `mapcleanup.editor.busy` | Un autre admin édite déjà le nettoyage. | Another admin is already editing the cleanup. |
| `admin.usage` (modifier) | Usage : !retake [edit\|cleanup] | Usage: !retake [edit\|cleanup] |

- [ ] **Step 4: Run** — PASS (`ConfigExportTests` attendra `mapcleanup.json` une fois le module enregistré en tâche 5 : marquer ce test dans la tâche 5).
- [ ] **Step 5: Commit** `feat: config mapcleanup.json, magasin des corrections par map, textes`

### Task 5: Module MapCleanup (passe, vérification, éditeur, admin)

**Files:**
- Create: `src/RetakeV4/Modules/MapCleanup/MapCleanupModule.cs`, `CleanupEntities.cs`, `CleanupEditor.cs`
- Modify: `src/RetakeV4.Domain/Rounds/PreparationOrder.cs` (`Cleanup = 70`), `src/RetakeV4.Domain/Events/EditorEvents.cs` (`MapCleanupEditorRequested(PlayerId Player)`), `src/RetakeV4/Modules/ModuleCatalog.cs`, `src/RetakeV4/Modules/Admin/AdminModule.cs`, `tests/RetakeV4.Integration.Tests/Modules/ModuleCatalogTests.cs`, `tests/RetakeV4.Integration.Tests/Configuration/ConfigExportTests.cs`

**Interfaces:**
- Consumes : tâches 1–4 ; `ModuleContext`, `hooks.PreparationStep`, `hooks.OnEvent<EventRoundFreezeEnd>`, `hooks.OnBus<HudMenuSelected>`, `HudMenuOpen`, `HudMenuClose`, `SpawnEditorStateChanged`.
- Produces : module `MapCleanup` (dépend de `Core`, `Hud`).

- [ ] **Step 1: Tests (catalogue et export)**

`ModuleCatalogTests` : liste attendue `{ "Core", "Hud", "RoundTypes", "Teams", "Spawns", "Allocation", "Plant", "InstaDefuse", "Admin", "Links", "Announcements", "MapCleanup", "Api" }`. `ConfigExportTests` : `"mapcleanup.json"` dans la liste. Run : FAIL.

- [ ] **Step 2: `CleanupEntities` (lecture et actions CS2)**

```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Modules.MapCleanup;

// The only place that touches map entities: reads the five candidate classes, sends Open / Break, nothing else.
internal static class CleanupEntities
{
    public static IReadOnlyList<CleanupCandidate> Candidates()
    {
        var entities = CleanupClassifier.CandidateClasses
            .SelectMany(cls => Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(cls))
            .Where(e => e is { IsValid: true })
            .ToList();
        var names = entities
            .Select(e => e.Entity?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .GroupBy(n => n!)
            .ToDictionary(g => g.Key, g => g.Count());
        return entities.Select(e =>
        {
            var facts = Facts(e);
            var unique = facts.TargetName is { } n && names.GetValueOrDefault(n) == 1;
            return new CleanupCandidate((int)e.Index, facts, EntityKey.For(facts, unique));
        }).ToList();
    }

    public static EntityFacts Facts(CBaseEntity entity)
    {
        var origin = entity.AbsOrigin;
        var model = entity.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
        return new EntityFacts(entity.DesignerName, model, entity.Entity?.Name,
            origin is null ? new Vec3(0, 0, 0) : new Vec3(origin.X, origin.Y, origin.Z));
    }

    public static Vec3 Center(CBaseEntity entity) => Facts(entity).Origin;

    // Returns false when the entity is gone (already broken) or the input failed.
    public static bool Apply(CleanupTarget target)
    {
        var entity = Utilities.GetEntityFromIndex<CBaseEntity>(target.Handle);
        if (entity is not { IsValid: true })
        {
            return false;
        }
        entity.AcceptInput(target.Action == CleanupAction.Open ? "Open" : "Break");
        return true;
    }
}
```

(Vérifier par compilation : `CBaseEntity.Entity.Name` (targetname), `AbsOrigin`, `CBodyComponent.SceneNode.GetSkeletonInstance().ModelState.ModelName`, `Utilities.FindAllEntitiesByDesignerName<T>(string)`, `Utilities.GetEntityFromIndex<T>(int)`, `AcceptInput(string)`. Si un nom diffère dans 1.0.370, adapter à l'équivalent exposé ; ne jamais remplacer par `Remove`.)

- [ ] **Step 3: `MapCleanupModule`**

Comportement exact :
- `LoadConfig` : `store.Load("mapcleanup.json", new MapCleanupConfig(), new MapCleanupConfigValidator())`, `ConfigLogging.Report`.
- `Load` :
  - `_store = new CleanupOverrideStore(Path.Combine(<config dir>, "mapcleanup"))` où `<config dir>` = `Path.GetFullPath(Path.Combine(context.Plugin.ModuleDirectory, "..", "..", "configs", "plugins", "RetakeV4"))`.
  - `hooks.OnMapStart("map_start", LoadOverrides)` et chargement immédiat si `Server.MapName` n'est pas vide (hot reload) ; `LoadOverrides` logue chaque issue (`"Map cleanup {Map}: {Issue}"`) et ferme l'éditeur.
  - `hooks.PreparationStep(new DelegatePreparationStep("cleanup", PreparationOrder.Cleanup, ctx => { RunPass(); return ctx; }))`.
  - si `FreezeEndCheck` : `hooks.OnEvent<EventRoundFreezeEnd>("freeze_end", _ => Recheck())`.
  - `hooks.OnBus<SpawnEditorStateChanged>(e => _spawnEditing = e.Active)`.
  - `hooks.OnBus<MapCleanupEditorRequested>(e => _editor.Enter(e.Player))`, `hooks.OnBus<HudMenuSelected>(_editor.OnSelected)`, `hooks.OnEvent<EventPlayerDisconnect>` → `_editor.Leave(slot)`.
- `RunPass()` : rien si `_spawnEditing` ou `_editor.Active` ou `GameRulesAccessor.Get()?.WarmupPeriod == true`. Sinon `_lastTargets = CleanupPlan.Build(CleanupEntities.Candidates(), _overrides, _config.ToSettings(), SystemRandom.Shared)` puis `Apply` sur chaque cible dans un `try/catch` par entité ; les échecs sont comptés et une seule ligne `"Map cleanup: {Failed} of {Count} entities failed on {Map}"` est loguée par round s'il y en a. `Debug` : `"Map cleanup: {Doors} door(s), {Windows} window(s), {Vents} vent(s)"`.
- `Recheck()` : même garde ; renvoie `Apply` aux `_lastTargets` encore valides, sans relancer le tirage.
- `Unload` : `_editor.CloseAll()`.

- [ ] **Step 4: `CleanupEditor`**

- Une session à la fois (`int? _editorSlot`, `Dictionary<string, CleanupOverride> _working`, `bool _dirty`, `int? _aimed`). Second admin → `Text.Chat(player, "mapcleanup.editor.busy")`.
- `Enter` : copie des corrections chargées dans `_working`, message `mapcleanup.editor.entered` avec le nombre, `Pick` immédiat, publication `HudMenuOpen(player, CleanupEditorMenu.Build(view))`.
- `Pick` : `AimPicker.Pick(eye, forward, candidats)` avec `eye = pawn.AbsOrigin + (0,0,64)`, `forward = ViewGeometry.Forward(new ViewAngles(pawn.EyeAngles.X, pawn.EyeAngles.Y))`, candidats = `CleanupEntities.Candidates()` → `(Handle, Facts.Origin)` ; mémorise `_aimed` et sa clé.
- `OnSelected` (`MenuId == CleanupEditorMenu.MenuId` et slot éditeur) : `Parse` puis :
  - `Pick` → nouveau pick, rafraîchir ;
  - `Set(kind)` → `_working[key] = new CleanupOverride(key, kind, $"{class} {model}")`, `_dirty = true` ;
  - `Auto` → `_working.Remove(key)`, `_dirty = true` ;
  - `Test` → `RunPass` forcé avec `_working` (contourne la garde « éditeur actif ») et message `mapcleanup.editor.tested` ;
  - `Save` / `ExitSave` → `_store.Save(map, _working.Values.ToList())`, `_overrides = ToMap(_working.Values)`, message `saved` ou `save_failed` (exception `IOException`/`UnauthorizedAccessException`) ;
  - `Exit` est un sous-menu (pas d'action) ; `ExitDiscard`/`ExitSave` → `HudMenuClose`, fin de session.
  - après chaque commande hors sortie : `HudMenuOpen(... RefreshOnly: true)` avec la vue recalculée (`Detected = CleanupClassifier.Detect(facts)`, `Effective = Classify(facts, key, _working map)`, `HasOverride = _working.ContainsKey(key)`).
- `CloseAll` / changement de map : fin de session sans sauvegarde.

- [ ] **Step 5: Admin et enregistrement**

- `PreparationOrder.Cleanup = 70`.
- `EditorEvents.cs` : `public sealed record MapCleanupEditorRequested(PlayerId Player);`
- `AdminModule.Execute` : `case AdminAction.MapCleanupEditor: Context.Bus.Publish(new MapCleanupEditorRequested(id)); break;` ; `OnRetakeCommand` : `RetakeCommandKind.Cleanup` → `Execute(player, new AdminSelection(AdminAction.MapCleanupEditor))`.
- `ModuleCatalog` : `new MapCleanupModule(),` entre `AnnouncementsModule` et `ApiModule`.

- [ ] **Step 6: Build et tests**

Run : `dotnet build RetakeV4.sln -c Release && dotnet test RetakeV4.sln` — 0 warning, tout vert.

- [ ] **Step 7: Commit** `feat: module MapCleanup (passe par round, vérification de fin de freeze, éditeur en jeu, entrée admin)`

### Task 6: Version, docs, checklist

**Files:** `src/RetakeV4/RetakeV4.csproj`, `src/RetakeV4/RetakeV4Plugin.cs`, `README.md`, `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`

- [ ] **Step 1:** version 4.4.0 (csproj et `ModuleVersion`).
- [ ] **Step 2:** README : ligne `mapcleanup.json` dans le tableau de configuration ; section « Nettoyage de map » (comportement par défaut, catégories, `mapcleanup/<map>.json`, éditeur `!retake cleanup` / menu admin, ce qui n'est jamais touché) ; `!retake cleanup` dans les commandes admin.
- [ ] **Step 3:** checklist, section 4.4.0 :

```markdown
## 4.4.0 — nettoyage de map

- [ ] Mirage, Inferno, Nuke, Dust2 : au début de chaque round, portes ouvertes, vitres et aérations cassées ; caisses, barils, décor intacts ; aucune entité qui disparaît ou reste en l'air.
- [ ] `DoorOpenChancePercent` 0 : aucune porte ouverte, vitres et aérations toujours cassées.
- [ ] `!retake cleanup` (ou menu admin → Nettoyage de map) : le panneau montre l'entité visée (classe, modèle, nom) et sa catégorie détectée.
- [ ] Corriger une porte en « Ne pas toucher », Sauvegarder : elle reste fermée aux rounds suivants, et après redémarrage du serveur.
- [ ] « Auto » retire la correction ; « Tester ce round » rejoue la passe.
- [ ] Un second admin qui ouvre l'éditeur reçoit « déjà en cours ».
- [ ] Changement de map avec l'éditeur ouvert : il se ferme, aucune erreur dans les logs.
```

- [ ] **Step 4:** CLAUDE.md : « Nettoyage de map : décisions dans `Domain/MapCleanup` ; seul `CleanupEntities` touche les entités, entrées `Open`/`Break` uniquement ; corrections dans `configs/plugins/RetakeV4/mapcleanup/<map>.json`. »
- [ ] **Step 5:** `dotnet build` + `dotnet test` + couverture Domain ≥ 80 % ; commit `docs: nettoyage de map, checklist 4.4.0 ; version 4.4.0`.
