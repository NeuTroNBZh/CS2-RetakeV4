# RetakeV4 phase 4a — Éditeur de spawns en jeu et forçage de site — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permettre aux admins d'éditer les spawns en jeu (marqueurs visibles des seuls éditeurs, menu HUD, commandes console V3) avec pause du jeu, sauvegarde sûre, et de forcer le site des rounds.

**Architecture:** La logique (ensemble de spawns éditable, spawn le plus proche, sessions d'édition et pause, menu de l'éditeur, analyse des arguments) vit dans `RetakeV4.Domain/Spawns` et est testée. Le stockage (`SpawnFileStore`, `SpawnCatalog`) est sans CounterStrikeSharp et testé en intégration. Le module `Spawns` porte l'adaptateur `SpawnEditor` (marqueurs `CBeam` / `point_worldtext`, noclip, pause via warmup) et passe par le bus pour le menu HUD (`HudMenuOpen` / `HudMenuSelected`).

**Tech Stack:** C# / .NET 10, CounterStrikeSharp.API 1.0.370, xUnit 2.9.3, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-09-30-retake-v4-design.md` (sections 7.1-7.3, 12)

**Découpage de la phase 4 :** 4a (ce plan) = éditeur de spawns + forçage de site. 4b (plan suivant) = menu admin `!retake` (module Admin), pont SimpleAdmin, module Links. 4a publie déjà l'événement `SpawnEditorRequested` que le menu admin utilisera.

## Global Constraints

- CounterStrikeSharp.API 1.0.370, .NET 10, `dotnet build RetakeV4.sln -c Release` : 0 warning.
- `RetakeV4.Domain` sans CounterStrikeSharp, couverture de lignes ≥ 80 %.
- Modules sans référence croisée : bus, pipeline ou services du `ModuleContext`. Handlers via `context.Hooks`, `Server.NextFrame` via `_context?.Guard.Run`.
- Textes joueurs via `lang/*.json` (clés `module.section.key`, en + fr synchronisés), logs en anglais.
- Permission admin : `@retakev4/admin` pour l'éditeur, le forçage de site et toutes les commandes de spawns.
- Commandes console : `css_retake_addspawn`, `css_retake_delspawn`, `css_retake_tpspawn`, `css_retake_savespawns`, `css_retake_reloadspawns`, `css_retake_teleport`, `css_retake_forcesite <A|B|off> [sticky]`.
- Format de fichier de spawns V2 ; un fichier V3 sauvegardé garde une copie `<map>.json.v3.bak`.

## Décisions de conception (écarts assumés vs spec)

1. **Entrée de l'éditeur** : `css_retake_edit` (`!retake_edit`) et l'événement de bus `SpawnEditorRequested`. La commande `!retake edit` (avec espace) arrive en 4b avec le module Admin qui possède `!retake`.
2. **Numérotation** : les spawns sont numérotés à partir de 1 (`#01`), comme les étiquettes ; `css_retake_tpspawn <n>` suit cette numérotation (V3 comptait depuis 0).
3. **Ensemble partagé** : tous les éditeurs travaillent sur le même ensemble en mémoire. « Quitter sans sauvegarder » recharge le fichier (annule aussi les modifications non sauvegardées des autres éditeurs). Si le dernier éditeur se déconnecte avec des modifications non sauvegardées, le fichier est rechargé (les rounds ne jouent jamais un ensemble non sauvegardé après l'édition).
4. **Pause** : le premier éditeur lance `mp_warmup_start` + `mp_warmup_pausetimer 1` ; le dernier qui sort remet `mp_warmup_pausetimer 0` et, si le jeu n'était pas en warmup avant, `mp_warmup_end`. Le watchdog de warmup du Core est neutralisé (`Settle`) dès qu'un éditeur entre.
5. **Marqueurs** : recréés après chaque nettoyage de round (le `mp_warmup_start` provoque un restart) ; jamais créés entre `round_prestart` et la frame après `round_start`. Étiquettes orientées vers le joueur (`REORIENT_AROUND_UP`).
6. **Rotation des spawns** : la position de l'éditeur sert de position du spawn, l'angle garde seulement le yaw du regard (pitch et roll à 0).
7. **Forçage de site** : refusé si des spawns existent mais aucun pour ce site. `off` annule un forçage `Sticky` ou un `Once` pas encore consommé.
8. **Alerte admin** : si la map n'a aucun spawn, chaque admin connecté reçoit une alerte HUD à chaque round (pas seulement au chargement, pour les admins qui arrivent plus tard).

## Review Focus

1. Un argument d'équipe ou de site au format V3 (`2`/`3`, `0`/`1`) ou en minuscules → accepté ; un argument inconnu → refusé avec l'usage (Task 1 : `SpawnArgs_AcceptV3NumbersAndLetters`).
2. Une sauvegarde qui échoue en cours d'écriture → l'ancien fichier reste intact, pas de fichier `.tmp` laissé ni de spawns perdus (Task 3 : `Save_WritesThroughATempFile_AndKeepsABackup`).
3. Un nom de map dangereux (`../x`) → aucune lecture ni écriture hors du dossier des spawns (Task 3 : `UnsafeMapNames_AreRejected`).
4. Deux éditeurs : le premier qui sort ne relance pas le jeu ; le dernier restaure l'état d'avant (warmup ou non) (Task 2 : `LastEditorLeaving_RestoresTheGame`).
5. Quitter avec des modifications non sauvegardées → confirmation obligatoire (sous-menu « sauver et quitter / quitter sans sauver ») (Task 2 : `DirtySet_ExitAsksForConfirmation`).

---

## File Structure

```
src/RetakeV4.Domain/Spawns/SpawnSet.cs, SpawnArgs.cs                       (Task 1)
src/RetakeV4.Domain/Spawns/EditorState.cs, SpawnEditorMenu.cs              (Task 2)
src/RetakeV4.Domain/Rounds/WarmupTracker.cs                                (Task 2, Settle)
src/RetakeV4/Modules/Spawns/SpawnFileStore.cs, SpawnCatalog.cs             (Task 3)
src/RetakeV4.Domain/Events/EditorEvents.cs, Modules/Core/CoreModule.cs,
    lang/*.json                                                            (Task 4)
src/RetakeV4/Modules/Spawns/SpawnsModule.cs                                (Tasks 5, 7)
src/RetakeV4/Modules/Spawns/SpawnMarkers.cs, SpawnEditor.cs                (Task 6)
docs/CHECKLIST-INGAME.md, CLAUDE.md                                        (Task 8)
```

---

### Task 1: Ensemble de spawns éditable et arguments (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Spawns/SpawnSet.cs`, `src/RetakeV4.Domain/Spawns/SpawnArgs.cs`
- Test: `tests/RetakeV4.Domain.Tests/Spawns/SpawnSetTests.cs`, `tests/RetakeV4.Domain.Tests/Spawns/SpawnArgsTests.cs`

**Interfaces:**
- Consumes: `SpawnPoint`, `SiteForce`, `ForceSiteMode`, `Vec3`, `ViewAngles`.
- Produces:
  - `sealed record SpawnChange(TeamSide? Team = null, BombSite? Site = null, bool? CanPlant = null)`.
  - `sealed record SpawnSet(IReadOnlyList<SpawnPoint> Spawns, bool Dirty)` : `static Loaded(IReadOnlyList<SpawnPoint>)`, `Add(SpawnPoint)`, `Remove(Guid)`, `Update(Guid, SpawnChange)`, `MarkSaved()`, `Nearest(Vec3, float maxDistance)`, `IndexOf(Guid)` (0-based, -1 si absent), `Label(SpawnPoint)` (`[CT][A][C4] #07`), `Count(TeamSide)`.
  - `static SpawnArgs.Team(string?)` → `TeamSide?`, `SpawnArgs.Site(string?)` → `BombSite?`, `SpawnArgs.IsPlantFlag(string?)`.
  - `sealed record ForceSiteRequest(SiteForce? Force)` (null = annuler) et `static ForceSiteCommand.Parse(string? site, string? mode)` → `ForceSiteRequest?` (null = arguments invalides).

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Spawns/SpawnSetTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class SpawnSetTests
{
    private static SpawnPoint Spawn(TeamSide team, BombSite site, float x, bool canPlant = false) =>
        new(Guid.NewGuid(), team, site, canPlant, new Vec3(x, 0f, 0f), new ViewAngles(0f, 90f));

    [Fact]
    public void Loaded_IsClean_AndChangesMakeItDirty()
    {
        var a = Spawn(TeamSide.T, BombSite.A, 0f);
        var set = SpawnSet.Loaded(new[] { a });
        Assert.False(set.Dirty);
        Assert.True(set.Add(Spawn(TeamSide.CT, BombSite.B, 10f)).Dirty);
        Assert.True(set.Remove(a.Id).Dirty);
        Assert.True(set.Update(a.Id, new SpawnChange(CanPlant: true)).Dirty);
        Assert.False(set.Add(Spawn(TeamSide.CT, BombSite.B, 10f)).MarkSaved().Dirty);
    }

    [Fact]
    public void UnknownIds_LeaveTheSetUnchanged()
    {
        var set = SpawnSet.Loaded(new[] { Spawn(TeamSide.T, BombSite.A, 0f) });
        Assert.Same(set, set.Remove(Guid.NewGuid()));
        Assert.Same(set, set.Update(Guid.NewGuid(), new SpawnChange(Team: TeamSide.CT)));
    }

    [Fact]
    public void Update_ChangesOnlyTheGivenFields()
    {
        var a = Spawn(TeamSide.T, BombSite.A, 0f);
        var updated = Assert.Single(SpawnSet.Loaded(new[] { a }).Update(a.Id, new SpawnChange(Site: BombSite.B, CanPlant: true)).Spawns);
        Assert.Equal(a with { Site = BombSite.B, CanPlant = true }, updated);
    }

    [Fact]
    public void Nearest_IsTheClosestWithinTheDistance()
    {
        var near = Spawn(TeamSide.T, BombSite.A, 50f);
        var far = Spawn(TeamSide.CT, BombSite.A, 120f);
        var set = SpawnSet.Loaded(new[] { far, near });
        Assert.Equal(near, set.Nearest(new Vec3(40f, 0f, 0f), 150f));
        Assert.Null(set.Nearest(new Vec3(400f, 0f, 0f), 150f));
        Assert.Null(SpawnSet.Loaded(Array.Empty<SpawnPoint>()).Nearest(Vec3Zero(), 150f));
    }

    [Fact]
    public void Labels_AreNumberedFromOne_AndShowThePlantFlag()
    {
        var a = Spawn(TeamSide.T, BombSite.A, 0f, canPlant: true);
        var b = Spawn(TeamSide.CT, BombSite.B, 10f);
        var set = SpawnSet.Loaded(new[] { a, b });
        Assert.Equal("[T][A][C4] #01", set.Label(a));
        Assert.Equal("[CT][B] #02", set.Label(b));
        Assert.Equal(-1, set.IndexOf(Guid.NewGuid()));
        Assert.Equal(1, set.Count(TeamSide.CT));
    }

    private static Vec3 Vec3Zero() => new(0f, 0f, 0f);
}
```

`tests/RetakeV4.Domain.Tests/Spawns/SpawnArgsTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class SpawnArgsTests
{
    [Theory]
    [InlineData("T", TeamSide.T)]
    [InlineData("t", TeamSide.T)]
    [InlineData("2", TeamSide.T)]
    [InlineData("CT", TeamSide.CT)]
    [InlineData(" ct ", TeamSide.CT)]
    [InlineData("3", TeamSide.CT)]
    public void SpawnArgs_AcceptV3NumbersAndLetters(string value, TeamSide expected) => Assert.Equal(expected, SpawnArgs.Team(value));

    [Theory]
    [InlineData("A", BombSite.A)]
    [InlineData("0", BombSite.A)]
    [InlineData("b", BombSite.B)]
    [InlineData("1", BombSite.B)]
    public void Sites_AcceptV3NumbersAndLetters(string value, BombSite expected) => Assert.Equal(expected, SpawnArgs.Site(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("spec")]
    public void UnknownTeamsAndSites_AreRejected(string? value)
    {
        Assert.Null(SpawnArgs.Team(value));
        Assert.Null(SpawnArgs.Site(value));
    }

    [Theory]
    [InlineData("plant", true)]
    [InlineData("C4", true)]
    [InlineData(null, false)]
    [InlineData("no", false)]
    public void PlantFlag_IsOptional(string? value, bool expected) => Assert.Equal(expected, SpawnArgs.IsPlantFlag(value));

    [Fact]
    public void ForceSite_ParsesOnceStickyAndOff()
    {
        Assert.Equal(new SiteForce(BombSite.A, ForceSiteMode.Once), ForceSiteCommand.Parse("a", null)?.Force);
        Assert.Equal(new SiteForce(BombSite.B, ForceSiteMode.Sticky), ForceSiteCommand.Parse("B", "sticky")?.Force);
        Assert.Equal(new SiteForce(BombSite.B, ForceSiteMode.Once), ForceSiteCommand.Parse("1", "once")?.Force);
        var off = ForceSiteCommand.Parse("off", null);
        Assert.NotNull(off);
        Assert.Null(off.Force);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("C", null)]
    [InlineData("A", "forever")]
    public void ForceSite_RejectsInvalidArguments(string? site, string? mode) => Assert.Null(ForceSiteCommand.Parse(site, mode));
}
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`SpawnSet`, `SpawnArgs`, `ForceSiteCommand` introuvables).

- [ ] **Step 2: Implémentation**

`src/RetakeV4.Domain/Spawns/SpawnSet.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Spawns;

public sealed record SpawnChange(TeamSide? Team = null, BombSite? Site = null, bool? CanPlant = null);

// The spawns being played or edited. Dirty = changed since the last load or save.
public sealed record SpawnSet(IReadOnlyList<SpawnPoint> Spawns, bool Dirty)
{
    public static SpawnSet Loaded(IReadOnlyList<SpawnPoint> spawns) => new(spawns, false);

    public SpawnSet Add(SpawnPoint spawn) => new(Spawns.Append(spawn).ToList(), true);

    public SpawnSet Remove(Guid id) =>
        IndexOf(id) < 0 ? this : new SpawnSet(Spawns.Where(s => s.Id != id).ToList(), true);

    public SpawnSet Update(Guid id, SpawnChange change) =>
        IndexOf(id) < 0 ? this : new SpawnSet(Spawns.Select(s => s.Id == id ? Apply(s, change) : s).ToList(), true);

    public SpawnSet MarkSaved() => this with { Dirty = false };

    public SpawnPoint? Nearest(Vec3 position, float maxDistance) =>
        Spawns
            .Select(s => (Spawn: s, Distance: (s.Position - position).Length))
            .Where(c => c.Distance <= maxDistance)
            .OrderBy(c => c.Distance)
            .Select(c => c.Spawn)
            .FirstOrDefault();

    public int IndexOf(Guid id) => Spawns.Select(s => s.Id).ToList().IndexOf(id);

    public string Label(SpawnPoint spawn) =>
        $"[{spawn.Team}][{spawn.Site}]{(spawn.CanPlant ? "[C4]" : string.Empty)} #{IndexOf(spawn.Id) + 1:D2}";

    public int Count(TeamSide team) => Spawns.Count(s => s.Team == team);

    private static SpawnPoint Apply(SpawnPoint spawn, SpawnChange change) => spawn with
    {
        Team = change.Team ?? spawn.Team,
        Site = change.Site ?? spawn.Site,
        CanPlant = change.CanPlant ?? spawn.CanPlant,
    };
}
```

`src/RetakeV4.Domain/Spawns/SpawnArgs.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Spawns;

public static class SpawnArgs
{
    // V3 numeric forms (team 2/3, site 0/1) are still accepted.
    public static TeamSide? Team(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "T" or "2" => TeamSide.T,
        "CT" or "3" => TeamSide.CT,
        _ => null,
    };

    public static BombSite? Site(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "A" or "0" => BombSite.A,
        "B" or "1" => BombSite.B,
        _ => null,
    };

    public static bool IsPlantFlag(string? value) => value?.Trim().ToLowerInvariant() is "plant" or "c4" or "1" or "true";
}

// Force = null: cancel the current force.
public sealed record ForceSiteRequest(SiteForce? Force);

public static class ForceSiteCommand
{
    public static ForceSiteRequest? Parse(string? site, string? mode)
    {
        if (site?.Trim().ToLowerInvariant() is "off" or "none" or "clear")
        {
            return new ForceSiteRequest(null);
        }
        var parsedMode = mode?.Trim().ToLowerInvariant() switch
        {
            null or "" or "once" => ForceSiteMode.Once,
            "sticky" => ForceSiteMode.Sticky,
            _ => (ForceSiteMode?)null,
        };
        return SpawnArgs.Site(site) is { } parsed && parsedMode is { } forceMode
            ? new ForceSiteRequest(new SiteForce(parsed, forceMode))
            : null;
    }
}
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4.Domain/Spawns tests/RetakeV4.Domain.Tests/Spawns
git commit -m "feat: ensemble de spawns éditable, arguments des commandes et forçage de site (Domain)"
```

---

### Task 2: Sessions d'édition, menu de l'éditeur et watchdog (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Spawns/EditorState.cs`, `src/RetakeV4.Domain/Spawns/SpawnEditorMenu.cs`
- Modify: `src/RetakeV4.Domain/Rounds/WarmupTracker.cs`
- Test: `tests/RetakeV4.Domain.Tests/Spawns/EditorStateTests.cs`, `tests/RetakeV4.Domain.Tests/Spawns/SpawnEditorMenuTests.cs`, `tests/RetakeV4.Domain.Tests/Rounds/WarmupTrackerTests.cs`

**Interfaces:**
- Consumes: `SpawnSet`, `SpawnPoint` (Task 1) ; `Menu`, `MenuItem`, `MenuItemKind`, `HudText` (phase 3b).
- Produces:
  - `sealed record EditorState(ImmutableHashSet<int> Editors, ImmutableHashSet<int> Noclip, bool? WarmupBefore)` : `Idle`, `IsActive`, `IsEditing(int)`, `Enter(int slot, bool isWarmupNow)` → `(EditorState State, bool StartPause)`, `Leave(int slot)` → `(EditorState State, bool EndPause, bool EndWarmup)`, `ToggleNoclip(int slot)` → `(EditorState State, bool Enabled)`.
  - `WarmupTracker.Settle()`.
  - `sealed record SpawnEditorView(SpawnSet Set, SpawnPoint? Nearest, bool Noclip)`, `sealed record SpawnToAdd(TeamSide Team, BombSite Site, bool CanPlant)`.
  - `static class SpawnEditorMenu` : `MenuId = "spawns.editor"`, ids `SaveId`, `ReloadId`, `NoclipId`, `ExitId`, `ExitSaveId`, `ExitDiscardId`, `DeleteId`, `TeamId`, `SiteId`, `PlantId` ; `Build(SpawnEditorView)`, `AddItemId(SpawnToAdd)`, `ParseAdd(string)` → `SpawnToAdd?`, `TeleportItemId(Guid)`, `ParseTeleport(string)` → `Guid?`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Spawns/EditorStateTests.cs`
```csharp
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class EditorStateTests
{
    [Fact]
    public void FirstEditor_PausesTheGame_OthersDoNot()
    {
        var (first, pause) = EditorState.Idle.Enter(1, isWarmupNow: false);
        Assert.True(pause);
        Assert.True(first.IsActive);
        var (second, pauseAgain) = first.Enter(2, isWarmupNow: true);
        Assert.False(pauseAgain);
        Assert.Equal(false, second.WarmupBefore);
        var (same, pauseThird) = second.Enter(2, isWarmupNow: true);
        Assert.False(pauseThird);
        Assert.Equal(second.Editors, same.Editors);
    }

    [Fact]
    public void LastEditorLeaving_RestoresTheGame()
    {
        var (state, _) = EditorState.Idle.Enter(1, isWarmupNow: false);
        (state, _) = state.Enter(2, isWarmupNow: true);
        var (afterFirst, endPause, endWarmup) = state.Leave(1);
        Assert.False(endPause);
        Assert.False(endWarmup);
        var (afterLast, lastEndPause, lastEndWarmup) = afterFirst.Leave(2);
        Assert.True(lastEndPause);
        Assert.True(lastEndWarmup);
        Assert.False(afterLast.IsActive);
        Assert.Null(afterLast.WarmupBefore);
    }

    [Fact]
    public void EditingDuringWarmup_KeepsTheWarmupWhenLeaving()
    {
        var (state, _) = EditorState.Idle.Enter(1, isWarmupNow: true);
        var (_, endPause, endWarmup) = state.Leave(1);
        Assert.True(endPause);
        Assert.False(endWarmup);
    }

    [Fact]
    public void LeavingWithoutEditing_DoesNothing()
    {
        var (_, endPause, endWarmup) = EditorState.Idle.Leave(3);
        Assert.False(endPause);
        Assert.False(endWarmup);
    }

    [Fact]
    public void Noclip_IsOnlyForEditors_AndClearedWhenLeaving()
    {
        Assert.False(EditorState.Idle.ToggleNoclip(1).Enabled);
        var (state, _) = EditorState.Idle.Enter(1, isWarmupNow: false);
        var (on, enabled) = state.ToggleNoclip(1);
        Assert.True(enabled);
        Assert.False(on.ToggleNoclip(1).Enabled);
        Assert.DoesNotContain(1, on.Leave(1).State.Noclip);
    }
}
```

`tests/RetakeV4.Domain.Tests/Spawns/SpawnEditorMenuTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class SpawnEditorMenuTests
{
    private static readonly SpawnPoint A = new(Guid.NewGuid(), TeamSide.T, BombSite.A, true, new Vec3(0f, 0f, 0f), new ViewAngles(0f, 0f));

    private static IReadOnlyList<string> Ids(Menu menu) => menu.Items.Select(i => i.Id).ToList();

    [Fact]
    public void CleanSet_WithoutNearestSpawn()
    {
        var menu = SpawnEditorMenu.Build(new SpawnEditorView(SpawnSet.Loaded(new[] { A }), null, false));
        Assert.Equal(SpawnEditorMenu.MenuId, menu.Id);
        Assert.Equal(new[] { "add", "teleport", SpawnEditorMenu.SaveId, SpawnEditorMenu.ReloadId, SpawnEditorMenu.NoclipId, SpawnEditorMenu.ExitId }, Ids(menu));
        Assert.Equal(MenuItemKind.Action, menu.Items[^1].Kind);
    }

    [Fact]
    public void NearestSpawn_OffersItsEdits()
    {
        var menu = SpawnEditorMenu.Build(new SpawnEditorView(SpawnSet.Loaded(new[] { A }), A, true));
        var nearest = Assert.Single(menu.Items, i => i.Id == "nearest");
        Assert.Equal(new object[] { "[T][A][C4] #01" }, nearest.Label.Args);
        Assert.Equal(
            new[] { SpawnEditorMenu.DeleteId, SpawnEditorMenu.TeamId, SpawnEditorMenu.SiteId, SpawnEditorMenu.PlantId },
            nearest.Submenu!.Items.Select(i => i.Id));
        Assert.True(nearest.Submenu.Items[^1].IsOn);
        Assert.True(Assert.Single(menu.Items, i => i.Id == SpawnEditorMenu.NoclipId).IsOn);
    }

    [Fact]
    public void DirtySet_ExitAsksForConfirmation()
    {
        var dirty = SpawnSet.Loaded(Array.Empty<SpawnPoint>()).Add(A);
        var menu = SpawnEditorMenu.Build(new SpawnEditorView(dirty, null, false));
        var exit = menu.Items[^1];
        Assert.Equal(MenuItemKind.Submenu, exit.Kind);
        Assert.Equal(new[] { SpawnEditorMenu.ExitSaveId, SpawnEditorMenu.ExitDiscardId }, exit.Submenu!.Items.Select(i => i.Id));
        Assert.DoesNotContain(menu.Items, i => i.Id == SpawnEditorMenu.ExitId);
    }

    [Fact]
    public void EmptySet_HasNoTeleportEntry() =>
        Assert.DoesNotContain(
            SpawnEditorMenu.Build(new SpawnEditorView(SpawnSet.Loaded(Array.Empty<SpawnPoint>()), null, false)).Items,
            i => i.Id == "teleport");

    [Fact]
    public void AddChoices_RoundTrip()
    {
        var add = SpawnEditorMenu.Build(new SpawnEditorView(SpawnSet.Loaded(Array.Empty<SpawnPoint>()), null, false)).Items[0].Submenu!;
        var parsed = add.Items.Select(i => SpawnEditorMenu.ParseAdd(i.Id)).ToList();
        Assert.Equal(6, parsed.Count);
        Assert.Contains(new SpawnToAdd(TeamSide.T, BombSite.B, true), parsed);
        Assert.Contains(new SpawnToAdd(TeamSide.CT, BombSite.A, false), parsed);
        Assert.Null(SpawnEditorMenu.ParseAdd("add:X:A"));
        Assert.Null(SpawnEditorMenu.ParseAdd(SpawnEditorMenu.SaveId));
    }

    [Fact]
    public void TeleportEntries_CarryTheSpawnId()
    {
        var menu = SpawnEditorMenu.Build(new SpawnEditorView(SpawnSet.Loaded(new[] { A }), null, false));
        var entry = Assert.Single(menu.Items, i => i.Id == "teleport").Submenu!.Items[0];
        Assert.Equal(A.Id, SpawnEditorMenu.ParseTeleport(entry.Id));
        Assert.Equal("[T][A][C4] #01", entry.Label.Literal);
        Assert.Null(SpawnEditorMenu.ParseTeleport("tp:nope"));
    }
}
```

Ajouter à `tests/RetakeV4.Domain.Tests/Rounds/WarmupTrackerTests.cs` (dans la classe) :
```csharp
    [Fact]
    public void Settle_StopsTheWatchdog()
    {
        var tracker = WarmupTracker.Start(16f).Settle();
        var (_, forceEnd) = tracker.Evaluate(new WarmupSnapshot(true, 1f, 100f));
        Assert.False(forceEnd);
    }
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`EditorState`, `SpawnEditorMenu`, `Settle` introuvables).

- [ ] **Step 2: Implémentation**

`src/RetakeV4.Domain/Spawns/EditorState.cs`
```csharp
using System.Collections.Immutable;

namespace RetakeV4.Domain.Spawns;

// WarmupBefore: whether the game was already in warmup when the first editor paused it (restored when the last one leaves).
public sealed record EditorState(ImmutableHashSet<int> Editors, ImmutableHashSet<int> Noclip, bool? WarmupBefore)
{
    public static EditorState Idle { get; } = new(ImmutableHashSet<int>.Empty, ImmutableHashSet<int>.Empty, null);

    public bool IsActive => !Editors.IsEmpty;

    public bool IsEditing(int slot) => Editors.Contains(slot);

    public (EditorState State, bool StartPause) Enter(int slot, bool isWarmupNow)
    {
        if (Editors.Contains(slot))
        {
            return (this, false);
        }
        return IsActive
            ? (this with { Editors = Editors.Add(slot) }, false)
            : (this with { Editors = Editors.Add(slot), WarmupBefore = isWarmupNow }, true);
    }

    public (EditorState State, bool EndPause, bool EndWarmup) Leave(int slot)
    {
        if (!Editors.Contains(slot))
        {
            return (this, false, false);
        }
        var next = this with { Editors = Editors.Remove(slot), Noclip = Noclip.Remove(slot) };
        return next.IsActive
            ? (next, false, false)
            : (next with { WarmupBefore = null }, true, WarmupBefore == false);
    }

    public (EditorState State, bool Enabled) ToggleNoclip(int slot)
    {
        if (!Editors.Contains(slot))
        {
            return (this, false);
        }
        return Noclip.Contains(slot)
            ? (this with { Noclip = Noclip.Remove(slot) }, false)
            : (this with { Noclip = Noclip.Add(slot) }, true);
    }
}
```

`src/RetakeV4.Domain/Spawns/SpawnEditorMenu.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Spawns;

public sealed record SpawnEditorView(SpawnSet Set, SpawnPoint? Nearest, bool Noclip);

public sealed record SpawnToAdd(TeamSide Team, BombSite Site, bool CanPlant);

public static class SpawnEditorMenu
{
    public const string MenuId = "spawns.editor";
    public const string SaveId = "save";
    public const string ReloadId = "reload";
    public const string NoclipId = "noclip";
    public const string ExitId = "exit";
    public const string ExitSaveId = "exit:save";
    public const string ExitDiscardId = "exit:discard";
    public const string DeleteId = "nearest:delete";
    public const string TeamId = "nearest:team";
    public const string SiteId = "nearest:site";
    public const string PlantId = "nearest:plant";

    private const string AddPrefix = "add:";
    private const string TeleportPrefix = "tp:";
    private const string PlantSuffix = ":plant";

    private static readonly SpawnToAdd[] AddChoices =
    {
        new(TeamSide.T, BombSite.A, true),
        new(TeamSide.T, BombSite.A, false),
        new(TeamSide.T, BombSite.B, true),
        new(TeamSide.T, BombSite.B, false),
        new(TeamSide.CT, BombSite.A, false),
        new(TeamSide.CT, BombSite.B, false),
    };

    public static Menu Build(SpawnEditorView view)
    {
        var set = view.Set;
        var items = new List<MenuItem>
        {
            new("add", HudText.Of("spawns.editor.menu.add"), MenuItemKind.Submenu, Submenu: AddMenu()),
        };
        if (view.Nearest is { } nearest)
        {
            items.Add(new MenuItem(
                "nearest", HudText.Of("spawns.editor.menu.nearest", set.Label(nearest)), MenuItemKind.Submenu, Submenu: NearestMenu(set, nearest)));
        }
        if (set.Spawns.Count > 0)
        {
            items.Add(new MenuItem("teleport", HudText.Of("spawns.editor.menu.teleport"), MenuItemKind.Submenu, Submenu: TeleportMenu(set)));
        }
        items.Add(new MenuItem(SaveId, HudText.Of("spawns.editor.menu.save"), MenuItemKind.Action));
        items.Add(new MenuItem(ReloadId, HudText.Of("spawns.editor.menu.reload"), MenuItemKind.Action));
        items.Add(new MenuItem(NoclipId, HudText.Of("spawns.editor.menu.noclip"), MenuItemKind.Toggle, IsOn: view.Noclip));
        items.Add(set.Dirty
            ? new MenuItem("exit_confirm", HudText.Of("spawns.editor.menu.exit"), MenuItemKind.Submenu, Submenu: ExitMenu())
            : new MenuItem(ExitId, HudText.Of("spawns.editor.menu.exit"), MenuItemKind.Action));
        var title = HudText.Of("spawns.editor.menu.title", set.Spawns.Count, set.Dirty ? "*" : string.Empty);
        return new Menu(MenuId, title, items);
    }

    public static string AddItemId(SpawnToAdd spawn) =>
        $"{AddPrefix}{spawn.Team}:{spawn.Site}{(spawn.CanPlant ? PlantSuffix : string.Empty)}";

    public static SpawnToAdd? ParseAdd(string itemId) => AddChoices.FirstOrDefault(c => AddItemId(c) == itemId);

    public static string TeleportItemId(Guid spawnId) => $"{TeleportPrefix}{spawnId}";

    public static Guid? ParseTeleport(string itemId) =>
        itemId.StartsWith(TeleportPrefix, StringComparison.Ordinal) && Guid.TryParse(itemId[TeleportPrefix.Length..], out var id) ? id : null;

    private static Menu AddMenu() => new(
        "add",
        HudText.Of("spawns.editor.menu.add"),
        AddChoices
            .Select(c => new MenuItem(
                AddItemId(c), HudText.Raw($"{c.Team} - {c.Site}{(c.CanPlant ? " (C4)" : string.Empty)}"), MenuItemKind.Action))
            .ToList());

    private static Menu NearestMenu(SpawnSet set, SpawnPoint nearest)
    {
        var otherTeam = nearest.Team == TeamSide.T ? TeamSide.CT : TeamSide.T;
        var otherSite = nearest.Site == BombSite.A ? BombSite.B : BombSite.A;
        return new Menu("nearest", HudText.Raw(set.Label(nearest)), new[]
        {
            new MenuItem(DeleteId, HudText.Of("spawns.editor.menu.delete"), MenuItemKind.Action),
            new MenuItem(TeamId, HudText.Of("spawns.editor.menu.set_team", otherTeam.ToString()), MenuItemKind.Action),
            new MenuItem(SiteId, HudText.Of("spawns.editor.menu.set_site", otherSite.ToString()), MenuItemKind.Action),
            new MenuItem(PlantId, HudText.Of("spawns.editor.menu.can_plant"), MenuItemKind.Toggle, IsOn: nearest.CanPlant),
        });
    }

    private static Menu TeleportMenu(SpawnSet set) => new(
        "teleport",
        HudText.Of("spawns.editor.menu.teleport"),
        set.Spawns.Select(s => new MenuItem(TeleportItemId(s.Id), HudText.Raw(set.Label(s)), MenuItemKind.Action)).ToList());

    private static Menu ExitMenu() => new("exit_confirm", HudText.Of("spawns.editor.menu.exit"), new[]
    {
        new MenuItem(ExitSaveId, HudText.Of("spawns.editor.menu.exit_save"), MenuItemKind.Action),
        new MenuItem(ExitDiscardId, HudText.Of("spawns.editor.menu.exit_discard"), MenuItemKind.Action),
    });
}
```

Dans `src/RetakeV4.Domain/Rounds/WarmupTracker.cs`, ajouter après `Start` :
```csharp
    // An admin-driven warmup (spawn editor) must never be cut by the watchdog, even during the map's first warmup.
    public WarmupTracker Settle() => this with { Settled = true };
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4.Domain/Spawns src/RetakeV4.Domain/Rounds/WarmupTracker.cs tests/RetakeV4.Domain.Tests/Spawns tests/RetakeV4.Domain.Tests/Rounds/WarmupTrackerTests.cs
git commit -m "feat: sessions d'édition, menu de l'éditeur de spawns et arrêt du watchdog de warmup (Domain)"
```

---

### Task 3: Stockage des spawns (fichier et catalogue)

**Files:**
- Create: `src/RetakeV4/Modules/Spawns/SpawnFileStore.cs`, `src/RetakeV4/Modules/Spawns/SpawnCatalog.cs`
- Test: `tests/RetakeV4.Integration.Tests/Spawns/SpawnFileStoreTests.cs`, `tests/RetakeV4.Integration.Tests/Spawns/SpawnCatalogTests.cs`

**Interfaces:**
- Consumes: `SpawnFileFormat`, `MapNames`, `SpawnSet` (Task 1).
- Produces:
  - `sealed record SpawnLoad(IReadOnlyList<SpawnPoint> Spawns, bool Found, bool IsLegacy, IReadOnlyList<string> Issues)`.
  - `sealed class SpawnFileStore(string directory)` : `SpawnLoad Load(string map)`, `void Save(string map, IReadOnlyList<SpawnPoint> spawns)` (lève `ArgumentException` pour un nom de map dangereux).
  - `sealed class SpawnCatalog(SpawnFileStore store, ILogger logger)` : `MapName`, `Set`, `FileFound`, `Load(string map)`, `Replace(SpawnSet)`, `Reload()`, `string? Save()` (null = succès, sinon message).

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/Spawns/SpawnFileStoreTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;
using RetakeV4.Modules.Spawns;

namespace RetakeV4.Integration.Tests.Spawns;

public sealed class SpawnFileStoreTests : IDisposable
{
    private const string LegacyJson = """
        [{"SpawnId":"0b8d5c2e-3f0a-4b1e-9c55-111111111111","Team":2,"BombSite":0,"IsInBombZone":true,
          "PositionX":1,"PositionY":2,"PositionZ":3,"QAngleX":0,"QAngleY":90,"QAngleZ":0}]
        """;

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private SpawnFileStore Store() => new(_dir.Path);

    private static SpawnPoint Spawn(TeamSide team, BombSite site) =>
        new(Guid.NewGuid(), team, site, false, new Vec3(1f, 2f, 3f), new ViewAngles(0f, 90f));

    [Fact]
    public void Load_MissingFile_IsNotFound()
    {
        var loaded = Store().Load("de_mirage");
        Assert.False(loaded.Found);
        Assert.Empty(loaded.Spawns);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var spawns = new[] { Spawn(TeamSide.T, BombSite.A), Spawn(TeamSide.CT, BombSite.B) };
        Store().Save("de_mirage", spawns);
        var loaded = Store().Load("de_mirage");
        Assert.True(loaded.Found);
        Assert.False(loaded.IsLegacy);
        Assert.Equal(spawns, loaded.Spawns);
    }

    [Fact]
    public void Save_WritesThroughATempFile_AndKeepsABackup()
    {
        var first = new[] { Spawn(TeamSide.T, BombSite.A) };
        Store().Save("de_dust2", first);
        Store().Save("de_dust2", new[] { Spawn(TeamSide.CT, BombSite.B) });
        Assert.False(File.Exists(_dir.File("de_dust2.json.tmp")));
        Assert.Equal(first, SpawnFileFormat.Parse(File.ReadAllText(_dir.File("de_dust2.json.bak"))).Spawns);
    }

    [Fact]
    public void Save_OverALegacyFile_KeepsAV3Backup()
    {
        File.WriteAllText(_dir.File("de_nuke.json"), LegacyJson);
        Store().Save("de_nuke", Store().Load("de_nuke").Spawns);
        Assert.Equal(LegacyJson, File.ReadAllText(_dir.File("de_nuke.json.v3.bak")));
        var reloaded = Store().Load("de_nuke");
        Assert.False(reloaded.IsLegacy);
        Assert.True(Assert.Single(reloaded.Spawns).CanPlant);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("de/../x")]
    [InlineData("")]
    public void UnsafeMapNames_AreRejected(string map)
    {
        Assert.Throws<ArgumentException>(() => Store().Load(map));
        Assert.Throws<ArgumentException>(() => Store().Save(map, Array.Empty<SpawnPoint>()));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*", SearchOption.AllDirectories));
    }
}
```

`tests/RetakeV4.Integration.Tests/Spawns/SpawnCatalogTests.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;
using RetakeV4.Modules.Spawns;

namespace RetakeV4.Integration.Tests.Spawns;

public sealed class SpawnCatalogTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ListLogger _logger = new();

    public void Dispose() => _dir.Dispose();

    private SpawnCatalog Catalog() => new(new SpawnFileStore(_dir.Path), _logger);

    private static SpawnPoint Spawn() => new(Guid.NewGuid(), TeamSide.T, BombSite.A, true, new Vec3(1f, 2f, 3f), new ViewAngles(0f, 90f));

    [Fact]
    public void MissingFile_GivesAnEmptySet_AndAWarning()
    {
        var catalog = Catalog();
        catalog.Load("de_mirage");
        Assert.Empty(catalog.Set.Spawns);
        Assert.False(catalog.FileFound);
        Assert.Equal("de_mirage", catalog.MapName);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Save_ThenReload_KeepsTheEdits()
    {
        var catalog = Catalog();
        catalog.Load("de_mirage");
        var spawn = Spawn();
        catalog.Replace(catalog.Set.Add(spawn));
        Assert.True(catalog.Set.Dirty);
        Assert.Null(catalog.Save());
        Assert.False(catalog.Set.Dirty);
        Assert.True(catalog.FileFound);
        catalog.Reload();
        Assert.Equal(spawn, Assert.Single(catalog.Set.Spawns));
    }

    [Fact]
    public void Reload_DiscardsUnsavedEdits()
    {
        var catalog = Catalog();
        catalog.Load("de_mirage");
        catalog.Replace(catalog.Set.Add(Spawn()));
        catalog.Reload();
        Assert.Empty(catalog.Set.Spawns);
        Assert.False(catalog.Set.Dirty);
    }

    [Fact]
    public void Save_WithoutAMap_ReturnsAnError() => Assert.NotNull(Catalog().Save());

    [Fact]
    public void UnsafeMapName_IsNotRead()
    {
        var catalog = Catalog();
        catalog.Load("../secret");
        Assert.Empty(catalog.Set.Spawns);
        Assert.NotNull(catalog.Save());
    }
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`SpawnFileStore`, `SpawnCatalog` introuvables).

- [ ] **Step 2: Implémentation**

`src/RetakeV4/Modules/Spawns/SpawnFileStore.cs`
```csharp
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Modules.Spawns;

public sealed record SpawnLoad(IReadOnlyList<SpawnPoint> Spawns, bool Found, bool IsLegacy, IReadOnlyList<string> Issues);

public sealed class SpawnFileStore
{
    private readonly string _directory;

    public SpawnFileStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public SpawnLoad Load(string map)
    {
        var path = PathFor(map);
        if (!File.Exists(path))
        {
            return new SpawnLoad(Array.Empty<SpawnPoint>(), false, false, Array.Empty<string>());
        }
        var result = SpawnFileFormat.Parse(File.ReadAllText(path));
        return new SpawnLoad(result.Spawns, true, result.IsLegacyFormat, result.Issues);
    }

    // Written to a temp file then moved, so a failed write never truncates the spawn file. The previous version is kept as
    // <map>.json.bak, and the first V3 file overwritten is kept once as <map>.json.v3.bak.
    public void Save(string map, IReadOnlyList<SpawnPoint> spawns)
    {
        var path = PathFor(map);
        Directory.CreateDirectory(_directory);
        if (File.Exists(path))
        {
            var current = File.ReadAllText(path);
            var legacyBackup = path + ".v3.bak";
            if (!File.Exists(legacyBackup) && SpawnFileFormat.Parse(current).IsLegacyFormat)
            {
                File.WriteAllText(legacyBackup, current);
            }
            File.WriteAllText(path + ".bak", current);
        }
        var temp = path + ".tmp";
        File.WriteAllText(temp, SpawnFileFormat.Serialize(map, spawns));
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(string map)
    {
        if (!MapNames.IsSafe(map))
        {
            throw new ArgumentException($"Unsafe map name '{map}'", nameof(map));
        }
        return Path.Combine(_directory, map + ".json");
    }
}
```

`src/RetakeV4/Modules/Spawns/SpawnCatalog.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Modules.Spawns;

// The spawns of the current map: what rounds use and what the editor changes. Game thread only.
public sealed class SpawnCatalog
{
    private static readonly SpawnSet Empty = SpawnSet.Loaded(Array.Empty<SpawnPoint>());

    private readonly SpawnFileStore _store;
    private readonly ILogger _logger;

    public SpawnCatalog(SpawnFileStore store, ILogger logger)
    {
        _store = store;
        _logger = logger;
    }

    public string? MapName { get; private set; }

    public SpawnSet Set { get; private set; } = Empty;

    public bool FileFound { get; private set; }

    public void Load(string mapName)
    {
        MapName = mapName;
        Set = Empty;
        FileFound = false;
        if (!MapNames.IsSafe(mapName))
        {
            _logger.LogWarning("Map name {Map} is not a plain file name: default CS2 spawns will be used", mapName);
            return;
        }
        SpawnLoad loaded;
        try
        {
            loaded = _store.Load(mapName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the spawn file of {Map}: default CS2 spawns will be used", mapName);
            return;
        }
        if (!loaded.Found)
        {
            _logger.LogWarning("No spawn file for {Map}: default CS2 spawns will be used", mapName);
            return;
        }
        foreach (var issue in loaded.Issues)
        {
            _logger.LogWarning("Spawn file {Map}: {Issue}", mapName, issue);
        }
        Set = SpawnSet.Loaded(loaded.Spawns);
        FileFound = true;
        _logger.LogInformation("Loaded {Count} spawns for {Map} (legacy format: {Legacy})", loaded.Spawns.Count, mapName, loaded.IsLegacy);
    }

    public void Replace(SpawnSet set) => Set = set;

    public void Reload()
    {
        if (MapName is { } map)
        {
            Load(map);
        }
    }

    // Returns null on success, otherwise a short reason for the admin.
    public string? Save()
    {
        if (MapName is not { } map || !MapNames.IsSafe(map))
        {
            return "no map loaded";
        }
        try
        {
            _store.Save(map, Set.Spawns);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the spawns of {Map}", map);
            return ex.Message;
        }
        Set = Set.MarkSaved();
        FileFound = true;
        _logger.LogInformation("Saved {Count} spawns for {Map}", Set.Spawns.Count, map);
        return null;
    }
}
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Modules/Spawns/SpawnFileStore.cs src/RetakeV4/Modules/Spawns/SpawnCatalog.cs tests/RetakeV4.Integration.Tests/Spawns
git commit -m "feat: stockage des spawns (écriture atomique, sauvegardes .bak et .v3.bak) et catalogue de la map"
```

---

### Task 4: Événements de l'éditeur, watchdog du Core et textes

**Files:**
- Create: `src/RetakeV4.Domain/Events/EditorEvents.cs`
- Modify: `src/RetakeV4/Modules/Core/CoreModule.cs`, `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`
- Test: `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs`

**Interfaces:**
- Consumes: `WarmupTracker.Settle` (Task 2) ; `PlayerId`.
- Produces:
  - `sealed record SpawnEditorRequested(PlayerId Player)` — demande d'ouverture de l'éditeur (menu admin 4b).
  - `sealed record SpawnEditorStateChanged(bool Active)` — publié quand le premier éditeur entre et quand le dernier sort.
  - Clés lang `spawns.editor.*`, `spawns.editor.menu.*`, `spawns.forcesite.*`, `spawns.missing.admin`.

- [ ] **Step 1: Écrire le test qui échoue**

Ajouter à `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs` (dans la classe) :
```csharp
    [Theory]
    [InlineData("spawns.editor.entered")]
    [InlineData("spawns.editor.left")]
    [InlineData("spawns.editor.saved")]
    [InlineData("spawns.editor.save_failed")]
    [InlineData("spawns.editor.reloaded")]
    [InlineData("spawns.editor.added")]
    [InlineData("spawns.editor.deleted")]
    [InlineData("spawns.editor.updated")]
    [InlineData("spawns.editor.none_nearby")]
    [InlineData("spawns.editor.teleported")]
    [InlineData("spawns.editor.not_found")]
    [InlineData("spawns.editor.noclip_on")]
    [InlineData("spawns.editor.noclip_off")]
    [InlineData("spawns.editor.usage_add")]
    [InlineData("spawns.editor.usage_tp")]
    [InlineData("spawns.editor.usage_teleport")]
    [InlineData("spawns.editor.usage_edit")]
    [InlineData("spawns.editor.unsaved")]
    [InlineData("spawns.editor.player_only")]
    [InlineData("spawns.editor.no_permission")]
    [InlineData("spawns.editor.menu.title")]
    [InlineData("spawns.editor.menu.add")]
    [InlineData("spawns.editor.menu.nearest")]
    [InlineData("spawns.editor.menu.teleport")]
    [InlineData("spawns.editor.menu.save")]
    [InlineData("spawns.editor.menu.reload")]
    [InlineData("spawns.editor.menu.noclip")]
    [InlineData("spawns.editor.menu.exit")]
    [InlineData("spawns.editor.menu.exit_save")]
    [InlineData("spawns.editor.menu.exit_discard")]
    [InlineData("spawns.editor.menu.delete")]
    [InlineData("spawns.editor.menu.set_team")]
    [InlineData("spawns.editor.menu.set_site")]
    [InlineData("spawns.editor.menu.can_plant")]
    [InlineData("spawns.forcesite.set_once")]
    [InlineData("spawns.forcesite.set_sticky")]
    [InlineData("spawns.forcesite.cleared")]
    [InlineData("spawns.forcesite.usage")]
    [InlineData("spawns.forcesite.no_spawns")]
    [InlineData("spawns.missing.admin")]
    public void Phase4aKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: `Failed: 40` (`Phase4aKeys_ArePresent`).

- [ ] **Step 2: Implémentation**

`src/RetakeV4.Domain/Events/EditorEvents.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Events;

public sealed record SpawnEditorRequested(PlayerId Player);

// Active = true when the first editor enters (game paused in warmup), false when the last one leaves.
public sealed record SpawnEditorStateChanged(bool Active);
```

Dans `src/RetakeV4/Modules/Core/CoreModule.cs`, dans `Load`, à côté des autres `hooks.OnBus` :
```csharp
        hooks.OnBus<SpawnEditorStateChanged>(e =>
        {
            if (e.Active)
            {
                _warmup = _warmup.Settle();
            }
        });
```

Lang : ajouter à `src/RetakeV4/lang/en.json`
```json
  "spawns.editor.entered": "Spawn editor on ({0} spawns). Markers are only visible to editors; the game is paused.",
  "spawns.editor.left": "Spawn editor off.",
  "spawns.editor.saved": "{0} spawn(s) saved.",
  "spawns.editor.save_failed": "Saving failed: {0}",
  "spawns.editor.reloaded": "{0} spawn(s) reloaded from the file.",
  "spawns.editor.added": "Spawn {0} added.",
  "spawns.editor.deleted": "Spawn {0} deleted.",
  "spawns.editor.updated": "Spawn {0} updated.",
  "spawns.editor.none_nearby": "No spawn near you.",
  "spawns.editor.teleported": "Teleported to {0}.",
  "spawns.editor.not_found": "No spawn #{0}.",
  "spawns.editor.noclip_on": "Noclip on.",
  "spawns.editor.noclip_off": "Noclip off.",
  "spawns.editor.usage_add": "Usage: css_retake_addspawn <T|CT> <A|B> [plant]",
  "spawns.editor.usage_tp": "Usage: css_retake_tpspawn <number>",
  "spawns.editor.usage_teleport": "Usage: css_retake_teleport <x> <y> <z>",
  "spawns.editor.usage_edit": "Usage: css_retake_edit [save|discard|exit]",
  "spawns.editor.unsaved": "Unsaved changes: use css_retake_edit save or css_retake_edit discard.",
  "spawns.editor.player_only": "This command must be used in game.",
  "spawns.editor.no_permission": "You do not have permission to edit spawns.",
  "spawns.editor.menu.title": "Spawn editor ({0}){1}",
  "spawns.editor.menu.add": "Add a spawn here",
  "spawns.editor.menu.nearest": "Nearest: {0}",
  "spawns.editor.menu.teleport": "Teleport to a spawn",
  "spawns.editor.menu.save": "Save",
  "spawns.editor.menu.reload": "Reload from file",
  "spawns.editor.menu.noclip": "Noclip",
  "spawns.editor.menu.exit": "Leave the editor",
  "spawns.editor.menu.exit_save": "Save and leave",
  "spawns.editor.menu.exit_discard": "Leave without saving",
  "spawns.editor.menu.delete": "Delete",
  "spawns.editor.menu.set_team": "Change team to {0}",
  "spawns.editor.menu.set_site": "Change site to {0}",
  "spawns.editor.menu.can_plant": "Can plant",
  "spawns.forcesite.set_once": "Next round forced on site {0}.",
  "spawns.forcesite.set_sticky": "Rounds forced on site {0} until css_retake_forcesite off.",
  "spawns.forcesite.cleared": "Site forcing cancelled.",
  "spawns.forcesite.usage": "Usage: css_retake_forcesite <A|B|off> [once|sticky]",
  "spawns.forcesite.no_spawns": "No spawn on site {0}: forcing refused.",
  "spawns.missing.admin": "No spawns for {0}: default CS2 spawns are used (css_retake_edit)."
```
et à `src/RetakeV4/lang/fr.json`
```json
  "spawns.editor.entered": "Éditeur de spawns activé ({0} spawns). Marqueurs visibles des éditeurs seulement ; le jeu est en pause.",
  "spawns.editor.left": "Éditeur de spawns désactivé.",
  "spawns.editor.saved": "{0} spawn(s) sauvegardé(s).",
  "spawns.editor.save_failed": "Échec de la sauvegarde : {0}",
  "spawns.editor.reloaded": "{0} spawn(s) rechargé(s) depuis le fichier.",
  "spawns.editor.added": "Spawn {0} ajouté.",
  "spawns.editor.deleted": "Spawn {0} supprimé.",
  "spawns.editor.updated": "Spawn {0} modifié.",
  "spawns.editor.none_nearby": "Aucun spawn près de toi.",
  "spawns.editor.teleported": "Téléporté sur {0}.",
  "spawns.editor.not_found": "Pas de spawn n°{0}.",
  "spawns.editor.noclip_on": "Noclip activé.",
  "spawns.editor.noclip_off": "Noclip désactivé.",
  "spawns.editor.usage_add": "Usage : css_retake_addspawn <T|CT> <A|B> [plant]",
  "spawns.editor.usage_tp": "Usage : css_retake_tpspawn <numéro>",
  "spawns.editor.usage_teleport": "Usage : css_retake_teleport <x> <y> <z>",
  "spawns.editor.usage_edit": "Usage : css_retake_edit [save|discard|exit]",
  "spawns.editor.unsaved": "Modifications non sauvegardées : css_retake_edit save ou css_retake_edit discard.",
  "spawns.editor.player_only": "Cette commande s'utilise en jeu.",
  "spawns.editor.no_permission": "Tu n'as pas la permission d'éditer les spawns.",
  "spawns.editor.menu.title": "Éditeur de spawns ({0}){1}",
  "spawns.editor.menu.add": "Ajouter un spawn ici",
  "spawns.editor.menu.nearest": "Le plus proche : {0}",
  "spawns.editor.menu.teleport": "Aller à un spawn",
  "spawns.editor.menu.save": "Sauvegarder",
  "spawns.editor.menu.reload": "Recharger le fichier",
  "spawns.editor.menu.noclip": "Noclip",
  "spawns.editor.menu.exit": "Quitter l'éditeur",
  "spawns.editor.menu.exit_save": "Sauvegarder et quitter",
  "spawns.editor.menu.exit_discard": "Quitter sans sauvegarder",
  "spawns.editor.menu.delete": "Supprimer",
  "spawns.editor.menu.set_team": "Passer en {0}",
  "spawns.editor.menu.set_site": "Passer sur le site {0}",
  "spawns.editor.menu.can_plant": "Peut poser la bombe",
  "spawns.forcesite.set_once": "Prochain round forcé sur le site {0}.",
  "spawns.forcesite.set_sticky": "Rounds forcés sur le site {0} jusqu'à css_retake_forcesite off.",
  "spawns.forcesite.cleared": "Forçage du site annulé.",
  "spawns.forcesite.usage": "Usage : css_retake_forcesite <A|B|off> [once|sticky]",
  "spawns.forcesite.no_spawns": "Aucun spawn sur le site {0} : forçage refusé.",
  "spawns.missing.admin": "Aucun spawn pour {0} : spawns CS2 par défaut utilisés (css_retake_edit)."
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4.Domain/Events/EditorEvents.cs src/RetakeV4/Modules/Core/CoreModule.cs src/RetakeV4/lang tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs
git commit -m "feat: événements de l'éditeur de spawns, watchdog neutralisé pendant l'édition et textes"
```

---

### Task 5: Module Spawns — catalogue, forçage de site et alerte admin

**Files:**
- Modify: `src/RetakeV4/Modules/Spawns/SpawnsModule.cs`

**Interfaces:**
- Consumes: `SpawnCatalog`, `SpawnFileStore` (Task 3) ; `ForceSiteCommand`, `SiteForce` (Task 1) ; `HudAlert`, `HudText` (phase 3b).
- Produces:
  - `SpawnsModule` lit les spawns via `_catalog` (champ `SpawnCatalog`), garde `_force` (`SiteForce?`) consommé par `SiteSelector.Choose`.
  - Commande `css_retake_forcesite <A|B|off> [once|sticky]` (`@retakev4/admin`, console autorisée).
  - Helpers privés `IsAdmin(CCSPlayerController?)` (console = admin), `Reply(CCSPlayerController?, string key, params object[] args)` — réutilisés en Task 7.

Code adaptateur (CounterStrikeSharp) : vérifié par build, tests existants et checklist en jeu.

- [ ] **Step 1: Catalogue et forçage**

Dans `src/RetakeV4/Modules/Spawns/SpawnsModule.cs` :
- ajouter les `using` `CounterStrikeSharp.API`, `CounterStrikeSharp.API.Modules.Admin`, `CounterStrikeSharp.API.Modules.Commands`, `RetakeV4.Domain.Hud` ;
- remplacer le champ `private IReadOnlyList<SpawnPoint> _spawns = Array.Empty<SpawnPoint>();` par :
```csharp
    private const string AdminFlag = "@retakev4/admin";

    private SpawnCatalog? _catalog;
    private SiteForce? _force;
```
- ajouter la propriété :
```csharp
    private SpawnCatalog Catalog => _catalog ?? throw new InvalidOperationException("Spawns module is not loaded");
```
- dans `Load`, après `_context = context;` :
```csharp
        _catalog = new SpawnCatalog(new SpawnFileStore(Path.Combine(context.Plugin.ModuleDirectory, "spawns")), context.Logger);
```
  remplacer `hooks.OnBus<MapStarted>(e => LoadSpawns(e.MapName));` par :
```csharp
        hooks.OnBus<MapStarted>(e => LoadMap(e.MapName));
        hooks.Command("css_retake_forcesite", "Forces the bombsite: css_retake_forcesite <A|B|off> [once|sticky]", OnForceSite);
```
  et remplacer `hooks.OnBus<RoundPrepared>(Announce);` par :
```csharp
        hooks.OnBus<RoundPrepared>(e =>
        {
            Announce(e);
            WarnAdminsIfNoSpawns();
        });
```
- remplacer `Unload` par `public void Unload() { _catalog = null; _context = null; }` (sur plusieurs lignes, style du dépôt) ;
- remplacer la méthode `LoadSpawns` par :
```csharp
    private void LoadMap(string mapName)
    {
        _history = SiteHistory.Empty;
        _force = null;
        Catalog.Load(mapName);
    }
```
- dans `ChooseSite`, remplacer le corps par :
```csharp
        var available = Catalog.Set.Spawns.Select(s => s.Site).Distinct().ToList();
        var decision = SiteSelector.Choose(_history, _force, _config.MaxSameSiteInRow, available, _random);
        _history = decision.History;
        _force = decision.Force;
        return context with { Site = decision.Site };
```
- dans `PlacePlayers`, remplacer `_spawns` par `Catalog.Set.Spawns` (deux occurrences) ;
- ajouter les méthodes :
```csharp
    private void OnForceSite(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsAdmin(player))
        {
            Reply(player, "spawns.editor.no_permission");
            return;
        }
        if (ForceSiteCommand.Parse(command.GetArg(1), command.ArgCount > 2 ? command.GetArg(2) : null) is not { } request)
        {
            Reply(player, "spawns.forcesite.usage");
            return;
        }
        if (request.Force is not { } force)
        {
            _force = null;
            Reply(player, "spawns.forcesite.cleared");
            return;
        }
        var spawns = Catalog.Set.Spawns;
        if (spawns.Count > 0 && spawns.All(s => s.Site != force.Site))
        {
            Reply(player, "spawns.forcesite.no_spawns", force.Site.ToString());
            return;
        }
        _force = force;
        Reply(player, force.Mode == ForceSiteMode.Sticky ? "spawns.forcesite.set_sticky" : "spawns.forcesite.set_once", force.Site.ToString());
    }

    private void WarnAdminsIfNoSpawns()
    {
        if (Catalog.Set.Spawns.Count > 0 || Catalog.MapName is not { } map)
        {
            return;
        }
        foreach (var admin in PlayerQueries.Humans().Where(p => AdminManager.PlayerHasPermissions(p, AdminFlag)))
        {
            Context.Bus.Publish(new HudAlert(new PlayerId(admin.Slot), HudText.Of("spawns.missing.admin", map)));
        }
    }

    // The server console is always allowed.
    private static bool IsAdmin(CCSPlayerController? player) =>
        player is null || (player.IsValid && AdminManager.PlayerHasPermissions(player, AdminFlag));

    private void Reply(CCSPlayerController? player, string key, params object[] args)
    {
        if (player is { IsValid: true })
        {
            Context.Text.Chat(player, key, args);
            return;
        }
        Server.PrintToConsole(Context.Text.Server(key, args));
    }
```

- [ ] **Step 2: Vérifier**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`. `grep -n "_spawns" src/RetakeV4/Modules/Spawns/SpawnsModule.cs` ne renvoie rien.

- [ ] **Step 3: Commit**

```bash
git add src/RetakeV4/Modules/Spawns/SpawnsModule.cs
git commit -m "feat: forçage du site (css_retake_forcesite) et alerte HUD des admins sans spawns"
```

---

### Task 6: Adaptateur de l'éditeur (marqueurs, pause, noclip, menu)

**Files:**
- Create: `src/RetakeV4/Modules/Spawns/SpawnMarkers.cs`, `src/RetakeV4/Modules/Spawns/SpawnEditor.cs`

**Interfaces:**
- Consumes: `SpawnSet`, `SpawnChange`, `EditorState`, `SpawnEditorMenu`, `SpawnEditorView`, `SpawnToAdd` (Tasks 1-2) ; `SpawnCatalog` (Task 3) ; `SpawnEditorStateChanged` (Task 4) ; `HudMenuOpen`, `HudMenuClose`, `HudMenuSelected` (phase 3b) ; `ModuleContext`.
- Produces:
  - `internal sealed class SpawnMarkers` : `Rebuild(SpawnSet)`, `ShowRing(int slot, SpawnPoint?)`, `ClearRing(int slot)`, `Clear()`, `Hide(CCheckTransmitInfo info, int viewerSlot, bool viewerIsEditor)`.
  - `internal sealed class SpawnEditor(ModuleContext context, string moduleName, SpawnCatalog catalog)` : `IsEditing(int)`, `Enter(CCSPlayerController)`, `Leave(CCSPlayerController?, int slot)`, `HandleEditCommand(CCSPlayerController, string? arg)`, `AddHere(...)`, `DeleteNearest(...)`, `TeleportToNumber(...)`, `TeleportToPosition(...)`, `Save(CCSPlayerController?)`, `Reload(CCSPlayerController?)`, `OnNoclipCommand(CCSPlayerController?)` → `HookResult`, `OnSelected(HudMenuSelected)`, `Tick()`, `OnCheckTransmit(CCheckTransmitInfoList)`, `SuspendEntities()`, `ResumeEntities()`, `Reset()`, `OnDisconnect(int slot)`, `Shutdown()`.
  - Retour joueur via `Notify(player, key, args)` : chat du joueur, ou console serveur si `player` est null.

Code adaptateur : vérifié par build et checklist en jeu ; la logique est testée aux Tasks 1-3.

- [ ] **Step 1: Marqueurs**

`src/RetakeV4/Modules/Spawns/SpawnMarkers.cs`
```csharp
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Spawns;
using SpawnPoint = RetakeV4.Domain.Spawns.SpawnPoint;

namespace RetakeV4.Modules.Spawns;

// Editor-only markers: one pillar and one label per spawn (site B pillars are translucent), plus a ring around each editor's nearest spawn.
internal sealed class SpawnMarkers
{
    private const float PillarHeight = 72f;
    private const float LabelOffset = 20f;
    private const float RingRadius = 30f;
    private const float RingHeight = 5f;
    private const int RingSegments = 8;

    private static readonly Color TColor = Color.FromArgb(255, 255, 60, 60);
    private static readonly Color CtColor = Color.FromArgb(255, 60, 130, 255);
    private static readonly Color RingColor = Color.FromArgb(255, 255, 255, 0);

    private readonly List<CBaseEntity> _shared = new();
    private readonly Dictionary<int, (Guid Spawn, List<CBeam> Beams)> _rings = new();

    public void Rebuild(SpawnSet set)
    {
        Remove(_shared);
        foreach (var spawn in set.Spawns)
        {
            AddIfCreated(Pillar(spawn));
            AddIfCreated(Label(set, spawn));
        }
    }

    public void ShowRing(int slot, SpawnPoint? nearest)
    {
        if (_rings.TryGetValue(slot, out var ring) && ring.Spawn == nearest?.Id)
        {
            return;
        }
        ClearRing(slot);
        if (nearest is null)
        {
            return;
        }
        var beams = Enumerable.Range(0, RingSegments).Select(i => RingSegment(nearest, i)).OfType<CBeam>().ToList();
        _rings[slot] = (nearest.Id, beams);
    }

    public void ClearRing(int slot)
    {
        if (_rings.Remove(slot, out var ring))
        {
            Remove(ring.Beams);
        }
    }

    public void Clear()
    {
        Remove(_shared);
        foreach (var slot in _rings.Keys.ToList())
        {
            ClearRing(slot);
        }
    }

    // Players outside the editor see nothing; an editor only sees his own ring.
    public void Hide(CCheckTransmitInfo info, int viewerSlot, bool viewerIsEditor)
    {
        if (!viewerIsEditor)
        {
            Hide(info, _shared);
        }
        foreach (var (slot, ring) in _rings)
        {
            if (!viewerIsEditor || slot != viewerSlot)
            {
                Hide(info, ring.Beams);
            }
        }
    }

    private static void Hide(CCheckTransmitInfo info, IEnumerable<CBaseEntity> entities)
    {
        foreach (var entity in entities.Where(e => e.IsValid))
        {
            info.TransmitEntities.Remove(entity);
        }
    }

    private void AddIfCreated(CBaseEntity? entity)
    {
        if (entity is not null)
        {
            _shared.Add(entity);
        }
    }

    private static CBeam? Pillar(SpawnPoint spawn)
    {
        var color = ColorOf(spawn.Team);
        var p = spawn.Position;
        return Beam(
            new Vector(p.X, p.Y, p.Z),
            new Vector(p.X, p.Y, p.Z + PillarHeight),
            spawn.Site == BombSite.B ? Color.FromArgb(120, color.R, color.G, color.B) : color,
            3f);
    }

    private static CPointWorldText? Label(SpawnSet set, SpawnPoint spawn)
    {
        var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (text is not { IsValid: true })
        {
            return null;
        }
        text.MessageText = set.Label(spawn);
        text.Enabled = true;
        text.FontSize = 80f;
        text.WorldUnitsPerPx = 0.25f;
        text.Fullbright = true;
        text.Color = ColorOf(spawn.Team);
        text.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        text.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
        text.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
        var p = spawn.Position;
        text.Teleport(new Vector(p.X, p.Y, p.Z + PillarHeight + LabelOffset), new QAngle(0f, 0f, 0f), new Vector(0f, 0f, 0f));
        text.DispatchSpawn();
        return text;
    }

    private static CBeam? RingSegment(SpawnPoint spawn, int index)
    {
        var from = 2 * Math.PI * index / RingSegments;
        var to = 2 * Math.PI * (index + 1) / RingSegments;
        var p = spawn.Position;
        return Beam(
            new Vector(p.X + RingRadius * (float)Math.Cos(from), p.Y + RingRadius * (float)Math.Sin(from), p.Z + RingHeight),
            new Vector(p.X + RingRadius * (float)Math.Cos(to), p.Y + RingRadius * (float)Math.Sin(to), p.Z + RingHeight),
            RingColor,
            2f);
    }

    private static CBeam? Beam(Vector start, Vector end, Color color, float width)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is not { IsValid: true })
        {
            return null;
        }
        beam.Render = color;
        beam.Width = width;
        beam.EndWidth = width;
        beam.Amplitude = 0f;
        beam.Speed = 0f;
        beam.Teleport(start, new QAngle(0f, 0f, 0f), new Vector(0f, 0f, 0f));
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        beam.DispatchSpawn();
        return beam;
    }

    private static Color ColorOf(TeamSide team) => team == TeamSide.T ? TColor : CtColor;

    private static void Remove<T>(List<T> entities) where T : CBaseEntity
    {
        foreach (var entity in entities.Where(e => e.IsValid))
        {
            entity.Remove();
        }
        entities.Clear();
    }
}
```

- [ ] **Step 2: Éditeur**

`src/RetakeV4/Modules/Spawns/SpawnEditor.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;
using SpawnPoint = RetakeV4.Domain.Spawns.SpawnPoint;

namespace RetakeV4.Modules.Spawns;

// Game thread only. All editors share the catalog's set; markers are rebuilt after every round cleanup.
internal sealed class SpawnEditor
{
    private const float NearestDistance = 150f;
    private const int NearestRefreshTicks = 8;

    private readonly ModuleContext _context;
    private readonly SpawnCatalog _catalog;
    private readonly SpawnMarkers _markers = new();
    private readonly Dictionary<int, Guid?> _nearest = new();
    private EditorState _state = EditorState.Idle;
    private bool _entitiesAllowed = true;
    private int _tick;

    public SpawnEditor(ModuleContext context, SpawnCatalog catalog)
    {
        _context = context;
        _catalog = catalog;
    }

    public bool IsEditing(int slot) => _state.IsEditing(slot);

    public void Enter(CCSPlayerController player)
    {
        var wasEditing = _state.IsEditing(player.Slot);
        var (state, startPause) = _state.Enter(player.Slot, GameRulesAccessor.IsWarmup());
        _state = state;
        if (startPause)
        {
            Server.ExecuteCommand("mp_warmup_start");
            Server.ExecuteCommand("mp_warmup_pausetimer 1");
            _context.Bus.Publish(new SpawnEditorStateChanged(true));
            RefreshMarkers();
        }
        if (!wasEditing)
        {
            Notify(player, "spawns.editor.entered", _catalog.Set.Spawns.Count);
        }
        OpenMenu(player, refreshOnly: false);
    }

    public void Leave(CCSPlayerController? player, int slot)
    {
        if (!_state.IsEditing(slot))
        {
            return;
        }
        var (state, endPause, endWarmup) = _state.Leave(slot);
        _state = state;
        _nearest.Remove(slot);
        _markers.ClearRing(slot);
        if (player is { IsValid: true })
        {
            SetNoclip(player, false);
            _context.Bus.Publish(new HudMenuClose(new PlayerId(slot), SpawnEditorMenu.MenuId));
            Notify(player, "spawns.editor.left");
        }
        if (!endPause)
        {
            return;
        }
        _markers.Clear();
        Server.ExecuteCommand("mp_warmup_pausetimer 0");
        if (endWarmup)
        {
            Server.ExecuteCommand("mp_warmup_end");
        }
        _context.Bus.Publish(new SpawnEditorStateChanged(false));
    }

    // css_retake_edit [save|discard|exit]: no argument enters (or reopens the menu).
    public void HandleEditCommand(CCSPlayerController player, string? argument)
    {
        switch (argument?.Trim().ToLowerInvariant())
        {
            case null or "":
                Enter(player);
                break;
            case "save":
                if (Save(player))
                {
                    Leave(player, player.Slot);
                }
                break;
            case "discard":
                Discard(player);
                break;
            case "exit" when _catalog.Set.Dirty:
                Notify(player, "spawns.editor.unsaved");
                break;
            case "exit":
                Leave(player, player.Slot);
                break;
            default:
                Notify(player, "spawns.editor.usage_edit");
                break;
        }
    }

    public void AddHere(CCSPlayerController player, SpawnToAdd add)
    {
        if (Pose(player) is not { } pose)
        {
            return;
        }
        var spawn = new SpawnPoint(Guid.NewGuid(), add.Team, add.Site, add.CanPlant, pose.Origin, pose.Angles);
        Apply(_catalog.Set.Add(spawn));
        Notify(player, "spawns.editor.added", _catalog.Set.Label(spawn));
    }

    public void DeleteNearest(CCSPlayerController player)
    {
        if (NearestTo(player) is not { } nearest)
        {
            Notify(player, "spawns.editor.none_nearby");
            return;
        }
        var label = _catalog.Set.Label(nearest);
        Apply(_catalog.Set.Remove(nearest.Id));
        Notify(player, "spawns.editor.deleted", label);
    }

    public void TeleportToNumber(CCSPlayerController player, int number)
    {
        var spawns = _catalog.Set.Spawns;
        if (number < 1 || number > spawns.Count)
        {
            Notify(player, "spawns.editor.not_found", number);
            return;
        }
        TeleportTo(player, spawns[number - 1]);
    }

    public void TeleportToPosition(CCSPlayerController player, Vec3 position)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            return;
        }
        pawn.Teleport(new Vector(position.X, position.Y, position.Z), pawn.AbsRotation ?? new QAngle(0f, 0f, 0f), new Vector(0f, 0f, 0f));
    }

    public bool Save(CCSPlayerController? player)
    {
        if (_catalog.Save() is { } error)
        {
            Notify(player, "spawns.editor.save_failed", error);
            return false;
        }
        Notify(player, "spawns.editor.saved", _catalog.Set.Spawns.Count);
        RefreshMenus();
        return true;
    }

    public void Reload(CCSPlayerController? player)
    {
        _catalog.Reload();
        RefreshMarkers();
        Notify(player, "spawns.editor.reloaded", _catalog.Set.Spawns.Count);
    }

    public HookResult OnNoclipCommand(CCSPlayerController? player)
    {
        if (player is not { IsValid: true } || !_state.IsEditing(player.Slot))
        {
            return HookResult.Continue;
        }
        ToggleNoclip(player);
        return HookResult.Handled;
    }

    public void OnSelected(HudMenuSelected e)
    {
        if (e.MenuId != SpawnEditorMenu.MenuId || !_state.IsEditing(e.Player.Slot))
        {
            return;
        }
        var player = Utilities.GetPlayerFromSlot(e.Player.Slot);
        if (player is not { IsValid: true })
        {
            return;
        }
        Handle(player, e.ItemId);
        if (_state.IsEditing(player.Slot))
        {
            OpenMenu(player, refreshOnly: true);
        }
    }

    public void Tick()
    {
        if (!_state.IsActive || ++_tick % NearestRefreshTicks != 0)
        {
            return;
        }
        foreach (var slot in _state.Editors)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true })
            {
                continue;
            }
            var nearest = NearestTo(player);
            if (_entitiesAllowed)
            {
                _markers.ShowRing(slot, nearest);
            }
            if (_nearest.GetValueOrDefault(slot) != nearest?.Id)
            {
                _nearest[slot] = nearest?.Id;
                OpenMenu(player, refreshOnly: true);
            }
        }
    }

    public void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (!_state.IsActive)
        {
            return;
        }
        foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
        {
            if (viewer is not null)
            {
                _markers.Hide(info, viewer.Slot, _state.IsEditing(viewer.Slot));
            }
        }
    }

    public void SuspendEntities()
    {
        _entitiesAllowed = false;
        _markers.Clear();
        _nearest.Clear();
    }

    // After a round cleanup (the editor's own mp_warmup_start restarts the round): markers and noclip come back.
    public void ResumeEntities()
    {
        _entitiesAllowed = true;
        RefreshMarkers();
        foreach (var slot in _state.Noclip)
        {
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player)
            {
                SetNoclip(player, true);
            }
        }
    }

    // New map: entities are gone and the previous map's pause no longer applies.
    public void Reset()
    {
        var wasActive = _state.IsActive;
        _markers.Clear();
        _nearest.Clear();
        _state = EditorState.Idle;
        _entitiesAllowed = true;
        if (wasActive)
        {
            _context.Bus.Publish(new SpawnEditorStateChanged(false));
        }
    }

    public void OnDisconnect(int slot)
    {
        if (!_state.IsEditing(slot))
        {
            return;
        }
        if (_state.Editors.Count == 1 && _catalog.Set.Dirty)
        {
            _context.Logger.LogWarning("Last spawn editor left with unsaved changes: reloading the spawn file of {Map}", _catalog.MapName);
            _catalog.Reload();
        }
        Leave(null, slot);
    }

    public void Shutdown()
    {
        foreach (var slot in _state.Editors.ToList())
        {
            Leave(Utilities.GetPlayerFromSlot(slot), slot);
        }
        _markers.Clear();
    }

    private void Handle(CCSPlayerController player, string itemId)
    {
        switch (itemId)
        {
            case SpawnEditorMenu.SaveId:
                Save(player);
                return;
            case SpawnEditorMenu.ReloadId:
                Reload(player);
                return;
            case SpawnEditorMenu.NoclipId:
                ToggleNoclip(player);
                return;
            case SpawnEditorMenu.ExitId:
                HandleEditCommand(player, "exit");
                return;
            case SpawnEditorMenu.ExitSaveId:
                HandleEditCommand(player, "save");
                return;
            case SpawnEditorMenu.ExitDiscardId:
                Discard(player);
                return;
            case SpawnEditorMenu.DeleteId:
                DeleteNearest(player);
                return;
            case SpawnEditorMenu.TeamId or SpawnEditorMenu.SiteId or SpawnEditorMenu.PlantId:
                EditNearest(player, itemId);
                return;
        }
        if (SpawnEditorMenu.ParseAdd(itemId) is { } add)
        {
            AddHere(player, add);
        }
        else if (SpawnEditorMenu.ParseTeleport(itemId) is { } id && _catalog.Set.Spawns.FirstOrDefault(s => s.Id == id) is { } spawn)
        {
            TeleportTo(player, spawn);
        }
    }

    private void Discard(CCSPlayerController player)
    {
        _catalog.Reload();
        RefreshMarkers();
        Leave(player, player.Slot);
    }

    private void EditNearest(CCSPlayerController player, string itemId)
    {
        if (NearestTo(player) is not { } nearest)
        {
            Notify(player, "spawns.editor.none_nearby");
            return;
        }
        var change = itemId switch
        {
            SpawnEditorMenu.TeamId => new SpawnChange(Team: nearest.Team == TeamSide.T ? TeamSide.CT : TeamSide.T),
            SpawnEditorMenu.SiteId => new SpawnChange(Site: nearest.Site == BombSite.A ? BombSite.B : BombSite.A),
            _ => new SpawnChange(CanPlant: !nearest.CanPlant),
        };
        Apply(_catalog.Set.Update(nearest.Id, change));
        var updated = _catalog.Set.Spawns.First(s => s.Id == nearest.Id);
        Notify(player, "spawns.editor.updated", _catalog.Set.Label(updated));
    }

    private void TeleportTo(CCSPlayerController player, SpawnPoint spawn)
    {
        var p = spawn.Position;
        player.PlayerPawn.Value?.Teleport(
            new Vector(p.X, p.Y, p.Z), new QAngle(spawn.Angle.Pitch, spawn.Angle.Yaw, spawn.Angle.Roll), new Vector(0f, 0f, 0f));
        Notify(player, "spawns.editor.teleported", _catalog.Set.Label(spawn));
    }

    private void ToggleNoclip(CCSPlayerController player)
    {
        var (state, enabled) = _state.ToggleNoclip(player.Slot);
        _state = state;
        SetNoclip(player, enabled);
        Notify(player, enabled ? "spawns.editor.noclip_on" : "spawns.editor.noclip_off");
    }

    private void Apply(SpawnSet set)
    {
        _catalog.Replace(set);
        RefreshMarkers();
        RefreshMenus();
    }

    private void RefreshMarkers()
    {
        _markers.Clear();
        _nearest.Clear();
        if (_state.IsActive && _entitiesAllowed)
        {
            _markers.Rebuild(_catalog.Set);
        }
    }

    private void RefreshMenus()
    {
        foreach (var slot in _state.Editors)
        {
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player)
            {
                OpenMenu(player, refreshOnly: true);
            }
        }
    }

    private void OpenMenu(CCSPlayerController player, bool refreshOnly)
    {
        var view = new SpawnEditorView(_catalog.Set, NearestTo(player), _state.Noclip.Contains(player.Slot));
        _context.Bus.Publish(new HudMenuOpen(new PlayerId(player.Slot), SpawnEditorMenu.Build(view), refreshOnly));
    }

    private SpawnPoint? NearestTo(CCSPlayerController player) =>
        Pose(player) is { } pose ? _catalog.Set.Nearest(pose.Origin, NearestDistance) : null;

    // Spawns keep the editor's feet position and only the yaw of his view.
    private static (Vec3 Origin, ViewAngles Angles)? Pose(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is not { IsValid: true, AbsOrigin: { } origin } pawn)
        {
            return null;
        }
        return (new Vec3(origin.X, origin.Y, origin.Z), new ViewAngles(0f, pawn.EyeAngles.Y));
    }

    private static void SetNoclip(CCSPlayerController player, bool enabled)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            return;
        }
        var moveType = enabled ? MoveType_t.MOVETYPE_NOCLIP : MoveType_t.MOVETYPE_WALK;
        pawn.MoveType = moveType;
        pawn.ActualMoveType = moveType;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }

    private void Notify(CCSPlayerController? player, string key, params object[] args)
    {
        if (player is { IsValid: true })
        {
            _context.Text.Chat(player, key, args);
            return;
        }
        Server.PrintToConsole(_context.Text.Server(key, args));
    }
}
```

- [ ] **Step 3: Vérifier**

Run: `dotnet build RetakeV4.sln -c Release --nologo`
Expected: `0 Avertissement(s)`. Si une API diffère (`Teleport` avec angles null, `ActualMoveType`), la vérifier avec le dumper de réflexion et ledger l'écart.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Modules/Spawns/SpawnMarkers.cs src/RetakeV4/Modules/Spawns/SpawnEditor.cs
git commit -m "feat: éditeur de spawns (marqueurs réservés aux éditeurs, pause, noclip, menu HUD)"
```

---

### Task 7: Commandes de l'éditeur et branchement dans le module

**Files:**
- Modify: `src/RetakeV4/Modules/Spawns/SpawnsModule.cs`

**Interfaces:**
- Consumes: `SpawnEditor` (Task 6) ; `SpawnArgs`, `SpawnToAdd` (Tasks 1-2) ; `SpawnEditorRequested` (Task 4) ; helpers `IsAdmin`, `Reply` (Task 5).
- Produces: commandes `css_retake_edit`, `css_retake_addspawn`, `css_retake_delspawn`, `css_retake_tpspawn`, `css_retake_savespawns`, `css_retake_reloadspawns`, `css_retake_teleport` ; écoute de `noclip`, `HudMenuSelected`, `SpawnEditorRequested`, ticks, CheckTransmit, cycle de round et déconnexion.

- [ ] **Step 1: Branchement**

Dans `src/RetakeV4/Modules/Spawns/SpawnsModule.cs` :
- ajouter les `using` `RetakeV4.Domain.Geometry`, `System.Globalization` ;
- ajouter le champ `private SpawnEditor? _editor;` ;
- dans `Load`, juste après la création de `_catalog` :
```csharp
        _editor = new SpawnEditor(context, _catalog);
```
  remplacer `hooks.OnBus<MapStarted>(e => LoadMap(e.MapName));` par :
```csharp
        hooks.OnBus<MapStarted>(e =>
        {
            _editor?.Reset();
            LoadMap(e.MapName);
        });
```
  et, à la fin de `Load`, ajouter `RegisterEditor(hooks);` ;
- remplacer `Unload` par :
```csharp
    public void Unload()
    {
        _editor?.Shutdown();
        _editor = null;
        _catalog = null;
        _context = null;
    }
```
- ajouter :
```csharp
    private SpawnEditor Editor => _editor ?? throw new InvalidOperationException("Spawns module is not loaded");

    private void RegisterEditor(ModuleHooks hooks)
    {
        hooks.Command("css_retake_edit", "Spawn editor: css_retake_edit [save|discard|exit]", (p, c) => WithAdminPlayer(p, player => Editor.HandleEditCommand(player, c.ArgCount > 1 ? c.GetArg(1) : null)));
        hooks.Command("css_retake_addspawn", "Adds a spawn here: css_retake_addspawn <T|CT> <A|B> [plant]", OnAddSpawn);
        hooks.Command("css_retake_delspawn", "Deletes the nearest spawn", (p, _) => WithAdminPlayer(p, Editor.DeleteNearest));
        hooks.Command("css_retake_tpspawn", "Teleports to spawn <number>", OnTeleportToSpawn);
        hooks.Command("css_retake_teleport", "Teleports to <x> <y> <z>", OnTeleportToPosition);
        hooks.Command("css_retake_savespawns", "Saves the spawns of the current map", (p, _) => WithAdmin(p, () => Editor.Save(p)));
        hooks.Command("css_retake_reloadspawns", "Reloads the spawns of the current map", (p, _) => WithAdmin(p, () => Editor.Reload(p)));
        hooks.CommandListener("noclip", (p, _) => Editor.OnNoclipCommand(p), HookMode.Pre);
        hooks.OnBus<SpawnEditorRequested>(e =>
        {
            if (Utilities.GetPlayerFromSlot(e.Player.Slot) is { IsValid: true } player)
            {
                WithAdminPlayer(player, Editor.Enter);
            }
        });
        hooks.OnBus<HudMenuSelected>(e => Editor.OnSelected(e));
        hooks.OnTick("editor_tick", () => Editor.Tick());
        hooks.OnCheckTransmit("editor_transmit", infoList => Editor.OnCheckTransmit(infoList));
        hooks.OnEvent<EventRoundPrestart>("editor_prestart", _ => Editor.SuspendEntities());
        hooks.OnEvent<EventRoundStart>("editor_round_start", _ =>
            Server.NextFrame(() => _context?.Guard.Run(Name, "editor_resume", () => _editor?.ResumeEntities())));
        hooks.OnEvent<EventPlayerDisconnect>("editor_disconnect", e =>
        {
            if (e.Userid is { } player)
            {
                Editor.OnDisconnect(player.Slot);
            }
        });
    }

    private void OnAddSpawn(CCSPlayerController? player, CommandInfo command)
    {
        var team = SpawnArgs.Team(command.GetArg(1));
        var site = SpawnArgs.Site(command.GetArg(2));
        if (team is null || site is null)
        {
            Reply(player, "spawns.editor.usage_add");
            return;
        }
        var canPlant = command.ArgCount > 3 && SpawnArgs.IsPlantFlag(command.GetArg(3));
        WithAdminPlayer(player, p => Editor.AddHere(p, new SpawnToAdd(team.Value, site.Value, canPlant)));
    }

    private void OnTeleportToSpawn(CCSPlayerController? player, CommandInfo command)
    {
        if (!int.TryParse(command.GetArg(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            Reply(player, "spawns.editor.usage_tp");
            return;
        }
        WithAdminPlayer(player, p => Editor.TeleportToNumber(p, number));
    }

    private void OnTeleportToPosition(CCSPlayerController? player, CommandInfo command)
    {
        var coordinates = Enumerable.Range(1, 3)
            .Select(i => float.TryParse(command.GetArg(i), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value) ? value : (float?)null)
            .ToList();
        if (command.ArgCount < 4 || coordinates.Any(c => c is null))
        {
            Reply(player, "spawns.editor.usage_teleport");
            return;
        }
        WithAdminPlayer(player, p => Editor.TeleportToPosition(p, new Vec3(coordinates[0]!.Value, coordinates[1]!.Value, coordinates[2]!.Value)));
    }

    private void WithAdmin(CCSPlayerController? player, Action action)
    {
        if (!IsAdmin(player))
        {
            Reply(player, "spawns.editor.no_permission");
            return;
        }
        action();
    }

    private void WithAdminPlayer(CCSPlayerController? player, Action<CCSPlayerController> action)
    {
        if (player is not { IsValid: true })
        {
            Reply(player, "spawns.editor.player_only");
            return;
        }
        WithAdmin(player, () => action(player));
    }
```

- [ ] **Step 2: Vérifier**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`. `wc -l src/RetakeV4/Modules/Spawns/SpawnsModule.cs` < 800.

- [ ] **Step 3: Commit**

```bash
git add src/RetakeV4/Modules/Spawns/SpawnsModule.cs
git commit -m "feat: commandes de l'éditeur de spawns (edit, addspawn, delspawn, tpspawn, teleport, save, reload)"
```

---

### Task 8: Checklist en jeu phase 4a, documentation et vérification finale

**Files:**
- Modify: `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`

- [ ] **Step 1: Checklist**

Ajouter à `docs/CHECKLIST-INGAME.md` :
```markdown

## Phase 4a — Éditeur de spawns et forçage de site
- [ ] `css_retake_edit` sans la permission `@retakev4/admin` : refusé.
- [ ] `css_retake_edit` (admin) : le jeu passe en warmup en pause, le menu « Éditeur de spawns (N) » s'ouvre, piliers rouges (T) et bleus (CT), site B translucide, étiquettes `[CT][A] #07` lisibles de tous les côtés.
- [ ] Un joueur qui n'édite pas ne voit aucun marqueur ; deux éditeurs voient chacun seulement leur propre anneau jaune.
- [ ] L'anneau jaune suit le spawn le plus proche (< 150 unités) ; l'entrée « Le plus proche » du menu se met à jour.
- [ ] Ajouter un spawn T A (C4) ici, modifier équipe / site / C4 du plus proche, le supprimer : marqueurs et titre (`*`) à jour.
- [ ] « Aller à un spawn » (liste paginée) et `css_retake_tpspawn 3` téléportent ; `css_retake_teleport 0 0 0` aussi.
- [ ] Noclip via le menu et via la touche `noclip` (sans `sv_cheats`) ; il revient après le restart du warmup.
- [ ] « Quitter » avec des modifications : sous-menu « Sauvegarder et quitter / Quitter sans sauvegarder ». Sans sauvegarde, le fichier est rechargé.
- [ ] Sauvegarde : `spawns/<map>.json` réécrit en format V2, `<map>.json.bak` créé ; sur un fichier V3, `<map>.json.v3.bak` aussi.
- [ ] Le dernier éditeur qui sort relance le jeu (fin du warmup si le jeu n'était pas en warmup avant) ; le watchdog de warmup ne coupe jamais l'édition.
- [ ] Commandes console V3 : `css_retake_addspawn 2 0`, `css_retake_addspawn CT B`, `css_retake_delspawn`, `css_retake_savespawns`, `css_retake_reloadspawns` depuis la console serveur (save/reload) et en jeu.
- [ ] Déconnexion du dernier éditeur avec des modifications non sauvegardées : le jeu reprend avec le fichier sauvegardé, aucun marqueur orphelin (`ent_find beam`).
- [ ] `css_retake_forcesite B` : le round suivant est sur B, puis tirage normal ; `css_retake_forcesite A sticky` : tous les rounds sur A jusqu'à `css_retake_forcesite off`.
- [ ] `css_retake_forcesite B` sur une map sans spawn B : refusé.
- [ ] Map sans fichier de spawns : chaque admin reçoit l'alerte HUD « Aucun spawn pour <map> » à chaque round.
```

- [ ] **Step 2: CLAUDE.md**

Dans la section Règles de `CLAUDE.md`, ajouter :
```markdown
- Spawns : lecture et écriture uniquement via `SpawnCatalog` / `SpawnFileStore` (écriture atomique, `.bak`). Entités d'éditeur uniquement via `SpawnMarkers`, jamais entre `round_prestart` et la frame après `round_start`.
```

- [ ] **Step 3: Vérification finale**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo && dotnet test tests/RetakeV4.Domain.Tests --nologo -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`
puis (outil PowerShell) `pwsh -NoProfile -File scripts/package-dev.ps1`
Expected: 0 warning, `Failed: 0`, couverture Domain ≥ 80 %, `Package ready`.

- [ ] **Step 4: Commit**

```bash
git add docs/CHECKLIST-INGAME.md CLAUDE.md
git commit -m "docs: checklist en jeu phase 4a et règles de l'éditeur de spawns"
```
