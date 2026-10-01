# RetakeV4 phase 3b — Moteur HUD et menu d'armes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Donner aux joueurs un HUD modulaire (bloc d'informations centré + menus `point_worldtext` pilotés au viseur et au clavier) et un menu d'armes `!guns` qui modifie leurs préférences, avec application immédiate pendant le freeze time.

**Architecture:** Toute la logique (arbre de menu, navigation, pagination, widgets, composition, construction du menu d'armes, règles d'application) vit dans `RetakeV4.Domain` et est testée. Un nouveau module `Hud` traduit cette logique en entités CS2 (`point_worldtext`, center HTML) et en entrées (viseur, clic, W/S/E, touches 1-9). Les modules ne se référencent pas : `Allocation` publie `HudMenuOpen` / `HudMenuClose` / `HudAlert` sur le bus et écoute `HudMenuSelected`.

**Tech Stack:** C# / .NET 10, CounterStrikeSharp.API 1.0.370, xUnit 2.9.3, System.Collections.Immutable.

**Spec:** `docs/superpowers/specs/2026-09-30-retake-v4-design.md` (sections 4 « Moteur HUD », 5.2-5.4, 12, 17)

## Global Constraints

- CounterStrikeSharp.API 1.0.370, `[MinimumApiVersion(370)]`, .NET 10.
- `dotnet build RetakeV4.sln -c Release` : 0 warning (TreatWarningsAsErrors).
- `RetakeV4.Domain` : aucune référence à CounterStrikeSharp, couverture de lignes ≥ 80 %.
- Un module ne référence jamais un autre module : bus (`IEventBus`), `PreparationPipeline` ou services du `ModuleContext`.
- Tout handler CSSharp passe par `context.Hooks` ; tout `Server.NextFrame` d'un module passe par `_context?.Guard.Run`.
- Textes joueurs uniquement via `lang/*.json`, clés `module.section.key` (regex `^[a-z]+(\.[a-z0-9_]+)+$`), en et fr synchronisés avec les mêmes placeholders.
- Valeurs par défaut de config uniquement dans les records C# (`*Config.cs`), validées au chargement (valeur invalide → défaut + avertissement).
- Commandes `!guns` : alias V3 `guns`, `gans`, `gun`, `g`, `gns`, `gnus`, `weapon`, `waepon`, `weapons`, `waepons`, `waffen`, `menu`, `allocator`, `select`.

## Décisions de conception (écarts assumés vs spec)

1. **Prototype non validé** : `docs/spikes/hud-probe-findings.md` n'est pas rempli. Les paramètres incertains sont tous configurables dans `hud.json` (`Menu.Orientation` 0/1/2 = formules du prototype, `Menu.FollowMode` Tick/Parent, `Menu.Input` AimAndKeys/Keys, distances). Les défauts reprennent ceux du prototype (orientation 0, repositionnement à chaque tick). La checklist 3b reprend les 12 questions du prototype.
2. **Entrée par serveur, pas par menu** : `Menu.Input` est global dans `hud.json` (la spec le met sous `Widgets.WeaponMenu`). Un seul menu existe en 3b.
3. **Plafond d'entités fixe** : 9 lignes maximum par page (pagination automatique au-delà de 8 éléments) + 1 titre = 10 entités par joueur. Pas de réglage `MaxEntitiesPerPlayer`.
4. **`EntityGate` simplifié** : un indicateur dans le module Hud. Entre `round_prestart` et la frame suivant `round_start`, aucune entité n'est créée ; les menus restent ouverts (état conservé) et sont recréés à la réouverture de la fenêtre.
5. **`AllowPlayerChoice` dérivé** : une configuration (équipe × type de round) est proposée si elle offre au moins 2 armes principales ou 2 secondaires (défaut inclus). Pas de nouveau champ dans `roundtypes.json`.
6. **AWP** : jamais proposée comme arme principale (uniquement via le volontariat). Basculer l'AWP pendant le freeze time s'applique au round suivant ; changer d'arme pendant le freeze time conserve l'AWP, le kit, le Zeus et les grenades déjà donnés.
7. **Center HTML** : recalculé toutes les `CenterRefreshMs`, renvoyé à chaque tick tant qu'il y a un contenu. Pas de « retrait » automatique devant les messages natifs (les widgets n'affichent quelque chose que quelques secondes).
8. **Retour joueur** : les confirmations du menu d'armes passent par le widget `Alerts` (`HudAlert`), pas par le chat.
9. **Reporté en phase 5** : le message de rappel `HowToMessage` (spec 5.4) dépend du mode d'achat (`Menu` / `NativeBuy` / `Both`), livré en phase 5 ; widget `AdminMenu` en phase 4.

## Review Focus

1. Un pool de plus de 8 armes → le menu est paginé, jamais plus de 9 lignes, et les touches 1-9 visent toujours une ligne visible (Task 1 : `ManyItems_ArePaged_WithinNineLines`).
2. Un nom de type de round contenant `:` ou des caractères HTML → l'identifiant de sélection fait l'aller-retour (Task 2 : `Selection_RoundTrips_WithColonsInTheRoundType`) et le center HTML est échappé (Task 3 : `Format_EscapesHtml`).
3. Une sélection devenue invalide (arme retirée du pool après rechargement, AWP, type de round inconnu) → refusée, préférence intacte (Task 2 : `IsAllowed_RejectsStaleOrForbiddenWeapons`).
4. Un choix fait en round live, pour une autre équipe ou un autre type de round, ou en étant mort → enregistré pour plus tard, jamais appliqué tout de suite (Task 2 : `AppliesNow_OnlyDuringFreezeTimeForTheOwnCurrentConfig`).
5. Un joueur qui a reçu l'AWP et change d'arme pendant le freeze time → garde l'AWP, son kit et ses grenades (Task 2 : `WithWeapons_KeepsTheAwpAndTheRestOfTheLoadout`).

---

## File Structure

```
src/RetakeV4.Domain/Hud/HudText.cs, Menu.cs, MenuNavigator.cs, MenuInput.cs      (Task 1)
src/RetakeV4.Domain/Hud/AimMenuLayout.cs                                         (Task 1, + Centered)
src/RetakeV4.Domain/Loadouts/WeaponNames.cs, WeaponMenu.cs                       (Task 2)
src/RetakeV4.Domain/Loadouts/LoadoutPlanner.cs, Preferences/PreferenceBook.cs    (Task 2, ajouts)
src/RetakeV4.Domain/Hud/HudLine.cs, RoundInfoWidget.cs, QueueStatusWidget.cs,
    AlertsWidget.cs, CenterComposer.cs, CenterHtml.cs                            (Task 3)
src/RetakeV4.Domain/Events/HudEvents.cs, RoundEvents.cs                          (Task 4)
src/RetakeV4/Modules/RoundTypes/RoundTypesModule.cs, Teams/TeamsModule.cs        (Task 4, publication)
src/RetakeV4/Modules/Hud/HudConfig.cs, HudConfigValidator.cs, lang/*.json        (Task 5)
src/RetakeV4/Modules/ModuleHooks.cs, Hud/HudModule.cs, Hud/CenterHud.cs,
    Hud/HudTextFormatter.cs, RetakeV4Plugin.cs                                   (Task 6)
src/RetakeV4/Adapters/PlayerView.cs, Modules/Hud/MenuSession.cs,
    WorldTextMenuView.cs, MenuHud.cs                                             (Task 7)
src/RetakeV4/Modules/Allocation/*                                                (Task 8)
docs/CHECKLIST-INGAME.md, CLAUDE.md                                              (Task 9)
```

---

### Task 1: Arbre de menu, navigation et contrôles (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Hud/HudText.cs`, `src/RetakeV4.Domain/Hud/Menu.cs`, `src/RetakeV4.Domain/Hud/MenuNavigator.cs`, `src/RetakeV4.Domain/Hud/MenuInput.cs`
- Modify: `src/RetakeV4.Domain/Hud/AimMenuLayout.cs`
- Test: `tests/RetakeV4.Domain.Tests/Hud/MenuNavigatorTests.cs`, `tests/RetakeV4.Domain.Tests/Hud/MenuInputTests.cs`

**Interfaces:**
- Consumes: `AimMenuLayout`, `AimLineResolver` (phase 0), `RoundPhase`.
- Produces:
  - `sealed record HudText(string? Key, string? Literal, IReadOnlyList<object> Args)` avec `static HudText Of(string key, params object[] args)` et `static HudText Raw(string text)`.
  - `enum MenuItemKind { Action, Choice, Toggle, Submenu }`, `sealed record MenuItem(string Id, HudText Label, MenuItemKind Kind, bool IsOn = false, Menu? Submenu = null)`, `sealed record Menu(string Id, HudText Title, IReadOnlyList<MenuItem> Items)`.
  - `enum MenuLineKind { Item, Previous, Next, Back, Close }`, `sealed record MenuLine(string Id, HudText Label, MenuLineKind Kind, MenuItemKind? ItemKind = null, bool IsOn = false)`.
  - `enum MenuOutcomeKind { None, Selected, Closed }`, `sealed record MenuOutcome(MenuOutcomeKind Kind, string? ItemId = null)` avec `None`, `Closed`, `Selected(string)`.
  - `sealed record MenuNavigator` : `const int MaxLines = 9`, ids `PreviousId`/`NextId`/`BackId`/`CloseId`, `static Open(Menu)`, `Root`, `Path`, `Page`, `Cursor`, `Current`, `Lines()`, `Move(int)`, `Activate(int)`, `ActivateCursor()`, `Replace(Menu)`.
  - `enum MenuInputSetting { AimAndKeys, Keys }`, `sealed record MenuControls(bool Aim, bool Movement, bool Keys)`, `static MenuInput.For(RoundPhase phase, bool alive, MenuInputSetting setting)`.
  - `AimMenuLayout.Centered(int lineCount, float distanceUnits, float lineHeightUnits, float halfWidthUnits)`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Hud/MenuNavigatorTests.cs`
```csharp
using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Tests.Hud;

public class MenuNavigatorTests
{
    private static MenuItem Choice(string id) => new(id, HudText.Raw(id), MenuItemKind.Choice);

    private static Menu Weapons(int count) =>
        new("weapons", HudText.Raw("Primary"), Enumerable.Range(1, count).Select(i => Choice($"w{i}")).ToList());

    private static Menu Root(Menu? submenu = null) => new("root", HudText.Of("title"), new[]
    {
        new MenuItem("primary", HudText.Raw("Primary"), MenuItemKind.Submenu, Submenu: submenu ?? Weapons(3)),
        new MenuItem("awp", HudText.Raw("AWP"), MenuItemKind.Toggle, IsOn: true),
        new MenuItem("info", HudText.Raw("Info"), MenuItemKind.Action),
    });

    [Fact]
    public void RootLines_EndWithClose()
    {
        var lines = MenuNavigator.Open(Root()).Lines();
        Assert.Equal(new[] { "primary", "awp", "info", MenuNavigator.CloseId }, lines.Select(l => l.Id));
        Assert.True(lines[1].IsOn);
        Assert.Equal(MenuItemKind.Toggle, lines[1].ItemKind);
    }

    [Fact]
    public void Submenu_IsEntered_AndBackReturnsToTheParentEntry()
    {
        var (inside, outcome) = MenuNavigator.Open(Root()).Activate(0);
        Assert.Equal(MenuOutcome.None, outcome);
        Assert.Equal(new[] { "w1", "w2", "w3", MenuNavigator.BackId }, inside.Lines().Select(l => l.Id));
        var (back, _) = inside.Activate(3);
        Assert.Empty(back.Path);
        Assert.Equal(0, back.Cursor);
    }

    [Fact]
    public void Choice_InASubmenu_IsSelected_AndReturnsToTheParent()
    {
        var (inside, _) = MenuNavigator.Open(Root()).Activate(0);
        var (after, outcome) = inside.Activate(1);
        Assert.Equal(MenuOutcome.Selected("w2"), outcome);
        Assert.Empty(after.Path);
        Assert.Equal("primary", after.Lines()[after.Cursor].Id);
    }

    [Fact]
    public void Toggle_IsSelected_AndStaysInPlace()
    {
        var (after, outcome) = MenuNavigator.Open(Root()).Activate(1);
        Assert.Equal(MenuOutcome.Selected("awp"), outcome);
        Assert.Equal(1, after.Cursor);
    }

    [Fact]
    public void Close_ClosesTheMenu() =>
        Assert.Equal(MenuOutcome.Closed, MenuNavigator.Open(Root()).Activate(3).Outcome);

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9)]
    public void OutOfRangeLine_DoesNothing(int index)
    {
        var navigator = MenuNavigator.Open(Root());
        var (next, outcome) = navigator.Activate(index);
        Assert.Same(navigator, next);
        Assert.Equal(MenuOutcome.None, outcome);
    }

    [Fact]
    public void EightItems_FitWithoutPaging()
    {
        var (inside, _) = MenuNavigator.Open(Root(Weapons(8))).Activate(0);
        Assert.Equal(MenuNavigator.MaxLines, inside.Lines().Count);
    }

    [Fact]
    public void ManyItems_ArePaged_WithinNineLines()
    {
        var (inside, _) = MenuNavigator.Open(Root(Weapons(13))).Activate(0);
        var first = inside.Lines();
        Assert.Equal(new[] { "w1", "w2", "w3", "w4", "w5", "w6", MenuNavigator.NextId, MenuNavigator.BackId }, first.Select(l => l.Id));
        var (second, _) = inside.Activate(6);
        Assert.Equal(
            new[] { "w7", "w8", "w9", "w10", "w11", "w12", MenuNavigator.PreviousId, MenuNavigator.NextId, MenuNavigator.BackId },
            second.Lines().Select(l => l.Id));
        var (third, _) = second.Activate(7);
        Assert.Equal(new[] { "w13", MenuNavigator.PreviousId, MenuNavigator.BackId }, third.Lines().Select(l => l.Id));
        var (backToSecond, _) = third.Activate(1);
        Assert.Equal("w7", backToSecond.Lines()[0].Id);
        Assert.All(new[] { first, second.Lines(), third.Lines() }, lines => Assert.True(lines.Count <= MenuNavigator.MaxLines));
    }

    [Fact]
    public void ChoiceOnASecondPage_ReturnsToTheParentEntry()
    {
        var (inside, _) = MenuNavigator.Open(Root(Weapons(13))).Activate(0);
        var (second, _) = inside.Activate(6);
        var (after, outcome) = second.Activate(2);
        Assert.Equal(MenuOutcome.Selected("w9"), outcome);
        Assert.Equal("primary", after.Lines()[after.Cursor].Id);
    }

    [Fact]
    public void Move_IsClampedToTheLines()
    {
        var navigator = MenuNavigator.Open(Root());
        Assert.Equal(0, navigator.Move(-1).Cursor);
        Assert.Equal(3, navigator.Move(10).Cursor);
        Assert.Equal(MenuOutcome.Selected("awp"), navigator.Move(1).ActivateCursor().Outcome);
    }

    [Fact]
    public void Replace_KeepsTheOpenSubmenu_WhenItStillExists()
    {
        var (inside, _) = MenuNavigator.Open(Root()).Activate(0);
        var replaced = inside.Move(2).Replace(Root(Weapons(5)));
        Assert.Equal(new[] { "primary" }, replaced.Path);
        Assert.Equal(2, replaced.Cursor);
        Assert.Equal(6, replaced.Lines().Count);
    }

    [Fact]
    public void Replace_ReturnsToTheRoot_WhenTheSubmenuDisappeared()
    {
        var (inside, _) = MenuNavigator.Open(Root()).Activate(0);
        var withoutSubmenu = new Menu("root", HudText.Of("title"), new[] { new MenuItem("awp", HudText.Raw("AWP"), MenuItemKind.Toggle) });
        var replaced = inside.Replace(withoutSubmenu);
        Assert.Empty(replaced.Path);
        Assert.Equal(new[] { "awp", MenuNavigator.CloseId }, replaced.Lines().Select(l => l.Id));
        Assert.Equal(0, replaced.Cursor);
    }

    [Fact]
    public void Replace_WithAnotherMenu_StartsOver()
    {
        var (inside, _) = MenuNavigator.Open(Root()).Activate(0);
        var other = new Menu("other", HudText.Raw("Other"), new[] { Choice("x") });
        var replaced = inside.Replace(other);
        Assert.Empty(replaced.Path);
        Assert.Equal("other", replaced.Root.Id);
    }

    [Fact]
    public void EmptySubmenu_OnlyOffersBack()
    {
        var (inside, _) = MenuNavigator.Open(Root(Weapons(0))).Activate(0);
        Assert.Equal(new[] { MenuNavigator.BackId }, inside.Lines().Select(l => l.Id));
    }
}
```

`tests/RetakeV4.Domain.Tests/Hud/MenuInputTests.cs`
```csharp
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Hud;

public class MenuInputTests
{
    [Fact]
    public void LiveRound_AlivePlayer_OnlyGetsKeys() =>
        Assert.Equal(new MenuControls(false, false, true), MenuInput.For(RoundPhase.Live, true, MenuInputSetting.AimAndKeys));

    [Theory]
    [InlineData(RoundPhase.FreezeTime, true)]
    [InlineData(RoundPhase.Live, false)]
    [InlineData(RoundPhase.Warmup, true)]
    [InlineData(RoundPhase.PostRound, true)]
    public void OtherwiseEverythingIsAvailable(RoundPhase phase, bool alive) =>
        Assert.Equal(new MenuControls(true, true, true), MenuInput.For(phase, alive, MenuInputSetting.AimAndKeys));

    [Fact]
    public void KeysSetting_DisablesAim() =>
        Assert.Equal(new MenuControls(false, true, true), MenuInput.For(RoundPhase.FreezeTime, true, MenuInputSetting.Keys));

    [Fact]
    public void CenteredLayout_PutsTheMiddleLineInFrontOfTheEyes()
    {
        var layout = AimMenuLayout.Centered(5, 60f, 4f, 18f);
        Assert.Equal(8f, layout.FirstLineUpUnits);
        var straight = new ViewAngles(0f, 90f);
        Assert.Equal(2, AimLineResolver.Resolve(layout, straight, straight));
    }
}
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`HudText`, `MenuNavigator`, `MenuInput` introuvables).

- [ ] **Step 2: Implémentation**

`src/RetakeV4.Domain/Hud/HudText.cs`
```csharp
namespace RetakeV4.Domain.Hud;

// Key is resolved per player through the localizer; Literal is shown as is (weapon names, config values).
public sealed record HudText(string? Key, string? Literal, IReadOnlyList<object> Args)
{
    public static HudText Of(string key, params object[] args) => new(key, null, args);

    public static HudText Raw(string text) => new(null, text, Array.Empty<object>());
}
```

`src/RetakeV4.Domain/Hud/Menu.cs`
```csharp
namespace RetakeV4.Domain.Hud;

public enum MenuItemKind
{
    Action,
    Choice,
    Toggle,
    Submenu,
}

public sealed record MenuItem(string Id, HudText Label, MenuItemKind Kind, bool IsOn = false, Menu? Submenu = null);

public sealed record Menu(string Id, HudText Title, IReadOnlyList<MenuItem> Items);

public enum MenuLineKind
{
    Item,
    Previous,
    Next,
    Back,
    Close,
}

public sealed record MenuLine(string Id, HudText Label, MenuLineKind Kind, MenuItemKind? ItemKind = null, bool IsOn = false);

public enum MenuOutcomeKind
{
    None,
    Selected,
    Closed,
}

public sealed record MenuOutcome(MenuOutcomeKind Kind, string? ItemId = null)
{
    public static MenuOutcome None { get; } = new(MenuOutcomeKind.None);

    public static MenuOutcome Closed { get; } = new(MenuOutcomeKind.Closed);

    public static MenuOutcome Selected(string itemId) => new(MenuOutcomeKind.Selected, itemId);
}
```

`src/RetakeV4.Domain/Hud/MenuNavigator.cs`
```csharp
using System.Collections.Immutable;

namespace RetakeV4.Domain.Hud;

public sealed record MenuNavigator
{
    public const int MaxLines = 9;
    public const string PreviousId = "__previous";
    public const string NextId = "__next";
    public const string BackId = "__back";
    public const string CloseId = "__close";

    // A paged menu needs room for previous, next and back/close.
    private const int ItemsPerPage = MaxLines - 3;

    private MenuNavigator(Menu root, ImmutableList<string> path, int page, int cursor)
    {
        Root = root;
        Path = path;
        Page = page;
        Cursor = cursor;
    }

    public Menu Root { get; private init; }

    public ImmutableList<string> Path { get; private init; }

    public int Page { get; private init; }

    public int Cursor { get; private init; }

    public Menu Current => Resolve(Root, Path).Menu;

    public static MenuNavigator Open(Menu root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new MenuNavigator(root, ImmutableList<string>.Empty, 0, 0);
    }

    public IReadOnlyList<MenuLine> Lines()
    {
        var items = Current.Items;
        var exit = Path.IsEmpty
            ? new MenuLine(CloseId, HudText.Of("hud.menu.close"), MenuLineKind.Close)
            : new MenuLine(BackId, HudText.Of("hud.menu.back"), MenuLineKind.Back);
        if (items.Count < MaxLines)
        {
            return items.Select(ToLine).Append(exit).ToList();
        }
        var page = ClampPage(Page, items.Count);
        var lines = items.Skip(page * ItemsPerPage).Take(ItemsPerPage).Select(ToLine).ToList();
        if (page > 0)
        {
            lines.Add(new MenuLine(PreviousId, HudText.Of("hud.menu.previous"), MenuLineKind.Previous));
        }
        if (page < PageCount(items.Count) - 1)
        {
            lines.Add(new MenuLine(NextId, HudText.Of("hud.menu.next"), MenuLineKind.Next));
        }
        lines.Add(exit);
        return lines;
    }

    public MenuNavigator Move(int delta) => this with { Cursor = Math.Clamp(Cursor + delta, 0, Lines().Count - 1) };

    public (MenuNavigator Next, MenuOutcome Outcome) ActivateCursor() => Activate(Cursor);

    public (MenuNavigator Next, MenuOutcome Outcome) Activate(int lineIndex)
    {
        var lines = Lines();
        if (lineIndex < 0 || lineIndex >= lines.Count)
        {
            return (this, MenuOutcome.None);
        }
        var line = lines[lineIndex];
        var page = ClampPage(Page, Current.Items.Count);
        return line.Kind switch
        {
            MenuLineKind.Previous => (this with { Page = page - 1, Cursor = 0 }, MenuOutcome.None),
            MenuLineKind.Next => (this with { Page = page + 1, Cursor = 0 }, MenuOutcome.None),
            MenuLineKind.Back => (Pop(), MenuOutcome.None),
            MenuLineKind.Close => (this, MenuOutcome.Closed),
            _ => ActivateItem(Current.Items.First(i => i.Id == line.Id), lineIndex),
        };
    }

    // Same menu id: keep the open submenu, page and cursor where still valid. Another id: start over.
    public MenuNavigator Replace(Menu root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Id != Root.Id)
        {
            return Open(root);
        }
        var (menu, validPath) = Resolve(root, Path);
        var kept = validPath.Count == Path.Count;
        var replaced = new MenuNavigator(root, validPath, kept ? ClampPage(Page, menu.Items.Count) : 0, 0);
        return kept ? replaced with { Cursor = Math.Clamp(Cursor, 0, replaced.Lines().Count - 1) } : replaced;
    }

    private (MenuNavigator Next, MenuOutcome Outcome) ActivateItem(MenuItem item, int lineIndex) => item.Kind switch
    {
        MenuItemKind.Submenu when item.Submenu is not null => (this with { Path = Path.Add(item.Id), Page = 0, Cursor = 0 }, MenuOutcome.None),
        MenuItemKind.Submenu => (this, MenuOutcome.None),
        MenuItemKind.Toggle => (this with { Cursor = lineIndex }, MenuOutcome.Selected(item.Id)),
        _ => (Path.IsEmpty ? this with { Cursor = lineIndex } : Pop(), MenuOutcome.Selected(item.Id)),
    };

    // Going back puts the cursor on the submenu entry that was left, on the right page.
    private MenuNavigator Pop()
    {
        if (Path.IsEmpty)
        {
            return this;
        }
        var left = Path[^1];
        var parent = new MenuNavigator(Root, Path.RemoveAt(Path.Count - 1), 0, 0);
        var items = parent.Current.Items;
        var index = items.ToList().FindIndex(i => i.Id == left);
        var onPage = parent with { Page = index < 0 ? 0 : ClampPage(index / ItemsPerPage, items.Count) };
        var lineIndex = onPage.Lines().ToList().FindIndex(l => l.Id == left);
        return onPage with { Cursor = Math.Max(0, lineIndex) };
    }

    private static (Menu Menu, ImmutableList<string> Path) Resolve(Menu root, ImmutableList<string> path)
    {
        var menu = root;
        var valid = ImmutableList<string>.Empty;
        foreach (var id in path)
        {
            var next = menu.Items.FirstOrDefault(i => i.Id == id && i.Kind == MenuItemKind.Submenu)?.Submenu;
            if (next is null)
            {
                break;
            }
            menu = next;
            valid = valid.Add(id);
        }
        return (menu, valid);
    }

    private static MenuLine ToLine(MenuItem item) => new(item.Id, item.Label, MenuLineKind.Item, item.Kind, item.IsOn);

    private static int PageCount(int itemCount) => (itemCount + ItemsPerPage - 1) / ItemsPerPage;

    private static int ClampPage(int page, int itemCount) =>
        itemCount < MaxLines ? 0 : Math.Clamp(page, 0, PageCount(itemCount) - 1);
}
```

`src/RetakeV4.Domain/Hud/MenuInput.cs`
```csharp
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Hud;

public enum MenuInputSetting
{
    AimAndKeys,
    Keys,
}

public sealed record MenuControls(bool Aim, bool Movement, bool Keys);

public static class MenuInput
{
    // A living player in a live round keeps movement and shooting: only the number keys drive the menu.
    public static MenuControls For(RoundPhase phase, bool alive, MenuInputSetting setting) =>
        phase == RoundPhase.Live && alive
            ? new MenuControls(false, false, true)
            : new MenuControls(setting == MenuInputSetting.AimAndKeys, true, true);
}
```

Dans `src/RetakeV4.Domain/Hud/AimMenuLayout.cs`, ajouter après le constructeur :
```csharp
    public static AimMenuLayout Centered(int lineCount, float distanceUnits, float lineHeightUnits, float halfWidthUnits) =>
        new(lineCount, distanceUnits, lineHeightUnits, (lineCount - 1) * lineHeightUnits / 2f, halfWidthUnits);
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4.Domain/Hud tests/RetakeV4.Domain.Tests/Hud
git commit -m "feat: arbre de menu, navigation paginée et contrôles du HUD (Domain)"
```

---

### Task 2: Menu d'armes et règles de changement (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Loadouts/WeaponNames.cs`, `src/RetakeV4.Domain/Loadouts/WeaponMenu.cs`
- Modify: `src/RetakeV4.Domain/Loadouts/LoadoutPlanner.cs`, `src/RetakeV4.Domain/Preferences/PreferenceBook.cs`
- Test: `tests/RetakeV4.Domain.Tests/Loadouts/WeaponMenuTests.cs`, `tests/RetakeV4.Domain.Tests/Loadouts/LoadoutPlannerTests.cs`, `tests/RetakeV4.Domain.Tests/Preferences/PreferenceBookTests.cs`

**Interfaces:**
- Consumes: `HudText`, `Menu`, `MenuItem`, `MenuItemKind` (Task 1) ; `RoundTypeDefinition`, `LoadoutPreference`, `LoadoutRequest`, `Loadout`, `LoadoutPlanner.ResolveWeapons`, `WeaponCatalog`, `PreferenceBook`, `RoundPhase`.
- Produces:
  - `static WeaponNames.Display(string? weaponId)` (`null` → `"-"`).
  - `enum WeaponSlot { Primary, Secondary }` (namespace `RetakeV4.Domain.Loadouts`).
  - `sealed record WeaponMenuSelection(TeamSide Team, string RoundType, WeaponSlot Slot, string Weapon)` avec `string ToItemId()` et `static WeaponMenuSelection? Parse(string itemId)`.
  - `sealed record WeaponMenuState(IReadOnlyList<RoundTypeDefinition> Definitions, RoundTypeDefinition? Current, TeamSide? Team, Func<TeamSide, string, LoadoutPreference?> PreferenceFor, bool AwpOptIn)`.
  - `static class WeaponMenu` : `MenuId = "allocation.weapons"`, `AwpItemId = "awp"`, `CurrentItemId = "current"`, `OthersItemId = "others"`, `Options(def, team, slot)`, `HasChoice(def, team)`, `IsAllowed(selection, definitions)`, `AppliesNow(selection, phase, alive, side, currentRoundType)`, `Build(WeaponMenuState)`.
  - `PreferenceBook.SetWeapon(ulong steamId, TeamSide team, string roundType, WeaponSlot slot, string weapon)` → `(PreferenceBook Book, StoredPreference Change)`.
  - `LoadoutPlanner.WithWeapons(Loadout current, RoundTypeDefinition definition, LoadoutRequest request)` → `Loadout`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Loadouts/WeaponMenuTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Loadouts;

public class WeaponMenuTests
{
    private static RoundTypeDefinition Definition(string name, string[] primaries, string[] secondaries, string? defaultPrimary = "weapon_ak47") =>
        new(
            name,
            ArmorKind.KevlarHelmet,
            new TeamWeapons(primaries, primaries, Array.Empty<string>()),
            new TeamWeapons(secondaries, secondaries, Array.Empty<string>()),
            new TeamDefault(defaultPrimary, "weapon_glock"),
            new TeamDefault(defaultPrimary, "weapon_usp_silencer"),
            new AwpSettings(true, 1, 0, 1.0),
            new DefuseKitSettings(DefuseKitMode.All, 0, 1.0, false),
            new ZeusSettings(false, 0),
            "default");

    private static readonly RoundTypeDefinition FullBuy =
        Definition("FullBuy", new[] { "weapon_ak47", "weapon_m4a1_silencer" }, new[] { "weapon_deagle" });

    private static readonly RoundTypeDefinition Pistol =
        Definition("Pistol", Array.Empty<string>(), new[] { "weapon_p250", "weapon_tec9" }, defaultPrimary: null);

    private static readonly RoundTypeDefinition Fixed =
        Definition("Fixed", Array.Empty<string>(), Array.Empty<string>());

    private static WeaponMenuState State(
        TeamSide? team, RoundTypeDefinition? current, LoadoutPreference? preference = null, bool awp = false, params RoundTypeDefinition[] definitions) =>
        new(definitions.Length > 0 ? definitions : new[] { FullBuy }, current, team, (_, _) => preference, awp);

    private static MenuItem Item(Menu menu, string id) => Assert.Single(menu.Items, i => i.Id == id);

    [Fact]
    public void Options_IncludeTheDefault_AndExcludeAwpAndWrongSlots()
    {
        var definition = Definition("Mixed", new[] { "weapon_m4a1", "weapon_awp", "weapon_glock" }, new[] { "weapon_ak47" });
        Assert.Equal(new[] { "weapon_ak47", "weapon_m4a1" }, WeaponMenu.Options(definition, TeamSide.T, WeaponSlot.Primary));
        Assert.Equal(new[] { "weapon_glock" }, WeaponMenu.Options(definition, TeamSide.T, WeaponSlot.Secondary));
    }

    [Fact]
    public void Build_CurrentConfig_ListsBothSlots_WithTheEffectiveChoiceMarked()
    {
        var menu = WeaponMenu.Build(State(TeamSide.CT, FullBuy, new LoadoutPreference("weapon_m4a1_silencer", null, false)));
        Assert.Equal(WeaponMenu.MenuId, menu.Id);
        var current = Item(menu, WeaponMenu.CurrentItemId).Submenu!;
        Assert.Equal(new[] { "primary", "secondary" }, current.Items.Select(i => i.Id));
        var primaries = Item(current, "primary").Submenu!.Items;
        Assert.Equal(new[] { "AK-47", "M4A1-S" }, primaries.Select(i => i.Label.Literal));
        Assert.Equal(new[] { false, true }, primaries.Select(i => i.IsOn));
        Assert.All(primaries, i => Assert.Equal(MenuItemKind.Choice, i.Kind));
        Assert.Equal(new object[] { "USP-S" }, Item(current, "secondary").Label.Args);
    }

    [Fact]
    public void Build_PistolRound_OnlyOffersTheSecondary()
    {
        var menu = WeaponMenu.Build(State(TeamSide.T, Pistol, definitions: new[] { Pistol }));
        var current = Item(menu, WeaponMenu.CurrentItemId).Submenu!;
        Assert.Equal(new[] { "secondary" }, current.Items.Select(i => i.Id));
    }

    [Fact]
    public void Build_Others_ExcludeTheCurrentConfig_AndConfigsWithoutChoice()
    {
        var menu = WeaponMenu.Build(State(TeamSide.T, FullBuy, definitions: new[] { FullBuy, Fixed }));
        var others = Item(menu, WeaponMenu.OthersItemId).Submenu!;
        Assert.Equal(new[] { "cfg:CT:FullBuy" }, others.Items.Select(i => i.Id));
    }

    [Fact]
    public void Build_ForASpectator_HasNoCurrentEntry_ButOffersEveryConfig()
    {
        var menu = WeaponMenu.Build(State(null, FullBuy));
        Assert.DoesNotContain(menu.Items, i => i.Id == WeaponMenu.CurrentItemId);
        Assert.Equal(new[] { "cfg:T:FullBuy", "cfg:CT:FullBuy" }, Item(menu, WeaponMenu.OthersItemId).Submenu!.Items.Select(i => i.Id));
    }

    [Fact]
    public void Build_WithoutAnyChoice_OnlyShowsTheAwpToggle()
    {
        var menu = WeaponMenu.Build(State(TeamSide.T, Fixed, awp: true, definitions: new[] { Fixed }));
        var awp = Assert.Single(menu.Items);
        Assert.Equal(WeaponMenu.AwpItemId, awp.Id);
        Assert.Equal(MenuItemKind.Toggle, awp.Kind);
        Assert.True(awp.IsOn);
    }

    [Fact]
    public void Selection_RoundTrips_WithColonsInTheRoundType()
    {
        var selection = new WeaponMenuSelection(TeamSide.CT, "Full:Buy<b>", WeaponSlot.Secondary, "weapon_deagle");
        Assert.Equal(selection, WeaponMenuSelection.Parse(selection.ToItemId()));
    }

    [Theory]
    [InlineData("awp")]
    [InlineData("pick:CT:Primary:weapon_ak47")]
    [InlineData("pick:XX:Primary:weapon_ak47:FullBuy")]
    [InlineData("pick:CT:Knife:weapon_ak47:FullBuy")]
    [InlineData("pick:7:Primary:weapon_ak47:FullBuy")]
    [InlineData("pick:CT:Primary::FullBuy")]
    [InlineData("other:CT:Primary:weapon_ak47:FullBuy")]
    public void Parse_RejectsMalformedIds(string itemId) => Assert.Null(WeaponMenuSelection.Parse(itemId));

    [Fact]
    public void IsAllowed_RejectsStaleOrForbiddenWeapons()
    {
        var definitions = new[] { FullBuy };
        Assert.True(WeaponMenu.IsAllowed(new WeaponMenuSelection(TeamSide.T, "FullBuy", WeaponSlot.Primary, "weapon_m4a1_silencer"), definitions));
        Assert.False(WeaponMenu.IsAllowed(new WeaponMenuSelection(TeamSide.T, "FullBuy", WeaponSlot.Primary, "weapon_famas"), definitions));
        Assert.False(WeaponMenu.IsAllowed(new WeaponMenuSelection(TeamSide.T, "FullBuy", WeaponSlot.Primary, "weapon_awp"), definitions));
        Assert.False(WeaponMenu.IsAllowed(new WeaponMenuSelection(TeamSide.T, "FullBuy", WeaponSlot.Secondary, "weapon_ak47"), definitions));
        Assert.False(WeaponMenu.IsAllowed(new WeaponMenuSelection(TeamSide.T, "Deleted", WeaponSlot.Primary, "weapon_ak47"), definitions));
    }

    [Theory]
    [InlineData(RoundPhase.FreezeTime, true, TeamSide.CT, "FullBuy", true)]
    [InlineData(RoundPhase.Live, true, TeamSide.CT, "FullBuy", false)]
    [InlineData(RoundPhase.FreezeTime, false, TeamSide.CT, "FullBuy", false)]
    [InlineData(RoundPhase.FreezeTime, true, TeamSide.T, "FullBuy", false)]
    [InlineData(RoundPhase.FreezeTime, true, TeamSide.CT, "Mid", false)]
    [InlineData(RoundPhase.Warmup, true, TeamSide.CT, "FullBuy", false)]
    public void AppliesNow_OnlyDuringFreezeTimeForTheOwnCurrentConfig(RoundPhase phase, bool alive, TeamSide side, string current, bool expected)
    {
        var selection = new WeaponMenuSelection(TeamSide.CT, "FullBuy", WeaponSlot.Primary, "weapon_m4a1_silencer");
        Assert.Equal(expected, WeaponMenu.AppliesNow(selection, phase, alive, side, current));
    }

    [Theory]
    [InlineData("weapon_m4a1_silencer", "M4A1-S")]
    [InlineData("weapon_custom", "CUSTOM")]
    [InlineData(null, "-")]
    public void WeaponNames_AreReadable(string? id, string expected) => Assert.Equal(expected, WeaponNames.Display(id));
}
```

Ajouter à `tests/RetakeV4.Domain.Tests/Loadouts/LoadoutPlannerTests.cs` (dans la classe existante, avec `using RetakeV4.Domain.Common;` si absent) :
```csharp
    [Fact]
    public void WithWeapons_KeepsTheAwpAndTheRestOfTheLoadout()
    {
        var definition = new RoundTypeDefinition(
            "FullBuy",
            ArmorKind.KevlarHelmet,
            new TeamWeapons(new[] { "weapon_ak47", "weapon_galilar" }, Array.Empty<string>(), Array.Empty<string>()),
            new TeamWeapons(new[] { "weapon_deagle" }, Array.Empty<string>(), Array.Empty<string>()),
            new TeamDefault("weapon_ak47", "weapon_glock"),
            new TeamDefault("weapon_m4a1", "weapon_usp_silencer"),
            new AwpSettings(true, 1, 0, 1.0),
            new DefuseKitSettings(DefuseKitMode.All, 0, 1.0, false),
            new ZeusSettings(false, 0),
            "default");
        var request = new LoadoutRequest(new PlayerId(3), TeamSide.T, new LoadoutPreference("weapon_galilar", "weapon_deagle", true));
        var withAwp = new Loadout(WeaponCatalog.Awp, "weapon_glock", ArmorKind.KevlarHelmet, false, true, new[] { "weapon_flashbang" });
        var adjusted = LoadoutPlanner.WithWeapons(withAwp, definition, request);
        Assert.Equal(withAwp with { Secondary = "weapon_deagle" }, adjusted);
        var withRifle = withAwp with { Primary = "weapon_ak47" };
        Assert.Equal("weapon_galilar", LoadoutPlanner.WithWeapons(withRifle, definition, request).Primary);
    }
```

Ajouter à `tests/RetakeV4.Domain.Tests/Preferences/PreferenceBookTests.cs` :
```csharp
    [Fact]
    public void SetWeapon_ChangesOneSlot_AndKeepsTheOther()
    {
        var book = Loaded(Alice, Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1", "weapon_deagle"));
        var (after, change) = book.SetWeapon(Alice, TeamSide.CT, "FullBuy", WeaponSlot.Primary, "weapon_aug");
        Assert.Equal(new PreferenceKey(Alice, TeamSide.CT, "FullBuy"), change.Key);
        Assert.Equal(new LoadoutPreference("weapon_aug", "weapon_deagle", false), change.Preference);
        Assert.Equal("weapon_aug", after.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
        var (fresh, secondary) = PreferenceBook.Empty.SetWeapon(Bob, TeamSide.T, "Pistol", WeaponSlot.Secondary, "weapon_tec9");
        Assert.Equal(new LoadoutPreference(null, "weapon_tec9", false), secondary.Preference);
        Assert.Equal("weapon_tec9", fresh.RequestFor(Bob, TeamSide.T, "Pistol")?.Secondary);
    }
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`WeaponMenu`, `WeaponNames`, `WeaponSlot`, `SetWeapon`, `WithWeapons` introuvables).

- [ ] **Step 2: Implémentation**

`src/RetakeV4.Domain/Loadouts/WeaponNames.cs`
```csharp
namespace RetakeV4.Domain.Loadouts;

public static class WeaponNames
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["weapon_ak47"] = "AK-47",
        ["weapon_m4a1"] = "M4A4",
        ["weapon_m4a1_silencer"] = "M4A1-S",
        ["weapon_famas"] = "FAMAS",
        ["weapon_galilar"] = "Galil AR",
        ["weapon_aug"] = "AUG",
        ["weapon_sg556"] = "SG 553",
        ["weapon_awp"] = "AWP",
        ["weapon_ssg08"] = "SSG 08",
        ["weapon_scar20"] = "SCAR-20",
        ["weapon_g3sg1"] = "G3SG1",
        ["weapon_mp9"] = "MP9",
        ["weapon_mac10"] = "MAC-10",
        ["weapon_mp7"] = "MP7",
        ["weapon_mp5sd"] = "MP5-SD",
        ["weapon_ump45"] = "UMP-45",
        ["weapon_p90"] = "P90",
        ["weapon_bizon"] = "PP-Bizon",
        ["weapon_nova"] = "Nova",
        ["weapon_xm1014"] = "XM1014",
        ["weapon_mag7"] = "MAG-7",
        ["weapon_sawedoff"] = "Sawed-Off",
        ["weapon_m249"] = "M249",
        ["weapon_negev"] = "Negev",
        ["weapon_glock"] = "Glock-18",
        ["weapon_hkp2000"] = "P2000",
        ["weapon_usp_silencer"] = "USP-S",
        ["weapon_p250"] = "P250",
        ["weapon_fiveseven"] = "Five-SeveN",
        ["weapon_tec9"] = "Tec-9",
        ["weapon_cz75a"] = "CZ75-Auto",
        ["weapon_deagle"] = "Desert Eagle",
        ["weapon_revolver"] = "R8 Revolver",
        ["weapon_elite"] = "Dual Berettas",
    };

    public static string Display(string? weaponId) => weaponId switch
    {
        null => "-",
        _ when Names.TryGetValue(weaponId, out var name) => name,
        _ => weaponId.Replace("weapon_", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
    };
}
```

`src/RetakeV4.Domain/Loadouts/WeaponMenu.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Loadouts;

public enum WeaponSlot
{
    Primary,
    Secondary,
}

public sealed record WeaponMenuSelection(TeamSide Team, string RoundType, WeaponSlot Slot, string Weapon)
{
    private const string Prefix = "pick";

    // The round type goes last: it is free text from roundtypes.json and may contain ':'.
    public string ToItemId() => $"{Prefix}:{Team}:{Slot}:{Weapon}:{RoundType}";

    public static WeaponMenuSelection? Parse(string itemId)
    {
        var parts = itemId.Split(':', 5);
        if (parts.Length != 5 || parts[0] != Prefix || parts[3].Length == 0 || parts[4].Length == 0)
        {
            return null;
        }
        if (!Enum.TryParse<TeamSide>(parts[1], out var team) || !Enum.IsDefined(team)
            || !Enum.TryParse<WeaponSlot>(parts[2], out var slot) || !Enum.IsDefined(slot))
        {
            return null;
        }
        return new WeaponMenuSelection(team, parts[4], slot, parts[3]);
    }
}

public sealed record WeaponMenuState(
    IReadOnlyList<RoundTypeDefinition> Definitions,
    RoundTypeDefinition? Current,
    TeamSide? Team,
    Func<TeamSide, string, LoadoutPreference?> PreferenceFor,
    bool AwpOptIn);

public static class WeaponMenu
{
    public const string MenuId = "allocation.weapons";
    public const string AwpItemId = "awp";
    public const string CurrentItemId = "current";
    public const string OthersItemId = "others";

    private static readonly TeamSide[] Sides = { TeamSide.T, TeamSide.CT };

    // The AWP is never a regular choice: it is only handed out to volunteers.
    public static IReadOnlyList<string> Options(RoundTypeDefinition definition, TeamSide team, WeaponSlot slot)
    {
        var primary = slot == WeaponSlot.Primary;
        var pool = primary ? definition.Primaries.For(team) : definition.Secondaries.For(team);
        var fallback = primary ? definition.DefaultFor(team).Primary : definition.DefaultFor(team).Secondary;
        return new[] { fallback }
            .Concat(pool)
            .OfType<string>()
            .Where(w => (primary ? WeaponCatalog.IsPrimary(w) : WeaponCatalog.IsSecondary(w)) && w != WeaponCatalog.Awp)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static bool HasChoice(RoundTypeDefinition definition, TeamSide team) =>
        Options(definition, team, WeaponSlot.Primary).Count > 1 || Options(definition, team, WeaponSlot.Secondary).Count > 1;

    public static bool IsAllowed(WeaponMenuSelection selection, IReadOnlyList<RoundTypeDefinition> definitions) =>
        definitions.FirstOrDefault(d => d.Name == selection.RoundType) is { } definition
        && Options(definition, selection.Team, selection.Slot).Contains(selection.Weapon, StringComparer.Ordinal);

    // The current loadout is only replaced during freeze time, for a living player choosing for his own team and the current round type.
    public static bool AppliesNow(WeaponMenuSelection selection, RoundPhase phase, bool alive, TeamSide? side, string? currentRoundType) =>
        phase == RoundPhase.FreezeTime && alive && side == selection.Team && selection.RoundType == currentRoundType;

    public static Menu Build(WeaponMenuState state)
    {
        var items = new List<MenuItem>();
        if (state.Team is { } team && state.Current is { } current && HasChoice(current, team))
        {
            items.Add(new MenuItem(
                CurrentItemId,
                HudText.Of("allocation.menu.current", current.Name, team.ToString()),
                MenuItemKind.Submenu,
                Submenu: ConfigMenu(state, current, team)));
        }
        var others = state.Definitions
            .SelectMany(d => Sides.Select(side => (Definition: d, Side: side)))
            .Where(c => HasChoice(c.Definition, c.Side) && !(c.Side == state.Team && c.Definition.Name == state.Current?.Name))
            .Select(c => new MenuItem(
                ConfigId(c.Side, c.Definition.Name),
                HudText.Of("allocation.menu.config", c.Definition.Name, c.Side.ToString()),
                MenuItemKind.Submenu,
                Submenu: ConfigMenu(state, c.Definition, c.Side)))
            .ToList();
        if (others.Count > 0)
        {
            items.Add(new MenuItem(
                OthersItemId,
                HudText.Of("allocation.menu.others"),
                MenuItemKind.Submenu,
                Submenu: new Menu(OthersItemId, HudText.Of("allocation.menu.others"), others)));
        }
        items.Add(new MenuItem(AwpItemId, HudText.Of("allocation.menu.awp"), MenuItemKind.Toggle, IsOn: state.AwpOptIn));
        return new Menu(MenuId, HudText.Of("allocation.menu.title"), items);
    }

    private static Menu ConfigMenu(WeaponMenuState state, RoundTypeDefinition definition, TeamSide team)
    {
        var request = new LoadoutRequest(new PlayerId(0), team, state.PreferenceFor(team, definition.Name));
        var (primary, secondary) = LoadoutPlanner.ResolveWeapons(definition, request);
        var items = new[]
            {
                SlotItem(definition, team, WeaponSlot.Primary, primary, "allocation.menu.primary"),
                SlotItem(definition, team, WeaponSlot.Secondary, secondary, "allocation.menu.secondary"),
            }
            .OfType<MenuItem>()
            .ToList();
        return new Menu(ConfigId(team, definition.Name), HudText.Of("allocation.menu.config", definition.Name, team.ToString()), items);
    }

    private static MenuItem? SlotItem(RoundTypeDefinition definition, TeamSide team, WeaponSlot slot, string? effective, string labelKey)
    {
        var options = Options(definition, team, slot);
        if (options.Count < 2)
        {
            return null;
        }
        var label = HudText.Of(labelKey, WeaponNames.Display(effective));
        var choices = options
            .Select(w => new MenuItem(
                new WeaponMenuSelection(team, definition.Name, slot, w).ToItemId(),
                HudText.Raw(WeaponNames.Display(w)),
                MenuItemKind.Choice,
                IsOn: w == effective))
            .ToList();
        var id = slot.ToString().ToLowerInvariant();
        return new MenuItem(id, label, MenuItemKind.Submenu, Submenu: new Menu($"{ConfigId(team, definition.Name)}:{id}", label, choices));
    }

    private static string ConfigId(TeamSide team, string roundType) => $"cfg:{team}:{roundType}";
}
```

Dans `src/RetakeV4.Domain/Loadouts/LoadoutPlanner.cs`, ajouter après `ResolveWeapons` :
```csharp
    // A freeze-time weapon change keeps what was already handed out (AWP, armor, kit, Zeus, grenades) and only swaps the guns.
    public static Loadout WithWeapons(Loadout current, RoundTypeDefinition definition, LoadoutRequest request)
    {
        var (primary, secondary) = ResolveWeapons(definition, request);
        return current with { Primary = current.Primary == WeaponCatalog.Awp ? WeaponCatalog.Awp : primary, Secondary = secondary };
    }
```

Dans `src/RetakeV4.Domain/Preferences/PreferenceBook.cs`, ajouter après `With` :
```csharp
    public (PreferenceBook Book, StoredPreference Change) SetWeapon(ulong steamId, TeamSide team, string roundType, WeaponSlot slot, string weapon)
    {
        var key = new PreferenceKey(steamId, team, roundType);
        var current = Entries.GetValueOrDefault(key) ?? Nothing;
        var change = new StoredPreference(key, slot == WeaponSlot.Primary ? current with { Primary = weapon } : current with { Secondary = weapon });
        return (With(change), change);
    }
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4.Domain/Loadouts src/RetakeV4.Domain/Preferences tests/RetakeV4.Domain.Tests/Loadouts tests/RetakeV4.Domain.Tests/Preferences
git commit -m "feat: menu d'armes, sélection et changement d'arme pendant le freeze time (Domain)"
```

---

### Task 3: Widgets centrés et composition HTML (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Hud/HudLine.cs`, `RoundInfoWidget.cs`, `QueueStatusWidget.cs`, `AlertsWidget.cs`, `CenterComposer.cs`, `CenterHtml.cs` (tous sous `src/RetakeV4.Domain/Hud/`)
- Test: `tests/RetakeV4.Domain.Tests/Hud/CenterWidgetsTests.cs`

**Interfaces:**
- Consumes: `HudText` (Task 1) ; `PlayerId`, `BombSite`, `TeamState`, `QueuedPlayer`.
- Produces:
  - `enum HudStyle { Text, Accent, Muted }`, `sealed record HudLine(HudText Text, HudStyle Style)`, `sealed record HudTheme(string Accent, string Text, string Muted)` avec `string ColorOf(HudStyle)`.
  - `sealed record RoundInfo(string RoundType, BombSite? Site, int CtCount, int TCount)`, `sealed record RoundInfoWidget(RoundInfo? Info, DateTimeOffset ShownUntil, int TWinStreak)` : `Empty`, `Show(RoundInfo, DateTimeOffset now, TimeSpan duration)`, `WithStreak(int)`, `Render(DateTimeOffset now)`.
  - `sealed record QueueStatusWidget(IReadOnlyList<QueuedPlayer> Ordered)` : `Empty`, `From(TeamState)`, `Render(PlayerId)`.
  - `sealed record HudAlertEntry(PlayerId? Target, HudText Text, DateTimeOffset Until)`, `sealed record AlertsWidget(ImmutableList<HudAlertEntry> Entries)` : `MaxVisible = 3`, `Empty`, `Push(PlayerId? target, HudText text, DateTimeOffset now, TimeSpan duration)`, `Render(PlayerId, DateTimeOffset now)`.
  - `static CenterComposer.Compose(IEnumerable<(int Priority, IReadOnlyList<HudLine> Lines)> blocks, int maxLines)` → `IReadOnlyList<HudLine>`.
  - `static CenterHtml.Format(IReadOnlyList<(string Text, HudStyle Style)> lines, HudTheme theme)` → `string?` (null si vide).

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Hud/CenterWidgetsTests.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Hud;

public class CenterWidgetsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly PlayerId Alice = new(1);
    private static readonly PlayerId Bob = new(2);
    private static readonly HudTheme Theme = new("#4FC3F7", "#FFFFFF", "#9E9E9E");

    [Fact]
    public void RoundInfo_IsShownForItsDuration()
    {
        var widget = RoundInfoWidget.Empty.Show(new RoundInfo("FullBuy", BombSite.B, 4, 3), Now, TimeSpan.FromSeconds(6));
        var lines = widget.Render(Now.AddSeconds(5));
        Assert.Equal(new object[] { "FullBuy", "B" }, lines[0].Text.Args);
        Assert.Equal(HudStyle.Accent, lines[0].Style);
        Assert.Equal(new object[] { 4, 3 }, lines[1].Text.Args);
        Assert.Equal(2, lines.Count);
        Assert.Empty(widget.Render(Now.AddSeconds(6)));
        Assert.Empty(RoundInfoWidget.Empty.Render(Now));
    }

    [Fact]
    public void RoundInfo_ShowsTheStreak_AndAnUnknownSite()
    {
        var widget = RoundInfoWidget.Empty.WithStreak(2).Show(new RoundInfo("Pistol", null, 1, 1), Now, TimeSpan.FromSeconds(6));
        var lines = widget.Render(Now);
        Assert.Equal("?", lines[0].Text.Args[1]);
        Assert.Equal("hud.round.streak", lines[2].Text.Key);
        Assert.Equal(HudStyle.Muted, lines[2].Style);
    }

    [Fact]
    public void QueueStatus_ShowsThePositionAndPriority()
    {
        var state = TeamState.Empty with
        {
            Queue = new[] { new QueuedPlayer(Alice, 0, 1), new QueuedPlayer(Bob, 1, 2) },
        };
        var widget = QueueStatusWidget.From(state);
        var bob = widget.Render(Bob);
        Assert.Equal(new object[] { 1, 2 }, bob[0].Text.Args);
        Assert.Equal("hud.queue.priority", bob[1].Text.Key);
        var alice = Assert.Single(widget.Render(Alice));
        Assert.Equal(new object[] { 2, 2 }, alice.Text.Args);
        Assert.Empty(widget.Render(new PlayerId(9)));
    }

    [Fact]
    public void Alerts_AreTargeted_Expire_AndKeepTheNewest()
    {
        var widget = AlertsWidget.Empty
            .Push(Alice, HudText.Raw("a1"), Now, TimeSpan.FromSeconds(4))
            .Push(null, HudText.Raw("all"), Now, TimeSpan.FromSeconds(4))
            .Push(Bob, HudText.Raw("b1"), Now, TimeSpan.FromSeconds(4))
            .Push(Alice, HudText.Raw("a2"), Now, TimeSpan.FromSeconds(4))
            .Push(Alice, HudText.Raw("a3"), Now, TimeSpan.FromSeconds(4));
        Assert.Equal(new[] { "all", "a2", "a3" }, widget.Render(Alice, Now).Select(l => l.Text.Literal));
        Assert.Equal(new[] { "all", "b1" }, widget.Render(Bob, Now).Select(l => l.Text.Literal));
        Assert.Empty(widget.Render(Alice, Now.AddSeconds(4)));
    }

    [Fact]
    public void Alerts_DropExpiredEntriesWhenPushing()
    {
        var widget = AlertsWidget.Empty
            .Push(Alice, HudText.Raw("old"), Now, TimeSpan.FromSeconds(1))
            .Push(Alice, HudText.Raw("new"), Now.AddSeconds(2), TimeSpan.FromSeconds(4));
        Assert.Equal("new", Assert.Single(widget.Entries).Text.Literal);
    }

    [Fact]
    public void Compose_OrdersByPriority_SkipsEmptyBlocks_AndCapsTheLines()
    {
        IReadOnlyList<HudLine> Lines(params string[] texts) => texts.Select(t => new HudLine(HudText.Raw(t), HudStyle.Text)).ToList();
        var composed = CenterComposer.Compose(new[] { (10, Lines("r1", "r2")), (30, Lines("a1")), (20, Lines()) }, 2);
        Assert.Equal(new[] { "a1", "r1" }, composed.Select(l => l.Text.Literal));
    }

    [Fact]
    public void Format_EscapesHtml_AndColorsEachLine()
    {
        var html = CenterHtml.Format(new[] { ("<b>Full</b> & co", HudStyle.Accent), ("CT 4", HudStyle.Muted) }, Theme);
        Assert.Equal("<font color='#4FC3F7'>&lt;b&gt;Full&lt;/b&gt; &amp; co</font><br><font color='#9E9E9E'>CT 4</font>", html);
        Assert.Null(CenterHtml.Format(Array.Empty<(string, HudStyle)>(), Theme));
    }
}
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`RoundInfoWidget`, `CenterHtml`… introuvables).

- [ ] **Step 2: Implémentation**

`src/RetakeV4.Domain/Hud/HudLine.cs`
```csharp
namespace RetakeV4.Domain.Hud;

public enum HudStyle
{
    Text,
    Accent,
    Muted,
}

public sealed record HudLine(HudText Text, HudStyle Style);

public sealed record HudTheme(string Accent, string Text, string Muted)
{
    public string ColorOf(HudStyle style) => style switch
    {
        HudStyle.Accent => Accent,
        HudStyle.Muted => Muted,
        _ => Text,
    };
}
```

`src/RetakeV4.Domain/Hud/RoundInfoWidget.cs`
```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Hud;

public sealed record RoundInfo(string RoundType, BombSite? Site, int CtCount, int TCount);

public sealed record RoundInfoWidget(RoundInfo? Info, DateTimeOffset ShownUntil, int TWinStreak)
{
    public static RoundInfoWidget Empty { get; } = new(null, DateTimeOffset.MinValue, 0);

    public RoundInfoWidget Show(RoundInfo info, DateTimeOffset now, TimeSpan duration) =>
        this with { Info = info, ShownUntil = now + duration };

    public RoundInfoWidget WithStreak(int tWinStreak) => this with { TWinStreak = tWinStreak };

    public IReadOnlyList<HudLine> Render(DateTimeOffset now)
    {
        if (Info is null || now >= ShownUntil)
        {
            return Array.Empty<HudLine>();
        }
        var lines = new List<HudLine>
        {
            new(HudText.Of("hud.round.title", Info.RoundType, Info.Site?.ToString() ?? "?"), HudStyle.Accent),
            new(HudText.Of("hud.round.teams", Info.CtCount, Info.TCount), HudStyle.Text),
        };
        if (TWinStreak > 0)
        {
            lines.Add(new HudLine(HudText.Of("hud.round.streak", TWinStreak), HudStyle.Muted));
        }
        return lines;
    }
}
```

`src/RetakeV4.Domain/Hud/QueueStatusWidget.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Hud;

public sealed record QueueStatusWidget(IReadOnlyList<QueuedPlayer> Ordered)
{
    public static QueueStatusWidget Empty { get; } = new(Array.Empty<QueuedPlayer>());

    public static QueueStatusWidget From(TeamState state) => new(state.OrderedQueue());

    public IReadOnlyList<HudLine> Render(PlayerId player)
    {
        var index = Ordered.Select(q => q.Player).ToList().IndexOf(player);
        if (index < 0)
        {
            return Array.Empty<HudLine>();
        }
        var position = new HudLine(HudText.Of("hud.queue.position", index + 1, Ordered.Count), HudStyle.Accent);
        return Ordered[index].Priority > 0
            ? new[] { position, new HudLine(HudText.Of("hud.queue.priority"), HudStyle.Muted) }
            : new[] { position };
    }
}
```

`src/RetakeV4.Domain/Hud/AlertsWidget.cs`
```csharp
using System.Collections.Immutable;
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Hud;

public sealed record HudAlertEntry(PlayerId? Target, HudText Text, DateTimeOffset Until);

public sealed record AlertsWidget(ImmutableList<HudAlertEntry> Entries)
{
    public const int MaxVisible = 3;
    private const int MaxStored = 64;

    public static AlertsWidget Empty { get; } = new(ImmutableList<HudAlertEntry>.Empty);

    public AlertsWidget Push(PlayerId? target, HudText text, DateTimeOffset now, TimeSpan duration)
    {
        var live = Entries.RemoveAll(e => e.Until <= now).Add(new HudAlertEntry(target, text, now + duration));
        return new AlertsWidget(live.Count > MaxStored ? live.RemoveRange(0, live.Count - MaxStored) : live);
    }

    public IReadOnlyList<HudLine> Render(PlayerId player, DateTimeOffset now) =>
        Entries
            .Where(e => e.Until > now && (e.Target is null || e.Target == player))
            .TakeLast(MaxVisible)
            .Select(e => new HudLine(e.Text, HudStyle.Accent))
            .ToList();
}
```

`src/RetakeV4.Domain/Hud/CenterComposer.cs`
```csharp
namespace RetakeV4.Domain.Hud;

public static class CenterComposer
{
    public static IReadOnlyList<HudLine> Compose(IEnumerable<(int Priority, IReadOnlyList<HudLine> Lines)> blocks, int maxLines) =>
        blocks
            .Where(b => b.Lines.Count > 0)
            .OrderByDescending(b => b.Priority)
            .SelectMany(b => b.Lines)
            .Take(Math.Max(0, maxLines))
            .ToList();
}
```

`src/RetakeV4.Domain/Hud/CenterHtml.cs`
```csharp
using System.Net;

namespace RetakeV4.Domain.Hud;

public static class CenterHtml
{
    // Texts come from lang files and config (round type names): always escaped. Colors are validated #RRGGBB values.
    public static string? Format(IReadOnlyList<(string Text, HudStyle Style)> lines, HudTheme theme) =>
        lines.Count == 0
            ? null
            : string.Join("<br>", lines.Select(l => $"<font color='{theme.ColorOf(l.Style)}'>{WebUtility.HtmlEncode(l.Text)}</font>"));
}
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4.Domain/Hud tests/RetakeV4.Domain.Tests/Hud
git commit -m "feat: widgets centrés (round, file, alertes) et composition HTML (Domain)"
```

---

### Task 4: Contrats de bus du HUD et publication des types de round et des équipes

**Files:**
- Create: `src/RetakeV4.Domain/Events/HudEvents.cs`
- Modify: `src/RetakeV4.Domain/Events/RoundEvents.cs`, `src/RetakeV4/Modules/RoundTypes/RoundTypesModule.cs`, `src/RetakeV4/Modules/Teams/TeamsModule.cs`
- Test: `tests/RetakeV4.Domain.Tests/Events/EventBusTests.cs`

**Interfaces:**
- Consumes: `Menu`, `HudText` (Task 1) ; `RoundTypeDefinition`, `TeamState`, `ModulesReady`.
- Produces:
  - `sealed record HudMenuOpen(PlayerId Player, Menu Menu, bool RefreshOnly = false)` — `RefreshOnly` : ne remplace que si ce menu est encore ouvert.
  - `sealed record HudMenuClose(PlayerId Player, string MenuId)`.
  - `sealed record HudMenuSelected(PlayerId Player, string MenuId, string ItemId)`.
  - `sealed record HudAlert(PlayerId? Player, HudText Text)` (`Player` null = tout le monde).
  - `sealed record RoundTypesLoaded(IReadOnlyList<RoundTypeDefinition> Definitions)` — publié par RoundTypes sur `ModulesReady`.
  - `sealed record TeamStateChanged(TeamState State)` — publié par Teams à chaque changement d'état et sur `ModulesReady`.

- [ ] **Step 1: Écrire le test qui échoue**

Le module Hud republie un menu (`HudMenuOpen`) depuis un handler de `HudMenuSelected` : le bus doit livrer une publication imbriquée. Ajouter à `tests/RetakeV4.Domain.Tests/Events/EventBusTests.cs` (avec `using RetakeV4.Domain.Common;` et `using RetakeV4.Domain.Hud;`) :
```csharp
    [Fact]
    public void Publish_FromAHandler_ReachesOtherSubscribers()
    {
        var bus = new EventBus(_ => { });
        var menu = new Menu("m", HudText.Raw("t"), Array.Empty<MenuItem>());
        var refreshed = new List<HudMenuOpen>();
        bus.Subscribe<HudMenuSelected>("allocation", e => bus.Publish(new HudMenuOpen(e.Player, menu, RefreshOnly: true)));
        bus.Subscribe<HudMenuOpen>("hud", refreshed.Add);
        bus.Publish(new HudMenuSelected(new PlayerId(4), "m", "awp"));
        var open = Assert.Single(refreshed);
        Assert.Equal(new PlayerId(4), open.Player);
        Assert.True(open.RefreshOnly);
    }
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`HudMenuSelected`, `HudMenuOpen` introuvables).

- [ ] **Step 2: Implémentation**

`src/RetakeV4.Domain/Events/HudEvents.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Events;

// RefreshOnly: replace the menu only if the player still has this menu open (never reopens a closed menu).
public sealed record HudMenuOpen(PlayerId Player, Menu Menu, bool RefreshOnly = false);

public sealed record HudMenuClose(PlayerId Player, string MenuId);

public sealed record HudMenuSelected(PlayerId Player, string MenuId, string ItemId);

// Player null: shown to everyone.
public sealed record HudAlert(PlayerId? Player, HudText Text);
```

Dans `src/RetakeV4.Domain/Events/RoundEvents.cs`, ajouter `using RetakeV4.Domain.Loadouts;` et `using RetakeV4.Domain.Teams;` puis :
```csharp
public sealed record RoundTypesLoaded(IReadOnlyList<RoundTypeDefinition> Definitions);

public sealed record TeamStateChanged(TeamState State);
```

Dans `src/RetakeV4/Modules/RoundTypes/RoundTypesModule.cs`, remplacer la première ligne de `Load` par :
```csharp
        var definitions = _config.ToDefinitions();
        context.Hooks.PreparationStep(new RoundTypeStep(_config.ToRules(), definitions, SystemRandom.Shared));
        context.Hooks.OnBus<ModulesReady>(_ => context.Bus.Publish(new RoundTypesLoaded(definitions.Values.ToList())));
```

Dans `src/RetakeV4/Modules/Teams/TeamsModule.cs` :
- ajouter la méthode :
```csharp
    private void SetState(TeamState state)
    {
        _state = state;
        _context?.Bus.Publish(new TeamStateChanged(state));
    }
```
- remplacer chaque affectation `_state = <expr>;` des méthodes (lignes 59, 84, 89, 168, 180, 185, 237 ; pas l'initialiseur du champ) par `SetState(<expr>);` ;
- dans `Load`, après `hooks.OnBus<RoundPhaseChanged>(OnPhaseChanged);`, ajouter :
```csharp
        hooks.OnBus<ModulesReady>(_ => Context.Bus.Publish(new TeamStateChanged(_state)));
```

Vérifier : `grep -n "_state = " src/RetakeV4/Modules/Teams/TeamsModule.cs` ne renvoie plus que l'initialiseur du champ et la ligne de `SetState`.

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4.Domain/Events src/RetakeV4/Modules/RoundTypes/RoundTypesModule.cs src/RetakeV4/Modules/Teams/TeamsModule.cs tests/RetakeV4.Domain.Tests/Events/EventBusTests.cs
git commit -m "feat: contrats de bus du HUD, publication des types de round et de l'état des équipes"
```

---

### Task 5: Configuration `hud.json` et textes du HUD

**Files:**
- Create: `src/RetakeV4/Modules/Hud/HudConfig.cs`, `src/RetakeV4/Modules/Hud/HudConfigValidator.cs`
- Modify: `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`
- Test: `tests/RetakeV4.Integration.Tests/Modules/Hud/HudConfigValidatorTests.cs`, `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs`

**Interfaces:**
- Consumes: `HudTheme`, `MenuInputSetting` (Tasks 1, 3) ; `ModuleConfig`, `IConfigValidator<T>`, `ConfigIssue`, `ValidationResult<T>`.
- Produces:
  - `enum MenuFollowMode { Tick, Parent }`.
  - `HudThemeConfig { Accent "#4FC3F7", Text "#FFFFFF", Muted "#9E9E9E", FontSize 24 }` avec `ToTheme()`.
  - `HudWidgetConfig { Enabled true, ShowSeconds 6 }`, `HudWidgetsConfig { RoundInfo, QueueStatus, Alerts (ShowSeconds 4) }`.
  - `HudMenuConfig { Input AimAndKeys, DistanceUnits 60, LineHeightUnits 4, HalfWidthUnits 18, WorldUnitsPerPx 0.1, Orientation 0, FollowMode Tick, BlockAttackWhileAiming true }`.
  - `HudConfig : ModuleConfig { Version 1, Theme, CenterRefreshMs 250, CenterMaxLines 6, Widgets, Menu }`, `HudConfigValidator`.
  - Clés lang `hud.menu.*`, `hud.round.*`, `hud.queue.*`, `allocation.menu.*`, `allocation.awp.received`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/Modules/Hud/HudConfigValidatorTests.cs`
```csharp
using RetakeV4.Domain.Hud;
using RetakeV4.Modules.Hud;

namespace RetakeV4.Integration.Tests.Modules.Hud;

public class HudConfigValidatorTests
{
    private static readonly HudConfig Defaults = new();
    private readonly HudConfigValidator _validator = new();

    private ValidationResultView Validate(HudConfig config)
    {
        var result = _validator.Validate(config, Defaults, "hud.json");
        return new ValidationResultView(result.Config, result.Issues.Select(i => i.Key).ToList());
    }

    private sealed record ValidationResultView(HudConfig Config, IReadOnlyList<string> Keys);

    [Fact]
    public void Defaults_AreValid() => Assert.Empty(Validate(Defaults).Keys);

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#12345G")]
    [InlineData("#FFFFFF' onload='x")]
    public void InvalidColor_FallsBackToTheDefault(string color)
    {
        var result = Validate(Defaults with { Theme = Defaults.Theme with { Accent = color } });
        Assert.Equal("#4FC3F7", result.Config.Theme.Accent);
        Assert.Equal(new[] { "Theme.Accent" }, result.Keys);
    }

    [Fact]
    public void OutOfRangeValues_FallBack()
    {
        var config = Defaults with
        {
            CenterRefreshMs = 10,
            Menu = Defaults.Menu with { DistanceUnits = 0f, Orientation = 3, WorldUnitsPerPx = float.NaN },
            Widgets = Defaults.Widgets with { Alerts = Defaults.Widgets.Alerts with { ShowSeconds = 0 } },
        };
        var result = Validate(config);
        Assert.Equal(250, result.Config.CenterRefreshMs);
        Assert.Equal(60f, result.Config.Menu.DistanceUnits);
        Assert.Equal(0, result.Config.Menu.Orientation);
        Assert.Equal(0.1f, result.Config.Menu.WorldUnitsPerPx);
        Assert.Equal(4, result.Config.Widgets.Alerts.ShowSeconds);
        Assert.Equal(5, result.Keys.Count);
    }

    [Fact]
    public void UndefinedEnums_FallBack()
    {
        var result = Validate(Defaults with { Menu = Defaults.Menu with { Input = (MenuInputSetting)7, FollowMode = (MenuFollowMode)9 } });
        Assert.Equal(MenuInputSetting.AimAndKeys, result.Config.Menu.Input);
        Assert.Equal(MenuFollowMode.Tick, result.Config.Menu.FollowMode);
        Assert.Equal(new[] { "Menu.Input", "Menu.FollowMode" }, result.Keys);
    }

    [Fact]
    public void MissingSections_FallBackToDefaults()
    {
        var result = Validate(Defaults with { Theme = null!, Widgets = null!, Menu = null! });
        Assert.Equal(Defaults.Theme, result.Config.Theme);
        Assert.Equal(Defaults.Widgets, result.Config.Widgets);
        Assert.Equal(Defaults.Menu, result.Config.Menu);
        Assert.Equal(3, result.Keys.Count);
    }

    [Fact]
    public void MissingWidget_FallsBackToItsDefault()
    {
        var result = Validate(Defaults with { Widgets = Defaults.Widgets with { QueueStatus = null! } });
        Assert.True(result.Config.Widgets.QueueStatus.Enabled);
        Assert.Equal(new[] { "Widgets.QueueStatus" }, result.Keys);
    }
}
```

Ajouter à `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs` (dans la classe) :
```csharp
    [Theory]
    [InlineData("hud.menu.back")]
    [InlineData("hud.menu.close")]
    [InlineData("hud.menu.next")]
    [InlineData("hud.menu.previous")]
    [InlineData("hud.menu.on")]
    [InlineData("hud.menu.off")]
    [InlineData("hud.round.title")]
    [InlineData("hud.round.teams")]
    [InlineData("hud.round.streak")]
    [InlineData("hud.queue.position")]
    [InlineData("hud.queue.priority")]
    [InlineData("allocation.menu.title")]
    [InlineData("allocation.menu.current")]
    [InlineData("allocation.menu.others")]
    [InlineData("allocation.menu.config")]
    [InlineData("allocation.menu.primary")]
    [InlineData("allocation.menu.secondary")]
    [InlineData("allocation.menu.awp")]
    [InlineData("allocation.menu.applied_now")]
    [InlineData("allocation.menu.applied_next_round")]
    [InlineData("allocation.awp.received")]
    public void Phase3bKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`HudConfig`, `HudConfigValidator` introuvables).

- [ ] **Step 2: Implémentation**

`src/RetakeV4/Modules/Hud/HudConfig.cs`
```csharp
using RetakeV4.Configuration;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Hud;

public enum MenuFollowMode
{
    Tick,
    Parent,
}

public sealed record HudThemeConfig
{
    public string Accent { get; init; } = "#4FC3F7";

    public string Text { get; init; } = "#FFFFFF";

    public string Muted { get; init; } = "#9E9E9E";

    public float FontSize { get; init; } = 24f;

    public HudTheme ToTheme() => new(Accent, Text, Muted);
}

public sealed record HudWidgetConfig
{
    public bool Enabled { get; init; } = true;

    public int ShowSeconds { get; init; } = 6;
}

public sealed record HudWidgetsConfig
{
    public HudWidgetConfig RoundInfo { get; init; } = new();

    public HudWidgetConfig QueueStatus { get; init; } = new();

    public HudWidgetConfig Alerts { get; init; } = new() { ShowSeconds = 4 };
}

// Orientation and FollowMode select the formulas tested by the HUD prototype (docs/spikes/hud-probe-findings.md).
public sealed record HudMenuConfig
{
    public MenuInputSetting Input { get; init; } = MenuInputSetting.AimAndKeys;

    public float DistanceUnits { get; init; } = 60f;

    public float LineHeightUnits { get; init; } = 4f;

    public float HalfWidthUnits { get; init; } = 18f;

    public float WorldUnitsPerPx { get; init; } = 0.1f;

    public int Orientation { get; init; }

    public MenuFollowMode FollowMode { get; init; } = MenuFollowMode.Tick;

    public bool BlockAttackWhileAiming { get; init; } = true;
}

public sealed record HudConfig : ModuleConfig
{
    public HudConfig() => Version = 1;

    public HudThemeConfig Theme { get; init; } = new();

    public int CenterRefreshMs { get; init; } = 250;

    public int CenterMaxLines { get; init; } = 6;

    public HudWidgetsConfig Widgets { get; init; } = new();

    public HudMenuConfig Menu { get; init; } = new();
}
```

`src/RetakeV4/Modules/Hud/HudConfigValidator.cs`
```csharp
using System.Text.RegularExpressions;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Hud;

public sealed partial class HudConfigValidator : IConfigValidator<HudConfig>
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    public ValidationResult<HudConfig> Validate(HudConfig config, HudConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var validated = config with
        {
            Theme = ValidateTheme(config.Theme ?? Missing(defaults.Theme, nameof(HudConfig.Theme), file, issues), defaults.Theme, file, issues),
            CenterRefreshMs = InRange(config.CenterRefreshMs, 50, 2000, defaults.CenterRefreshMs, nameof(HudConfig.CenterRefreshMs), file, issues),
            CenterMaxLines = InRange(config.CenterMaxLines, 1, 10, defaults.CenterMaxLines, nameof(HudConfig.CenterMaxLines), file, issues),
            Widgets = ValidateWidgets(config.Widgets ?? Missing(defaults.Widgets, nameof(HudConfig.Widgets), file, issues), defaults.Widgets, file, issues),
            Menu = ValidateMenu(config.Menu ?? Missing(defaults.Menu, nameof(HudConfig.Menu), file, issues), defaults.Menu, file, issues),
        };
        return new ValidationResult<HudConfig>(validated, issues);
    }

    private static HudThemeConfig ValidateTheme(HudThemeConfig theme, HudThemeConfig defaults, string file, List<ConfigIssue> issues) => theme with
    {
        Accent = Color(theme.Accent, defaults.Accent, "Theme.Accent", file, issues),
        Text = Color(theme.Text, defaults.Text, "Theme.Text", file, issues),
        Muted = Color(theme.Muted, defaults.Muted, "Theme.Muted", file, issues),
        FontSize = InRange(theme.FontSize, 8f, 128f, defaults.FontSize, "Theme.FontSize", file, issues),
    };

    private static HudWidgetsConfig ValidateWidgets(HudWidgetsConfig widgets, HudWidgetsConfig defaults, string file, List<ConfigIssue> issues) => widgets with
    {
        RoundInfo = Widget(widgets.RoundInfo, defaults.RoundInfo, "Widgets.RoundInfo", file, issues),
        QueueStatus = Widget(widgets.QueueStatus, defaults.QueueStatus, "Widgets.QueueStatus", file, issues),
        Alerts = Widget(widgets.Alerts, defaults.Alerts, "Widgets.Alerts", file, issues),
    };

    private static HudWidgetConfig Widget(HudWidgetConfig? widget, HudWidgetConfig defaults, string key, string file, List<ConfigIssue> issues)
    {
        var present = widget ?? Missing(defaults, key, file, issues);
        return present with { ShowSeconds = InRange(present.ShowSeconds, 1, 60, defaults.ShowSeconds, $"{key}.ShowSeconds", file, issues) };
    }

    private static HudMenuConfig ValidateMenu(HudMenuConfig menu, HudMenuConfig defaults, string file, List<ConfigIssue> issues) => menu with
    {
        Input = Defined(menu.Input, defaults.Input, "Menu.Input", file, issues),
        FollowMode = Defined(menu.FollowMode, defaults.FollowMode, "Menu.FollowMode", file, issues),
        DistanceUnits = InRange(menu.DistanceUnits, 10f, 200f, defaults.DistanceUnits, "Menu.DistanceUnits", file, issues),
        LineHeightUnits = InRange(menu.LineHeightUnits, 1f, 20f, defaults.LineHeightUnits, "Menu.LineHeightUnits", file, issues),
        HalfWidthUnits = InRange(menu.HalfWidthUnits, 2f, 100f, defaults.HalfWidthUnits, "Menu.HalfWidthUnits", file, issues),
        WorldUnitsPerPx = InRange(menu.WorldUnitsPerPx, 0.01f, 1f, defaults.WorldUnitsPerPx, "Menu.WorldUnitsPerPx", file, issues),
        Orientation = InRange(menu.Orientation, 0, 2, defaults.Orientation, "Menu.Orientation", file, issues),
    };

    private static T Missing<T>(T defaults, string key, string file, List<ConfigIssue> issues)
    {
        issues.Add(new ConfigIssue(file, key, "missing; using defaults"));
        return defaults;
    }

    private static string Color(string? value, string fallback, string key, string file, List<ConfigIssue> issues)
    {
        if (value is not null && HexColor().IsMatch(value))
        {
            return value;
        }
        issues.Add(new ConfigIssue(file, key, "must be a #RRGGBB color; using default"));
        return fallback;
    }

    // NaN compares below any minimum, so it falls back too.
    private static T InRange<T>(T value, T min, T max, T fallback, string key, string file, List<ConfigIssue> issues) where T : IComparable<T>
    {
        if (value.CompareTo(min) >= 0 && value.CompareTo(max) <= 0)
        {
            return value;
        }
        issues.Add(new ConfigIssue(file, key, $"must be between {min} and {max}; using default"));
        return fallback;
    }

    private static T Defined<T>(T value, T fallback, string key, string file, List<ConfigIssue> issues) where T : struct, Enum
    {
        if (Enum.IsDefined(value))
        {
            return value;
        }
        issues.Add(new ConfigIssue(file, key, "unknown value; using default"));
        return fallback;
    }
}
```

Lang : ajouter à `src/RetakeV4/lang/en.json`
```json
  "hud.menu.back": "Back",
  "hud.menu.close": "Close",
  "hud.menu.next": "Next page",
  "hud.menu.previous": "Previous page",
  "hud.menu.on": "ON",
  "hud.menu.off": "OFF",
  "hud.round.title": "{0} - site {1}",
  "hud.round.teams": "CT {0} vs {1} T",
  "hud.round.streak": "T win streak: {0}",
  "hud.queue.position": "Queue: {0}/{1}",
  "hud.queue.priority": "Priority access",
  "allocation.menu.title": "Weapons",
  "allocation.menu.current": "This round: {0} ({1})",
  "allocation.menu.others": "Other setups",
  "allocation.menu.config": "{0} ({1})",
  "allocation.menu.primary": "Primary: {0}",
  "allocation.menu.secondary": "Pistol: {0}",
  "allocation.menu.awp": "AWP",
  "allocation.menu.applied_now": "Weapons updated.",
  "allocation.menu.applied_next_round": "Saved, used from the next round.",
  "allocation.awp.received": "You have the AWP this round."
```
et à `src/RetakeV4/lang/fr.json`
```json
  "hud.menu.back": "Retour",
  "hud.menu.close": "Fermer",
  "hud.menu.next": "Page suivante",
  "hud.menu.previous": "Page précédente",
  "hud.menu.on": "ON",
  "hud.menu.off": "OFF",
  "hud.round.title": "{0} - site {1}",
  "hud.round.teams": "CT {0} contre {1} T",
  "hud.round.streak": "Série de victoires T : {0}",
  "hud.queue.position": "File d'attente : {0}/{1}",
  "hud.queue.priority": "Accès prioritaire",
  "allocation.menu.title": "Armes",
  "allocation.menu.current": "Ce round : {0} ({1})",
  "allocation.menu.others": "Autres configurations",
  "allocation.menu.config": "{0} ({1})",
  "allocation.menu.primary": "Principale : {0}",
  "allocation.menu.secondary": "Pistolet : {0}",
  "allocation.menu.awp": "AWP",
  "allocation.menu.applied_now": "Armes mises à jour.",
  "allocation.menu.applied_next_round": "Enregistré, utilisé dès le prochain round.",
  "allocation.awp.received": "Tu as l'AWP ce round."
```
(insérer avant l'accolade fermante, en ajoutant la virgule manquante à la dernière entrée existante).

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Modules/Hud src/RetakeV4/lang tests/RetakeV4.Integration.Tests/Modules/Hud tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs
git commit -m "feat: configuration hud.json validée et textes du HUD"
```

---

### Task 6: Module Hud — bloc d'informations centré

**Files:**
- Modify: `src/RetakeV4/Modules/ModuleHooks.cs`, `src/RetakeV4/RetakeV4Plugin.cs`
- Create: `src/RetakeV4/Modules/Hud/HudTextFormatter.cs`, `src/RetakeV4/Modules/Hud/CenterHud.cs`, `src/RetakeV4/Modules/Hud/HudModule.cs`

**Interfaces:**
- Consumes: widgets, `CenterComposer`, `CenterHtml` (Task 3) ; `RoundPrepared`, `TeamStateChanged`, `HudAlert` (Task 4) ; `HudConfig` (Task 5) ; `ITextService`, `PlayerQueries`.
- Produces:
  - `ModuleHooks.OnTick(string stage, Action handler)`, `ModuleHooks.OnCheckTransmit(string stage, Action<CCheckTransmitInfoList> handler)`, `ModuleHooks.OnPlayerButtons(string stage, Action<CCSPlayerController, PlayerButtons, PlayerButtons> handler)` — gardés, libérés au déchargement.
  - `internal static HudTextFormatter.Format(ITextService text, CCSPlayerController player, HudText value)`.
  - `internal sealed class CenterHud` : `OnRoundPrepared`, `OnTeams`, `OnAlert`, `Tick`.
  - `public sealed class HudModule : IRetakeModule` (`Name = "Hud"`, dépend de `Core`, `hud.json`), enregistré dans `RetakeV4Plugin` après `CoreModule`.

Code adaptateur (CounterStrikeSharp) : pas de test unitaire, couvert par la checklist en jeu (Task 9). La logique est déjà testée aux Tasks 1-3.

- [ ] **Step 1: Hooks de listeners**

Dans `src/RetakeV4/Modules/ModuleHooks.cs`, ajouter `using CounterStrikeSharp.API;` puis, après `OnMapStart` :
```csharp
    public void OnTick(string stage, Action handler)
    {
        Listeners.OnTick wrapper = () => _guard.Run(_module, stage, handler);
        _plugin.RegisterListener(wrapper);
        _registrations.Track(() => _plugin.RemoveListener(wrapper));
    }

    public void OnCheckTransmit(string stage, Action<CCheckTransmitInfoList> handler)
    {
        Listeners.CheckTransmit wrapper = infoList => _guard.Run(_module, stage, () => handler(infoList));
        _plugin.RegisterListener(wrapper);
        _registrations.Track(() => _plugin.RemoveListener(wrapper));
    }

    public void OnPlayerButtons(string stage, Action<CCSPlayerController, PlayerButtons, PlayerButtons> handler)
    {
        Listeners.OnPlayerButtonsChanged wrapper = (player, pressed, released) => _guard.Run(_module, stage, () => handler(player, pressed, released));
        _plugin.RegisterListener(wrapper);
        _registrations.Track(() => _plugin.RemoveListener(wrapper));
    }
```

- [ ] **Step 2: Formatage et bloc centré**

`src/RetakeV4/Modules/Hud/HudTextFormatter.cs`
```csharp
using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Hud;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

internal static class HudTextFormatter
{
    public static string Format(ITextService text, CCSPlayerController player, HudText value) =>
        value.Key is { } key ? text.For(player, key, value.Args.ToArray()) : value.Literal ?? string.Empty;
}
```

`src/RetakeV4/Modules/Hud/CenterHud.cs`
```csharp
using CounterStrikeSharp.API.Core;
using RetakeV4.Adapters;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Teams;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

// Game thread only. Widgets are recomputed every CenterRefreshMs; the last HTML is re-sent every tick because center HTML fades quickly.
internal sealed class CenterHud
{
    private const int AlertsPriority = 30;
    private const int QueuePriority = 20;
    private const int RoundInfoPriority = 10;

    private readonly HudConfig _config;
    private readonly ITextService _text;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<int, string> _html = new();
    private RoundInfoWidget _roundInfo = RoundInfoWidget.Empty;
    private QueueStatusWidget _queue = QueueStatusWidget.Empty;
    private AlertsWidget _alerts = AlertsWidget.Empty;
    private TeamState _teams = TeamState.Empty;
    private DateTimeOffset _nextRefresh = DateTimeOffset.MinValue;

    public CenterHud(HudConfig config, ITextService text, Func<DateTimeOffset> clock)
    {
        _config = config;
        _text = text;
        _clock = clock;
    }

    public void OnRoundPrepared(RoundPrepared e)
    {
        var info = new RoundInfo(e.Context.RoundType ?? "?", e.Context.Site, _teams.Ct.Count, _teams.T.Count);
        _roundInfo = _roundInfo.Show(info, _clock(), TimeSpan.FromSeconds(_config.Widgets.RoundInfo.ShowSeconds));
        _nextRefresh = DateTimeOffset.MinValue;
    }

    public void OnTeams(TeamStateChanged e)
    {
        _teams = e.State;
        _queue = QueueStatusWidget.From(e.State);
        _roundInfo = _roundInfo.WithStreak(e.State.TWinStreak);
        _nextRefresh = DateTimeOffset.MinValue;
    }

    public void OnAlert(HudAlert e)
    {
        _alerts = _alerts.Push(e.Player, e.Text, _clock(), TimeSpan.FromSeconds(_config.Widgets.Alerts.ShowSeconds));
        _nextRefresh = DateTimeOffset.MinValue;
    }

    public void Tick()
    {
        var now = _clock();
        var players = PlayerQueries.Humans();
        if (now >= _nextRefresh)
        {
            Refresh(players, now);
            _nextRefresh = now + TimeSpan.FromMilliseconds(_config.CenterRefreshMs);
        }
        foreach (var player in players)
        {
            if (_html.TryGetValue(player.Slot, out var html))
            {
                player.PrintToCenterHtml(html);
            }
        }
    }

    private void Refresh(IReadOnlyList<CCSPlayerController> players, DateTimeOffset now)
    {
        _html.Clear();
        foreach (var player in players)
        {
            if (Compose(player, now) is { } html)
            {
                _html[player.Slot] = html;
            }
        }
    }

    private string? Compose(CCSPlayerController player, DateTimeOffset now)
    {
        var id = new PlayerId(player.Slot);
        var widgets = _config.Widgets;
        var blocks = new List<(int Priority, IReadOnlyList<HudLine> Lines)>();
        if (widgets.Alerts.Enabled)
        {
            blocks.Add((AlertsPriority, _alerts.Render(id, now)));
        }
        if (widgets.QueueStatus.Enabled)
        {
            blocks.Add((QueuePriority, _queue.Render(id)));
        }
        if (widgets.RoundInfo.Enabled)
        {
            blocks.Add((RoundInfoPriority, _roundInfo.Render(now)));
        }
        var lines = CenterComposer.Compose(blocks, _config.CenterMaxLines)
            .Select(l => (HudTextFormatter.Format(_text, player, l.Text), l.Style))
            .ToList();
        return CenterHtml.Format(lines, _config.Theme.ToTheme());
    }
}
```

- [ ] **Step 3: Module**

`src/RetakeV4/Modules/Hud/HudModule.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Events;

namespace RetakeV4.Modules.Hud;

public sealed class HudModule : IRetakeModule
{
    private HudConfig _config = new();
    private ModuleContext? _context;

    public string Name => "Hud";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("hud.json", new HudConfig(), new HudConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        LoadCenter(context);
    }

    public void Unload() => _context = null;

    private void LoadCenter(ModuleContext context)
    {
        var center = new CenterHud(_config, context.Text, () => DateTimeOffset.UtcNow);
        var hooks = context.Hooks;
        hooks.OnBus<RoundPrepared>(center.OnRoundPrepared);
        hooks.OnBus<TeamStateChanged>(center.OnTeams);
        hooks.OnBus<HudAlert>(center.OnAlert);
        hooks.OnTick("center_tick", center.Tick);
    }
}
```

Dans `src/RetakeV4/RetakeV4Plugin.cs`, ajouter `using RetakeV4.Modules.Hud;` et `new HudModule(),` juste après `new CoreModule(),` dans la liste des modules.

- [ ] **Step 4: Vérifier**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4/Modules/ModuleHooks.cs src/RetakeV4/Modules/Hud src/RetakeV4/RetakeV4Plugin.cs
git commit -m "feat: module Hud avec bloc d'informations centré (round, file d'attente, alertes)"
```

---

### Task 7: Module Hud — menus point_worldtext et entrées

**Files:**
- Create: `src/RetakeV4/Adapters/PlayerView.cs`, `src/RetakeV4/Modules/Hud/MenuSession.cs`, `src/RetakeV4/Modules/Hud/WorldTextMenuView.cs`, `src/RetakeV4/Modules/Hud/MenuHud.cs`
- Modify: `src/RetakeV4/Modules/Hud/HudModule.cs`

**Interfaces:**
- Consumes: `MenuNavigator`, `MenuInput`, `AimMenuLayout.Centered`, `AimLineResolver`, `AimMenuGeometry` (Task 1, phase 0) ; `HudMenuOpen`, `HudMenuClose`, `HudMenuSelected` (Task 4) ; `HudConfig`, `MenuFollowMode` (Task 5) ; `ModuleHooks.OnTick/OnCheckTransmit/OnPlayerButtons`, `HudTextFormatter` (Task 6).
- Produces:
  - `internal sealed record PlayerViewPoint(Vec3 Eye, ViewAngles Angles, CBasePlayerPawn Pawn)`, `internal static PlayerView.Of(CCSPlayerController)` (pawn vivant, sinon pawn d'observateur), `PlayerView.DelayAttack(CCSPlayerController)`.
  - `internal sealed class MenuHud` : `OnOpen`, `OnClose`, `Close(int slot)`, `CloseAll()`, `Reset()`, `SuspendEntities()`, `ResumeEntities()`, `Tick()`, `OnButtons`, `OnSlotKey`, `OnCheckTransmit`.
  - Publie `HudMenuSelected(PlayerId, rootMenuId, itemId)` après avoir mis à jour et réaffiché la navigation.

Code adaptateur : pas de test unitaire (logique testée Task 1), vérifié par la checklist en jeu.

- [ ] **Step 1: Point de vue du joueur**

`src/RetakeV4/Adapters/PlayerView.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Adapters;

internal sealed record PlayerViewPoint(Vec3 Eye, ViewAngles Angles, CBasePlayerPawn Pawn);

internal static class PlayerView
{
    private const int AttackDelayTicks = 2;

    // Dead players and spectators look through their observer pawn.
    public static PlayerViewPoint? Of(CCSPlayerController player)
    {
        if (player.PawnIsAlive && player.PlayerPawn.Value is { IsValid: true, AbsOrigin: { } origin } pawn)
        {
            var eye = new Vec3(origin.X, origin.Y, origin.Z + pawn.ViewOffset.Z);
            return new PlayerViewPoint(eye, new ViewAngles(pawn.EyeAngles.X, pawn.EyeAngles.Y), pawn);
        }
        if (player.ObserverPawn.Value is { IsValid: true, AbsOrigin: { } position } observer)
        {
            var eye = new Vec3(position.X, position.Y, position.Z + observer.ViewOffset.Z);
            return new PlayerViewPoint(eye, new ViewAngles(observer.V_angle.X, observer.V_angle.Y), observer);
        }
        return null;
    }

    // Clicking an aimed menu line must not fire the weapon.
    public static void DelayAttack(CCSPlayerController player)
    {
        var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        if (weapon is not { IsValid: true })
        {
            return;
        }
        weapon.NextPrimaryAttackTick = Server.TickCount + AttackDelayTicks;
        weapon.NextSecondaryAttackTick = Server.TickCount + AttackDelayTicks;
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
    }
}
```

- [ ] **Step 2: Vue point_worldtext**

`src/RetakeV4/Modules/Hud/WorldTextMenuView.cs`
```csharp
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using RetakeV4.Adapters;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Hud;

internal sealed record WorldTextLine(string Text, Color Color);

// Entity 0 is the title, one line above the first menu line. Entities are reused when the menu changes.
internal sealed class WorldTextMenuView
{
    private readonly HudConfig _config;
    private readonly List<CPointWorldText> _entities = new();
    private readonly List<WorldTextLine> _shown = new();
    private readonly HashSet<uint> _parented = new();

    public WorldTextMenuView(HudConfig config) => _config = config;

    public IReadOnlyList<CPointWorldText> Entities => _entities;

    public bool Show(IReadOnlyList<WorldTextLine> lines)
    {
        if (_entities.Any(e => !e.IsValid))
        {
            Destroy();
        }
        for (var i = 0; i < lines.Count; i++)
        {
            if (i < _entities.Count)
            {
                Update(i, lines[i]);
                continue;
            }
            var entity = Create(lines[i]);
            if (entity is null)
            {
                return false;
            }
            _entities.Add(entity);
            _shown.Add(lines[i]);
        }
        while (_entities.Count > lines.Count)
        {
            RemoveLast();
        }
        return true;
    }

    public void Position(PlayerViewPoint view, ViewAngles opened, AimMenuLayout layout)
    {
        var facing = Facing(opened);
        for (var i = 0; i < _entities.Count; i++)
        {
            var p = AimMenuGeometry.LinePosition(layout, view.Eye, opened, i - 1);
            _entities[i].Teleport(new Vector(p.X, p.Y, p.Z), facing, new Vector(0f, 0f, 0f));
        }
        if (_config.Menu.FollowMode == MenuFollowMode.Parent)
        {
            Parent(view.Pawn);
        }
    }

    public void Destroy()
    {
        foreach (var entity in _entities.Where(e => e.IsValid))
        {
            entity.Remove();
        }
        _entities.Clear();
        _shown.Clear();
        _parented.Clear();
    }

    private void Update(int index, WorldTextLine line)
    {
        var entity = _entities[index];
        var shown = _shown[index];
        if (shown.Text != line.Text)
        {
            entity.AcceptInput("SetMessage", entity, entity, line.Text, 0);
        }
        if (shown.Color != line.Color)
        {
            entity.Color = line.Color;
            Utilities.SetStateChanged(entity, "CPointWorldText", "m_Color");
        }
        _shown[index] = line;
    }

    private CPointWorldText? Create(WorldTextLine line)
    {
        var entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (entity is not { IsValid: true })
        {
            return null;
        }
        entity.MessageText = line.Text;
        entity.Enabled = true;
        entity.FontSize = _config.Theme.FontSize;
        entity.WorldUnitsPerPx = _config.Menu.WorldUnitsPerPx;
        entity.Fullbright = true;
        entity.Color = line.Color;
        entity.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        entity.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
        entity.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
        entity.DispatchSpawn();
        return entity;
    }

    private void RemoveLast()
    {
        var last = _entities[^1];
        _parented.Remove(last.Index);
        if (last.IsValid)
        {
            last.Remove();
        }
        _entities.RemoveAt(_entities.Count - 1);
        _shown.RemoveAt(_shown.Count - 1);
    }

    private void Parent(CBasePlayerPawn pawn)
    {
        foreach (var entity in _entities)
        {
            if (_parented.Add(entity.Index))
            {
                entity.AcceptInput("SetParent", pawn, pawn, "!activator", 0);
            }
        }
    }

    // The three formulas of the HUD prototype (css_hudprobe_orient 0|1|2).
    private QAngle Facing(ViewAngles opened) => _config.Menu.Orientation switch
    {
        1 => new QAngle(0f, opened.Yaw - 90f, 90f),
        2 => new QAngle(opened.Pitch, opened.Yaw + 180f, 0f),
        _ => new QAngle(0f, opened.Yaw + 270f, 90f - opened.Pitch),
    };
}
```

- [ ] **Step 3: Session et gestionnaire de menus**

`src/RetakeV4/Modules/Hud/MenuSession.cs`
```csharp
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Hud;

internal sealed class MenuSession
{
    public MenuSession(MenuNavigator navigator) => Navigator = navigator;

    public MenuNavigator Navigator { get; set; }

    public ViewAngles? Opened { get; set; }

    public int? Aimed { get; set; }

    public WorldTextMenuView? View { get; set; }

    // The menu stays open; it is anchored again in front of the player when its entities are recreated.
    public void Detach()
    {
        View?.Destroy();
        View = null;
        Opened = null;
        Aimed = null;
    }
}
```

`src/RetakeV4/Modules/Hud/MenuHud.cs`
```csharp
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Rounds;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

// Game thread only. One session per player slot. Entities are never created between round_prestart and the frame after
// round_start (CS2 cleans the map in between): sessions survive and are rendered again when the window reopens.
internal sealed class MenuHud
{
    private readonly HudConfig _config;
    private readonly ITextService _text;
    private readonly IEventBus _bus;
    private readonly ILogger _logger;
    private readonly Func<RoundPhase> _phase;
    private readonly Color _accent;
    private readonly Color _normal;
    private readonly Color _muted;
    private readonly Dictionary<int, MenuSession> _sessions = new();
    private bool _entitiesAllowed = true;

    public MenuHud(HudConfig config, ITextService text, IEventBus bus, ILogger logger, Func<RoundPhase> phase)
    {
        _config = config;
        _text = text;
        _bus = bus;
        _logger = logger;
        _phase = phase;
        _accent = ColorTranslator.FromHtml(config.Theme.Accent);
        _normal = ColorTranslator.FromHtml(config.Theme.Text);
        _muted = ColorTranslator.FromHtml(config.Theme.Muted);
    }

    public void OnOpen(HudMenuOpen e)
    {
        var slot = e.Player.Slot;
        if (_sessions.TryGetValue(slot, out var session) && session.Navigator.Root.Id == e.Menu.Id)
        {
            session.Navigator = session.Navigator.Replace(e.Menu);
        }
        else if (e.RefreshOnly)
        {
            return;
        }
        else
        {
            Close(slot);
            _sessions[slot] = new MenuSession(MenuNavigator.Open(e.Menu));
        }
        Render(slot);
    }

    public void OnClose(HudMenuClose e)
    {
        if (_sessions.TryGetValue(e.Player.Slot, out var session) && session.Navigator.Root.Id == e.MenuId)
        {
            Close(e.Player.Slot);
        }
    }

    public void Close(int slot)
    {
        if (_sessions.Remove(slot, out var session))
        {
            session.Detach();
        }
    }

    public void CloseAll()
    {
        foreach (var slot in _sessions.Keys.ToList())
        {
            Close(slot);
        }
    }

    public void Reset()
    {
        CloseAll();
        _entitiesAllowed = true;
    }

    public void SuspendEntities()
    {
        _entitiesAllowed = false;
        foreach (var session in _sessions.Values)
        {
            session.Detach();
        }
    }

    public void ResumeEntities()
    {
        _entitiesAllowed = true;
        foreach (var slot in _sessions.Keys.ToList())
        {
            Render(slot);
        }
    }

    public void Tick()
    {
        foreach (var (slot, session) in _sessions.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true })
            {
                Close(slot);
            }
            else if (session.View is null)
            {
                Render(slot);
            }
            else
            {
                Follow(player, session);
            }
        }
    }

    public void OnButtons(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (!_sessions.TryGetValue(player.Slot, out var session) || session.View is null)
        {
            return;
        }
        var controls = Controls(player);
        if (controls.Aim && Pressed(pressed, PlayerButtons.Attack) && session.Aimed is { } aimed)
        {
            Activate(player, session, aimed);
        }
        else if (controls.Movement && Pressed(pressed, PlayerButtons.Forward))
        {
            Move(player, session, -1);
        }
        else if (controls.Movement && Pressed(pressed, PlayerButtons.Back))
        {
            Move(player, session, 1);
        }
        else if (controls.Movement && Pressed(pressed, PlayerButtons.Use))
        {
            Activate(player, session, session.Navigator.Cursor);
        }
    }

    public HookResult OnSlotKey(CCSPlayerController? player, int key)
    {
        if (player is not { IsValid: true } || !_sessions.TryGetValue(player.Slot, out var session) || session.View is null
            || !Controls(player).Keys || key > session.Navigator.Lines().Count)
        {
            return HookResult.Continue;
        }
        Activate(player, session, key - 1);
        return HookResult.Handled;
    }

    public void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_sessions.Count == 0)
        {
            return;
        }
        var owned = _sessions
            .Where(s => s.Value.View is not null)
            .Select(s => (Slot: s.Key, Entities: s.Value.View!.Entities))
            .ToList();
        foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
        {
            if (viewer is null)
            {
                continue;
            }
            foreach (var (_, entities) in owned.Where(o => o.Slot != viewer.Slot))
            {
                Hide(info, entities);
            }
        }
    }

    private static void Hide(CCheckTransmitInfo info, IReadOnlyList<CPointWorldText> entities)
    {
        foreach (var entity in entities.Where(e => e.IsValid))
        {
            info.TransmitEntities.Remove(entity);
        }
    }

    private void Render(int slot)
    {
        if (!_entitiesAllowed || !_sessions.TryGetValue(slot, out var session))
        {
            return;
        }
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is not { IsValid: true } || PlayerView.Of(player) is not { } view)
        {
            return;
        }
        session.Opened ??= view.Angles;
        session.View ??= new WorldTextMenuView(_config);
        if (!session.View.Show(Lines(player, session)))
        {
            _logger.LogWarning("Could not create HUD menu entities for slot {Slot}; closing the menu", slot);
            Close(slot);
            return;
        }
        session.View.Position(view, session.Opened.Value, Layout(session));
    }

    private void Follow(CCSPlayerController player, MenuSession session)
    {
        if (session.View is null || session.Opened is not { } opened || PlayerView.Of(player) is not { } view)
        {
            return;
        }
        var layout = Layout(session);
        if (_config.Menu.FollowMode == MenuFollowMode.Tick)
        {
            session.View.Position(view, opened, layout);
        }
        var aimed = Controls(player).Aim ? AimLineResolver.Resolve(layout, opened, view.Angles) : null;
        if (aimed != session.Aimed)
        {
            session.Aimed = aimed;
            session.View.Show(Lines(player, session));
        }
        if (aimed is not null && _config.Menu.BlockAttackWhileAiming)
        {
            PlayerView.DelayAttack(player);
        }
    }

    private void Move(CCSPlayerController player, MenuSession session, int delta)
    {
        session.Navigator = session.Navigator.Move(delta);
        session.View?.Show(Lines(player, session));
    }

    // The navigation is updated and shown before the selection is published: the owner may refresh the menu in its handler.
    private void Activate(CCSPlayerController player, MenuSession session, int lineIndex)
    {
        var menuId = session.Navigator.Root.Id;
        var (next, outcome) = session.Navigator.Activate(lineIndex);
        session.Navigator = next;
        if (outcome.Kind == MenuOutcomeKind.Closed)
        {
            Close(player.Slot);
            return;
        }
        Render(player.Slot);
        if (outcome is { Kind: MenuOutcomeKind.Selected, ItemId: { } itemId })
        {
            _bus.Publish(new HudMenuSelected(new PlayerId(player.Slot), menuId, itemId));
        }
    }

    private MenuControls Controls(CCSPlayerController player) => MenuInput.For(_phase(), player.PawnIsAlive, _config.Menu.Input);

    private AimMenuLayout Layout(MenuSession session) => AimMenuLayout.Centered(
        session.Navigator.Lines().Count, _config.Menu.DistanceUnits, _config.Menu.LineHeightUnits, _config.Menu.HalfWidthUnits);

    private IReadOnlyList<WorldTextLine> Lines(CCSPlayerController player, MenuSession session)
    {
        var highlighted = session.Aimed ?? session.Navigator.Cursor;
        var title = new WorldTextLine(HudTextFormatter.Format(_text, player, session.Navigator.Current.Title), _muted);
        return session.Navigator.Lines()
            .Select((line, index) => new WorldTextLine($"{index + 1}. {Label(player, line)}", index == highlighted ? _accent : _normal))
            .Prepend(title)
            .ToList();
    }

    private string Label(CCSPlayerController player, MenuLine line)
    {
        var label = HudTextFormatter.Format(_text, player, line.Label);
        return line.ItemKind switch
        {
            MenuItemKind.Toggle => $"{label} : {_text.For(player, line.IsOn ? "hud.menu.on" : "hud.menu.off")}",
            MenuItemKind.Choice when line.IsOn => $"> {label}",
            _ => label,
        };
    }

    private static bool Pressed(PlayerButtons pressed, PlayerButtons button) => (pressed & button) != 0;
}
```

- [ ] **Step 4: Branchement dans le module**

Dans `src/RetakeV4/Modules/Hud/HudModule.cs` :
- ajouter les `using` `CounterStrikeSharp.API`, `CounterStrikeSharp.API.Core`, `CounterStrikeSharp.API.Modules.Commands`, `RetakeV4.Domain.Hud` ;
- ajouter le champ `private MenuHud? _menus;` ;
- dans `Load`, appeler `LoadMenus(context);` après `LoadCenter(context);` ;
- remplacer `Unload` par :
```csharp
    public void Unload()
    {
        _menus?.CloseAll();
        _menus = null;
        _context = null;
    }
```
- ajouter :
```csharp
    private void LoadMenus(ModuleContext context)
    {
        var menus = new MenuHud(_config, context.Text, context.Bus, context.Logger, () => context.Rounds.State.Phase);
        _menus = menus;
        var hooks = context.Hooks;
        hooks.OnBus<HudMenuOpen>(menus.OnOpen);
        hooks.OnBus<HudMenuClose>(menus.OnClose);
        hooks.OnBus<MapStarted>(_ => menus.Reset());
        hooks.OnTick("menu_tick", menus.Tick);
        hooks.OnCheckTransmit("menu_transmit", menus.OnCheckTransmit);
        hooks.OnPlayerButtons("menu_buttons", menus.OnButtons);
        hooks.OnEvent<EventRoundPrestart>("round_prestart", _ => menus.SuspendEntities());
        hooks.OnEvent<EventRoundStart>("round_start", _ =>
            Server.NextFrame(() => _context?.Guard.Run(Name, "menu_resume", () => _menus?.ResumeEntities())));
        hooks.OnEvent<EventPlayerDisconnect>("player_disconnect", e =>
        {
            if (e.Userid is { } player)
            {
                menus.Close(player.Slot);
            }
        });
        for (var key = 1; key <= MenuNavigator.MaxLines; key++)
        {
            var slotKey = key;
            hooks.CommandListener($"slot{slotKey}", (player, _) => menus.OnSlotKey(player, slotKey), HookMode.Pre);
        }
    }
```

- [ ] **Step 5: Vérifier**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`. Si une API diffère (ex. `CEntityInstance.Index`, `CBasePlayerPawn.V_angle`), la vérifier avec le dumper de réflexion (`dotnet <scratchpad>/apidump/bin/Release/net10.0/apidump.dll <Type> [filtre]`) et ledger l'écart.

- [ ] **Step 6: Commit**

```bash
git add src/RetakeV4/Adapters/PlayerView.cs src/RetakeV4/Modules/Hud
git commit -m "feat: menus HUD point_worldtext (viseur + clic, W/S/E, touches 1-9, visibilité réservée)"
```

---

### Task 8: Menu d'armes dans le module Allocation

**Files:**
- Modify: `src/RetakeV4/Modules/Allocation/AllocationConfig.cs`, `src/RetakeV4/Modules/Allocation/PreferenceService.cs`, `src/RetakeV4/Modules/Allocation/AllocationModule.cs`

**Interfaces:**
- Consumes: `WeaponMenu`, `WeaponMenuSelection`, `WeaponMenuState`, `WeaponSlot`, `LoadoutPlanner.WithWeapons`, `PreferenceBook.SetWeapon` (Task 2) ; `HudMenuOpen`, `HudMenuClose`, `HudMenuSelected`, `HudAlert`, `RoundTypesLoaded` (Task 4) ; `HudText` (Task 1).
- Produces:
  - `AllocationConfig` version 3 avec `bool AutoOpenMenu = true`.
  - `PreferenceService.IsAwpVolunteer(ulong)`, `PreferenceService.SetWeapon(ulong, TeamSide, string, WeaponSlot, string)`.
  - Commandes `css_<alias>` pour chaque alias V3 → ouvre le menu.
  - Ouverture automatique au début du freeze time (nouveau joueur ou type de round changé), fermeture des menus ouverts automatiquement au début du round live.
  - Alertes `allocation.awp.received`, `allocation.menu.applied_now`, `allocation.menu.applied_next_round`.

- [ ] **Step 1: Config et service**

Dans `src/RetakeV4/Modules/Allocation/AllocationConfig.cs`, remplacer le record `AllocationConfig` par :
```csharp
public sealed record AllocationConfig : ModuleConfig
{
    public AllocationConfig() => Version = 3;

    public DatabaseConfig Database { get; init; } = new();

    public bool AutoOpenMenu { get; init; } = true;
}
```

Dans `src/RetakeV4/Modules/Allocation/PreferenceService.cs`, ajouter après `RequestFor` :
```csharp
    public bool IsAwpVolunteer(ulong steamId) => _book.IsAwpVolunteer(steamId);

    public void SetWeapon(ulong steamId, TeamSide team, string roundType, WeaponSlot slot, string weapon)
    {
        var (book, change) = _book.SetWeapon(steamId, team, roundType, slot, weapon);
        _book = book;
        _writes.Enqueue(change);
    }
```

- [ ] **Step 2: Module**

Dans `src/RetakeV4/Modules/Allocation/AllocationModule.cs` :
- ajouter les `using` `System.Collections.Immutable`, `RetakeV4.Domain.Events`, `RetakeV4.Domain.Hud` ;
- ajouter les champs :
```csharp
    private static readonly string[] GunsAliases =
    {
        "guns", "gans", "gun", "g", "gns", "gnus", "weapon", "waepon", "weapons", "waepons", "waffen", "menu", "allocator", "select",
    };

    private readonly HashSet<ulong> _menuSeen = new();
    private readonly HashSet<int> _autoOpened = new();
    private ImmutableDictionary<PlayerId, Loadout> _lastPlan = ImmutableDictionary<PlayerId, Loadout>.Empty;
    private IReadOnlyList<RoundTypeDefinition> _definitions = Array.Empty<RoundTypeDefinition>();
    private RoundTypeDefinition? _current;
    private bool _roundTypeChanged;
```
- dans `Load`, remplacer le handler `player_disconnect` par :
```csharp
        hooks.OnEvent<EventPlayerDisconnect>("player_disconnect", e =>
        {
            if (e.Userid is not { IsValid: true } player)
            {
                return;
            }
            _lastPlan = _lastPlan.Remove(new PlayerId(player.Slot));
            _autoOpened.Remove(player.Slot);
            if (!player.IsBot)
            {
                _preferences?.PlayerDisconnected(player.SteamID);
            }
        });
```
  et ajouter avant la boucle `foreach (var player in PlayerQueries.Humans())` :
```csharp
        hooks.OnBus<RoundTypesLoaded>(e => _definitions = e.Definitions);
        hooks.OnBus<HudMenuSelected>(OnMenuSelected);
        hooks.OnBus<RoundPhaseChanged>(OnPhaseChanged);
        foreach (var alias in GunsAliases)
        {
            hooks.Command($"css_{alias}", "Opens the weapon menu", (player, _) => OnGunsCommand(player));
        }
```
- dans `AssignLoadouts`, juste après le `if (context.RoundTypeDefinition is not { } definition) { ... }`, ajouter :
```csharp
        _roundTypeChanged = _current?.Name != definition.Name;
        _current = definition;
```
  et remplacer la boucle d'application par :
```csharp
        _lastPlan = plan.ToImmutableDictionary();
        foreach (var (controller, _) in players)
        {
            LoadoutApplier.Apply(controller, plan[new PlayerId(controller.Slot)]);
        }
        foreach (var awp in plan.Where(p => p.Value.Primary == WeaponCatalog.Awp))
        {
            Context.Bus.Publish(new HudAlert(awp.Key, HudText.Of("allocation.awp.received")));
        }
```
- ajouter les méthodes :
```csharp
    private void OnGunsCommand(CCSPlayerController? player)
    {
        if (player is { IsValid: true } && player.SteamID != 0)
        {
            _menuSeen.Add(player.SteamID);
            OpenMenu(player, refreshOnly: false);
        }
    }

    private void OpenMenu(CCSPlayerController player, bool refreshOnly)
    {
        if (_preferences is not { } preferences || player.SteamID == 0)
        {
            return;
        }
        var steamId = player.SteamID;
        var state = new WeaponMenuState(
            _definitions,
            _current,
            PlayerQueries.SideOf(player),
            (team, roundType) => preferences.RequestFor(steamId, team, roundType),
            preferences.IsAwpVolunteer(steamId));
        Context.Bus.Publish(new HudMenuOpen(new PlayerId(player.Slot), WeaponMenu.Build(state), refreshOnly));
    }

    private void OnMenuSelected(HudMenuSelected e)
    {
        if (e.MenuId != WeaponMenu.MenuId || _preferences is not { } preferences)
        {
            return;
        }
        var player = Utilities.GetPlayerFromSlot(e.Player.Slot);
        if (player is not { IsValid: true } || player.SteamID == 0)
        {
            return;
        }
        if (e.ItemId == WeaponMenu.AwpItemId)
        {
            preferences.ToggleAwp(player.SteamID);
        }
        else if (WeaponMenuSelection.Parse(e.ItemId) is { } selection && WeaponMenu.IsAllowed(selection, _definitions))
        {
            ApplySelection(player, preferences, selection);
        }
        OpenMenu(player, refreshOnly: true);
    }

    private void ApplySelection(CCSPlayerController player, PreferenceService preferences, WeaponMenuSelection selection)
    {
        preferences.SetWeapon(player.SteamID, selection.Team, selection.RoundType, selection.Slot, selection.Weapon);
        var id = new PlayerId(player.Slot);
        var phase = Context.Rounds.State.Phase;
        if (_current is { } definition && _lastPlan.TryGetValue(id, out var loadout)
            && WeaponMenu.AppliesNow(selection, phase, player.PawnIsAlive, PlayerQueries.SideOf(player), definition.Name))
        {
            var request = new LoadoutRequest(id, selection.Team, preferences.RequestFor(player.SteamID, selection.Team, definition.Name));
            var adjusted = LoadoutPlanner.WithWeapons(loadout, definition, request);
            _lastPlan = _lastPlan.SetItem(id, adjusted);
            LoadoutApplier.Apply(player, adjusted);
            Context.Bus.Publish(new HudAlert(id, HudText.Of("allocation.menu.applied_now")));
            return;
        }
        Context.Bus.Publish(new HudAlert(id, HudText.Of("allocation.menu.applied_next_round")));
    }

    private void OnPhaseChanged(RoundPhaseChanged e)
    {
        if (e.To == RoundPhase.FreezeTime)
        {
            AutoOpenMenus();
        }
        else if (e.To == RoundPhase.Live)
        {
            CloseAutoOpenedMenus();
        }
    }

    // New players and every player after a round type change get the menu once, if the round offers a choice.
    private void AutoOpenMenus()
    {
        if (!_config.AutoOpenMenu || _current is not { } current)
        {
            return;
        }
        foreach (var player in PlayerQueries.Humans())
        {
            if (player.SteamID == 0 || PlayerQueries.SideOf(player) is not { } side || !WeaponMenu.HasChoice(current, side))
            {
                continue;
            }
            var firstTime = _menuSeen.Add(player.SteamID);
            if (firstTime || _roundTypeChanged)
            {
                OpenMenu(player, refreshOnly: false);
                _autoOpened.Add(player.Slot);
            }
        }
    }

    private void CloseAutoOpenedMenus()
    {
        foreach (var slot in _autoOpened)
        {
            Context.Bus.Publish(new HudMenuClose(new PlayerId(slot), WeaponMenu.MenuId));
        }
        _autoOpened.Clear();
    }
```

- [ ] **Step 3: Vérifier**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Modules/Allocation
git commit -m "feat: menu d'armes !guns (ouverture auto, choix par configuration, AWP, changement pendant le freeze time)"
```

---

### Task 9: Checklist en jeu phase 3b, documentation et vérification finale

**Files:**
- Modify: `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`

- [ ] **Step 1: Checklist**

Ajouter à `docs/CHECKLIST-INGAME.md` :
```markdown

## Phase 3b — HUD et menu d'armes
Remplir aussi `docs/spikes/hud-probe-findings.md` : les réglages `hud.json` → `Menu.Orientation`, `Menu.FollowMode`, `Menu.Input`, `Menu.DistanceUnits` correspondent aux questions du prototype.
- [ ] Premier démarrage : `hud.json` est créé, aucun avertissement de config.
- [ ] Début de round : le bloc centré affiche « <type> - site <A/B> » et « CT n contre n T » pendant ~6 s ; la série de victoires T apparaît quand elle existe.
- [ ] Joueur en file d'attente : « File d'attente : position/total » visible en continu, « Accès prioritaire » pour un VIP.
- [ ] `!guns` (et un alias, ex. `!gun`) : le menu s'ouvre face au joueur, lisible (sinon essayer `Menu.Orientation` 1 puis 2).
- [ ] Le menu suit le joueur sans tremblement gênant (sinon tester `Menu.FollowMode` = `Parent`).
- [ ] Freeze time : viser une ligne la met en surbrillance ; clic gauche = sélection, sans tirer ; W/S déplacent le curseur, E valide ; touches 1-9 sélectionnent sans changer d'arme. Précision : < 2 erreurs sur 20 essais.
- [ ] Round live, vivant : seules les touches 1-9 agissent ; viser/cliquer tire normalement, W/S/E bougent/interagissent normalement.
- [ ] Un second joueur ne voit pas le menu du premier.
- [ ] Choisir une arme principale pendant le freeze time : l'arme est remplacée immédiatement, alerte « Armes mises à jour. » ; kit, grenades et AWP éventuelle conservés.
- [ ] Choisir une arme en round live ou pour une autre configuration : alerte « Enregistré, utilisé dès le prochain round. », appliqué au round suivant.
- [ ] « Autres configurations » liste les couples équipe × type de round qui offrent un choix ; un pistol round ne propose que le pistolet.
- [ ] AWP : ON/OFF bascule et persiste après reconnexion ; recevoir l'AWP affiche « Tu as l'AWP ce round. ».
- [ ] Nouveau joueur : le menu s'ouvre seul au freeze time, puis se ferme au début du round live ; il se rouvre quand le type de round change.
- [ ] Menu ouvert pendant la fin de round / le restart : aucune erreur console, le menu réapparaît au round suivant.
- [ ] Mort ou spectateur : le menu s'ouvre et se pilote au viseur.
- [ ] Déconnexion avec le menu ouvert : aucune entité orpheline (`ent_find point_worldtext`).
- [ ] Perf : 2+ menus ouverts, aucune chute de fps notable.
- [ ] `hud.json` → `Menu.Input` = `Keys` : la visée ne sélectionne plus, le clavier fonctionne.
```

- [ ] **Step 2: CLAUDE.md**

Dans la section Structure de `CLAUDE.md`, ajouter :
```markdown
- `src/RetakeV4/Modules/Hud` : moteur HUD (bloc centré, menus `point_worldtext`, entrées). Logique dans `src/RetakeV4.Domain/Hud`.
```
et dans la section Règles :
```markdown
- Menus HUD : un module construit un `Menu` (Domain), publie `HudMenuOpen` / `HudMenuClose` et écoute `HudMenuSelected` ; messages éphémères via `HudAlert`. Aucune entité HUD hors du module Hud.
```

- [ ] **Step 3: Vérification finale**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo && dotnet test tests/RetakeV4.Domain.Tests --nologo -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`
puis (outil PowerShell) `pwsh -NoProfile -File scripts/package-dev.ps1`
Expected: 0 warning, `Failed: 0`, couverture Domain ≥ 80 %, `Package ready`.

- [ ] **Step 4: Commit**

```bash
git add docs/CHECKLIST-INGAME.md CLAUDE.md
git commit -m "docs: checklist en jeu phase 3b et règles du HUD"
```
