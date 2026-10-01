# RetakeV4 phase 4b — Menu admin, pont SimpleAdmin et Links — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Donner aux admins un menu HUD `!retake` (éditeur de spawns, forçage de site, scramble), les mêmes entrées dans CS2-SimpleAdmin s'il est installé, et des commandes communautaires (`!discord`, `!site`…) définies dans `links.json`.

**Architecture:** Le menu admin est un `Menu` construit dans le Domain (`AdminMenu`) et partagé par le HUD et le pont SimpleAdmin. Le module `Admin` ne fait que vérifier la permission puis publier des demandes sur le bus (`SpawnEditorRequested`, `ForceSiteRequested`, `ScrambleRequested`) traitées par les modules Spawns et Teams. Le pont SimpleAdmin passe par réflexion sur l'interface publique `ICS2_SimpleAdminApi` (aucune dépendance de compilation). Le module `Links` enregistre une commande par entrée validée de `links.json`.

**Tech Stack:** C# / .NET 10, CounterStrikeSharp.API 1.0.370, xUnit 2.9.3, System.Reflection.

**Spec:** `docs/superpowers/specs/2026-09-30-retake-v4-design.md` (sections 3.3, 12)

## Global Constraints

- CounterStrikeSharp.API 1.0.370, .NET 10, `dotnet build RetakeV4.sln -c Release` : 0 warning.
- `RetakeV4.Domain` sans CounterStrikeSharp, couverture de lignes ≥ 80 %.
- Modules sans référence croisée : bus, pipeline ou services du `ModuleContext`. Handlers via `context.Hooks`.
- Textes joueurs via `lang/*.json` (en + fr synchronisés) ; logs en anglais. Exception : les messages de `links.json` sont du contenu serveur, affichés tels quels (balises de couleur CSSharp acceptées).
- Permission admin : `@retakev4/admin`.
- Pont SimpleAdmin : CS2-SimpleAdmin (`daffyyyy/CS2-SimpleAdmin`), capacité `simpleadmin:api`, interface `CS2_SimpleAdminApi.ICS2_SimpleAdminApi` ; méthodes utilisées (vérifiées sur le dépôt) : `RegisterMenuCategory(string, string, string)`, `RegisterMenu(string, string, string, Func<CCSPlayerController, object>, string?, string?)`, `UnregisterMenu(string, string)`, `CreateMenuWithBack(string, string, CCSPlayerController)`, `AddMenuOption(object, string, Action<CCSPlayerController>, bool, string?)`.

## Décisions de conception

1. **`!retake`** : sans argument ouvre le menu admin HUD ; `!retake edit` ouvre l'éditeur de spawns (via `SpawnEditorRequested`) ; tout autre argument affiche l'usage. Réservé à `@retakev4/admin` (le non-admin reçoit un refus). `css_retake_info` reste la commande publique de version.
2. **Demandes par le bus** : le module Admin vérifie la permission puis publie ; Spawns et Teams appliquent sans revérifier (les demandes ne viennent que du code du plugin). Le forçage via le bus suit exactement les règles de `css_retake_forcesite` (refus si aucun spawn sur le site).
3. **SimpleAdmin** : catégorie `retakev4`, une entrée par élément racine du menu admin ; un sous-menu (forçage de site) devient un menu SimpleAdmin avec une option par choix. Les libellés utilisent la langue du serveur. Enregistrement à `OnAllPluginsLoaded` (nouvel événement de bus `AllPluginsLoaded`), retrait au déchargement. API absente → message d'information ; API différente → avertissement, le plugin continue sans pont.
4. **Links** : `links.json` = liste d'entrées `{ "Commands": ["discord", "dc"], "Message": "..." }`, vide par défaut. Une entrée invalide est ignorée avec un avertissement : nom de commande hors `^[a-z0-9_]{1,32}$`, commençant par `retake`, déjà utilisé par le plugin (alias `!guns`, `awp`) ou par une autre entrée, message vide ou de plus de 512 caractères. Au plus 32 entrées. Le message est envoyé dans le chat du joueur qui tape la commande.

## Review Focus

1. Un non-admin qui tape `!retake`, `!retake edit`, ou dont la session HUD reçoit une sélection du menu admin → refus, aucune demande publiée (Task 3 ; Domain : `Parse_RejectsUnknownItems`).
2. `links.json` avec une commande réservée (`guns`, `retake_x`), en double ou mal formée → entrée ignorée avec avertissement, les autres gardées (Task 5 : `InvalidLinks_AreSkipped_AndTheRestIsKept`).
3. CS2-SimpleAdmin absent ou d'une version dont la signature diffère → aucun crash, log d'information ou d'avertissement, `!retake` fonctionne (Task 4, checklist).
4. `!retake EDIT`, `!retake  edit ` ou un argument inconnu → éditeur ou usage, jamais d'exception (Task 1 : `RetakeCommand_ParsesTheSubcommand`).
5. Un forçage demandé depuis le menu sur un site sans spawn → refusé avec le même message que la commande console (Task 2, checklist).

---

## File Structure

```
src/RetakeV4.Domain/Admin/AdminMenu.cs, Events/AdminEvents.cs                 (Task 1)
src/RetakeV4/RetakeV4Plugin.cs, Modules/Spawns/SpawnsModule.cs,
    Modules/Teams/TeamsModule.cs                                             (Task 2)
src/RetakeV4/Modules/Admin/AdminConfig.cs, AdminModule.cs, lang/*.json       (Task 3)
src/RetakeV4/Modules/Admin/SimpleAdminBridge.cs                              (Task 4)
src/RetakeV4/Modules/Links/LinksConfig.cs, LinksConfigValidator.cs,
    LinksModule.cs                                                           (Task 5)
docs/CHECKLIST-INGAME.md, CLAUDE.md                                          (Task 6)
```

---

### Task 1: Menu admin et demandes (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Admin/AdminMenu.cs`, `src/RetakeV4.Domain/Events/AdminEvents.cs`
- Test: `tests/RetakeV4.Domain.Tests/Admin/AdminMenuTests.cs`

**Interfaces:**
- Consumes: `Menu`, `MenuItem`, `MenuItemKind`, `HudText` (3b) ; `ForceSiteRequest`, `SiteForce`, `ForceSiteMode`, `BombSite` (4a).
- Produces:
  - `enum AdminAction { SpawnEditor, Scramble, ForceSite }`, `sealed record AdminSelection(AdminAction Action, ForceSiteRequest? Force = null)`.
  - `static class AdminMenu` : `MenuId = "admin.main"`, `EditorId = "editor"`, `ScrambleId = "scramble"`, `ForceOffId = "force:off"`, `Build()`, `Parse(string itemId)` → `AdminSelection?`.
  - `enum RetakeCommandKind { Menu, Editor, Unknown }`, `static RetakeCommand.Parse(string?)`.
  - Événements : `ForceSiteRequested(PlayerId? Requester, ForceSiteRequest Request)`, `ScrambleRequested(PlayerId? Requester)`, `AllPluginsLoaded(bool HotReload)`.

- [ ] **Step 1: Écrire le test qui échoue**

`tests/RetakeV4.Domain.Tests/Admin/AdminMenuTests.cs`
```csharp
using RetakeV4.Domain.Admin;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Admin;

public class AdminMenuTests
{
    [Fact]
    public void Root_OffersEditorForceSiteAndScramble()
    {
        var menu = AdminMenu.Build();
        Assert.Equal(AdminMenu.MenuId, menu.Id);
        Assert.Equal(new[] { AdminMenu.EditorId, "forcesite", AdminMenu.ScrambleId }, menu.Items.Select(i => i.Id));
        Assert.Equal(MenuItemKind.Submenu, menu.Items[1].Kind);
    }

    [Fact]
    public void EveryItem_ParsesToItsAction()
    {
        var menu = AdminMenu.Build();
        Assert.Equal(new AdminSelection(AdminAction.SpawnEditor), AdminMenu.Parse(AdminMenu.EditorId));
        Assert.Equal(new AdminSelection(AdminAction.Scramble), AdminMenu.Parse(AdminMenu.ScrambleId));
        var forces = menu.Items[1].Submenu!.Items.Select(i => AdminMenu.Parse(i.Id)?.Force?.Force).ToList();
        Assert.Equal(
            new SiteForce?[]
            {
                new(BombSite.A, ForceSiteMode.Once), new(BombSite.A, ForceSiteMode.Sticky),
                new(BombSite.B, ForceSiteMode.Once), new(BombSite.B, ForceSiteMode.Sticky), null,
            },
            forces);
        Assert.Equal(new AdminSelection(AdminAction.ForceSite, new ForceSiteRequest(null)), AdminMenu.Parse(AdminMenu.ForceOffId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("forcesite")]
    [InlineData("force:C:Once")]
    [InlineData("force:A:Forever")]
    [InlineData("force:A")]
    [InlineData("allocation.weapons")]
    public void Parse_RejectsUnknownItems(string itemId) => Assert.Null(AdminMenu.Parse(itemId));

    [Theory]
    [InlineData(null, RetakeCommandKind.Menu)]
    [InlineData("", RetakeCommandKind.Menu)]
    [InlineData("edit", RetakeCommandKind.Editor)]
    [InlineData(" EDIT ", RetakeCommandKind.Editor)]
    [InlineData("editor", RetakeCommandKind.Editor)]
    [InlineData("scramble", RetakeCommandKind.Unknown)]
    public void RetakeCommand_ParsesTheSubcommand(string? argument, RetakeCommandKind expected) =>
        Assert.Equal(expected, RetakeCommand.Parse(argument));
}
```

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Domain.Admin` introuvable).

- [ ] **Step 2: Implémentation**

`src/RetakeV4.Domain/Admin/AdminMenu.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Admin;

public enum AdminAction
{
    SpawnEditor,
    Scramble,
    ForceSite,
}

public sealed record AdminSelection(AdminAction Action, ForceSiteRequest? Force = null);

public static class AdminMenu
{
    public const string MenuId = "admin.main";
    public const string EditorId = "editor";
    public const string ScrambleId = "scramble";
    public const string ForceOffId = "force:off";

    private const string ForcePrefix = "force";

    public static Menu Build() => new(MenuId, HudText.Of("admin.menu.title"), new[]
    {
        new MenuItem(EditorId, HudText.Of("admin.menu.editor"), MenuItemKind.Action),
        new MenuItem("forcesite", HudText.Of("admin.menu.forcesite"), MenuItemKind.Submenu, Submenu: ForceMenu()),
        new MenuItem(ScrambleId, HudText.Of("admin.menu.scramble"), MenuItemKind.Action),
    });

    public static AdminSelection? Parse(string itemId) => itemId switch
    {
        EditorId => new AdminSelection(AdminAction.SpawnEditor),
        ScrambleId => new AdminSelection(AdminAction.Scramble),
        ForceOffId => new AdminSelection(AdminAction.ForceSite, new ForceSiteRequest(null)),
        _ => ParseForce(itemId),
    };

    private static AdminSelection? ParseForce(string itemId)
    {
        var parts = itemId.Split(':');
        if (parts.Length != 3 || parts[0] != ForcePrefix)
        {
            return null;
        }
        return Enum.TryParse<BombSite>(parts[1], out var site) && Enum.IsDefined(site)
            && Enum.TryParse<ForceSiteMode>(parts[2], out var mode) && Enum.IsDefined(mode)
                ? new AdminSelection(AdminAction.ForceSite, new ForceSiteRequest(new SiteForce(site, mode)))
                : null;
    }

    private static Menu ForceMenu() => new(
        "forcesite",
        HudText.Of("admin.menu.forcesite"),
        new[] { BombSite.A, BombSite.B }
            .SelectMany(site => new[]
            {
                new MenuItem($"{ForcePrefix}:{site}:{ForceSiteMode.Once}", HudText.Of("admin.menu.force_once", site.ToString()), MenuItemKind.Action),
                new MenuItem($"{ForcePrefix}:{site}:{ForceSiteMode.Sticky}", HudText.Of("admin.menu.force_sticky", site.ToString()), MenuItemKind.Action),
            })
            .Append(new MenuItem(ForceOffId, HudText.Of("admin.menu.force_off"), MenuItemKind.Action))
            .ToList());
}

public enum RetakeCommandKind
{
    Menu,
    Editor,
    Unknown,
}

public static class RetakeCommand
{
    public static RetakeCommandKind Parse(string? argument) => argument?.Trim().ToLowerInvariant() switch
    {
        null or "" => RetakeCommandKind.Menu,
        "edit" or "editor" => RetakeCommandKind.Editor,
        _ => RetakeCommandKind.Unknown,
    };
}
```

`src/RetakeV4.Domain/Events/AdminEvents.cs`
```csharp
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Events;

// Requester null: the server console. The publisher has already checked the permission.
public sealed record ForceSiteRequested(PlayerId? Requester, ForceSiteRequest Request);

public sealed record ScrambleRequested(PlayerId? Requester);

// CounterStrikeSharp's OnAllPluginsLoaded: other plugins' capabilities (CS2-SimpleAdmin) can be resolved from here on.
public sealed record AllPluginsLoaded(bool HotReload);
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4.Domain/Admin src/RetakeV4.Domain/Events/AdminEvents.cs tests/RetakeV4.Domain.Tests/Admin
git commit -m "feat: menu admin, sous-commandes de !retake et demandes d'administration (Domain)"
```

---

### Task 2: Spawns et Teams répondent aux demandes du bus

**Files:**
- Modify: `src/RetakeV4/RetakeV4Plugin.cs`, `src/RetakeV4/Modules/Spawns/SpawnsModule.cs`, `src/RetakeV4/Modules/Teams/TeamsModule.cs`

**Interfaces:**
- Consumes: `ForceSiteRequested`, `ScrambleRequested`, `AllPluginsLoaded` (Task 1).
- Produces:
  - `RetakeV4Plugin` publie `AllPluginsLoaded(hotReload)` depuis `OnAllPluginsLoaded`.
  - `SpawnsModule.ApplyForce(CCSPlayerController? player, ForceSiteRequest request)` partagé par la commande et le bus.
  - `TeamsModule.RequestScramble()` partagé par la commande et le bus.

Code adaptateur : vérifié par build, tests existants et checklist.

- [ ] **Step 1: Plugin**

Dans `src/RetakeV4/RetakeV4Plugin.cs` :
- ajouter le champ `private EventBus? _bus;` ;
- dans `Load`, après `var bus = new EventBus(OnBusError);`, ajouter `_bus = bus;` ;
- dans `Unload`, ajouter `_bus = null;` à la fin ;
- ajouter :
```csharp
    public override void OnAllPluginsLoaded(bool hotReload) => _bus?.Publish(new AllPluginsLoaded(hotReload));
```

- [ ] **Step 2: Spawns**

Dans `src/RetakeV4/Modules/Spawns/SpawnsModule.cs` :
- dans `Load`, après la ligne `hooks.Command("css_retake_forcesite", ...)`, ajouter :
```csharp
        hooks.OnBus<ForceSiteRequested>(e =>
            ApplyForce(e.Requester is { } requester ? Utilities.GetPlayerFromSlot(requester.Slot) : null, e.Request));
```
- remplacer `OnForceSite` par :
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
        ApplyForce(player, request);
    }

    private void ApplyForce(CCSPlayerController? player, ForceSiteRequest request)
    {
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
```

- [ ] **Step 3: Teams**

Dans `src/RetakeV4/Modules/Teams/TeamsModule.cs` :
- dans `Load`, après `hooks.Command("css_retake_scramble", ...)`, ajouter :
```csharp
        hooks.OnBus<ScrambleRequested>(_ => RequestScramble());
```
- remplacer les deux dernières lignes de `OnScrambleCommand` (`_scrambleRequested = true;` et `Context.Text.ChatAll("teams.scramble.requested");`) par `RequestScramble();` et ajouter :
```csharp
    private void RequestScramble()
    {
        _scrambleRequested = true;
        Context.Text.ChatAll("teams.scramble.requested");
    }
```

- [ ] **Step 4: Vérifier**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4/RetakeV4Plugin.cs src/RetakeV4/Modules/Spawns/SpawnsModule.cs src/RetakeV4/Modules/Teams/TeamsModule.cs
git commit -m "feat: forçage de site et scramble pilotables par le bus, événement AllPluginsLoaded"
```

---

### Task 3: Module Admin et commande `!retake`

**Files:**
- Create: `src/RetakeV4/Modules/Admin/AdminConfig.cs`, `src/RetakeV4/Modules/Admin/AdminModule.cs`
- Modify: `src/RetakeV4/RetakeV4Plugin.cs`, `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`
- Test: `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs`

**Interfaces:**
- Consumes: `AdminMenu`, `AdminSelection`, `RetakeCommand` (Task 1) ; `SpawnEditorRequested` (4a) ; `HudMenuOpen`, `HudMenuSelected`, `HudText` (3b).
- Produces:
  - `AdminConfig : ModuleConfig { Version 1, bool SimpleAdminBridge = true }`.
  - `AdminModule` (`Name = "Admin"`, dépend de `Core`, `admin.json`) : commande `css_retake [edit]`, écoute `HudMenuSelected` du menu `admin.main`, méthodes internes `Execute(CCSPlayerController, AdminSelection)` et `ServerText(HudText)` réutilisées par le pont (Task 4).
  - Clés lang `admin.menu.*`, `admin.no_permission`, `admin.usage`, `admin.player_only`.

- [ ] **Step 1: Écrire le test qui échoue**

Ajouter à `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs` (dans la classe) :
```csharp
    [Theory]
    [InlineData("admin.menu.title")]
    [InlineData("admin.menu.editor")]
    [InlineData("admin.menu.forcesite")]
    [InlineData("admin.menu.force_once")]
    [InlineData("admin.menu.force_sticky")]
    [InlineData("admin.menu.force_off")]
    [InlineData("admin.menu.scramble")]
    [InlineData("admin.no_permission")]
    [InlineData("admin.usage")]
    [InlineData("admin.player_only")]
    public void Phase4bKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: `Failed: 10`.

- [ ] **Step 2: Implémentation**

`src/RetakeV4/Modules/Admin/AdminConfig.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Admin;

public sealed record AdminConfig : ModuleConfig
{
    public AdminConfig() => Version = 1;

    public bool SimpleAdminBridge { get; init; } = true;
}
```

`src/RetakeV4/Modules/Admin/AdminModule.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Admin;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Admin;

// Checks the permission, then hands every action to the module that owns it through the bus.
public sealed class AdminModule : IRetakeModule
{
    private const string AdminFlag = "@retakev4/admin";

    private AdminConfig _config = new();
    private ModuleContext? _context;

    public string Name => "Admin";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Admin module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("admin.json", new AdminConfig());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.Command("css_retake", "Retake admin menu: css_retake [edit]", OnRetakeCommand);
        hooks.OnBus<HudMenuSelected>(OnMenuSelected);
    }

    public void Unload() => _context = null;

    internal static bool IsAdmin(CCSPlayerController player) =>
        player.IsValid && AdminManager.PlayerHasPermissions(player, AdminFlag);

    internal void Execute(CCSPlayerController player, AdminSelection selection)
    {
        var id = new PlayerId(player.Slot);
        switch (selection.Action)
        {
            case AdminAction.SpawnEditor:
                Context.Bus.Publish(new SpawnEditorRequested(id));
                break;
            case AdminAction.Scramble:
                Context.Bus.Publish(new ScrambleRequested(id));
                break;
            case AdminAction.ForceSite when selection.Force is { } request:
                Context.Bus.Publish(new ForceSiteRequested(id, request));
                break;
        }
    }

    internal string ServerText(HudText text) =>
        text.Key is { } key ? Context.Text.Server(key, text.Args.ToArray()) : text.Literal ?? string.Empty;

    private void OnRetakeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            Server.PrintToConsole(Context.Text.Server("admin.player_only"));
            return;
        }
        if (!IsAdmin(player))
        {
            Context.Text.Chat(player, "admin.no_permission");
            return;
        }
        switch (RetakeCommand.Parse(command.ArgCount > 1 ? command.GetArg(1) : null))
        {
            case RetakeCommandKind.Menu:
                Context.Bus.Publish(new HudMenuOpen(new PlayerId(player.Slot), AdminMenu.Build()));
                break;
            case RetakeCommandKind.Editor:
                Execute(player, new AdminSelection(AdminAction.SpawnEditor));
                break;
            default:
                Context.Text.Chat(player, "admin.usage");
                break;
        }
    }

    private void OnMenuSelected(HudMenuSelected e)
    {
        if (e.MenuId != AdminMenu.MenuId || AdminMenu.Parse(e.ItemId) is not { } selection)
        {
            return;
        }
        var player = Utilities.GetPlayerFromSlot(e.Player.Slot);
        if (player is not { IsValid: true })
        {
            return;
        }
        if (!IsAdmin(player))
        {
            Context.Text.Chat(player, "admin.no_permission");
            return;
        }
        Execute(player, selection);
    }
}
```

Dans `src/RetakeV4/RetakeV4Plugin.cs`, ajouter `using RetakeV4.Modules.Admin;` et `new AdminModule(),` après `new InstaDefuseModule(),` dans la liste des modules.

Lang : ajouter à `src/RetakeV4/lang/en.json`
```json
  "admin.menu.title": "Retake admin",
  "admin.menu.editor": "Spawn editor",
  "admin.menu.forcesite": "Force the bombsite",
  "admin.menu.force_once": "Site {0} for the next round",
  "admin.menu.force_sticky": "Site {0} until cancelled",
  "admin.menu.force_off": "Cancel the forcing",
  "admin.menu.scramble": "Scramble teams at round end",
  "admin.no_permission": "You do not have permission to use the Retake admin menu.",
  "admin.usage": "Usage: !retake [edit]",
  "admin.player_only": "This command must be used in game."
```
et à `src/RetakeV4/lang/fr.json`
```json
  "admin.menu.title": "Admin Retake",
  "admin.menu.editor": "Éditeur de spawns",
  "admin.menu.forcesite": "Forcer le site",
  "admin.menu.force_once": "Site {0} au prochain round",
  "admin.menu.force_sticky": "Site {0} jusqu'à annulation",
  "admin.menu.force_off": "Annuler le forçage",
  "admin.menu.scramble": "Mélanger les équipes en fin de round",
  "admin.no_permission": "Tu n'as pas la permission d'utiliser le menu admin Retake.",
  "admin.usage": "Usage : !retake [edit]",
  "admin.player_only": "Cette commande s'utilise en jeu."
```

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Modules/Admin src/RetakeV4/RetakeV4Plugin.cs src/RetakeV4/lang tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs
git commit -m "feat: module Admin et menu HUD !retake (éditeur, forçage de site, scramble)"
```

---

### Task 4: Pont CS2-SimpleAdmin par réflexion

**Files:**
- Create: `src/RetakeV4/Modules/Admin/SimpleAdminBridge.cs`
- Modify: `src/RetakeV4/Modules/Admin/AdminModule.cs`

**Interfaces:**
- Consumes: `AdminMenu.Build`, `AdminMenu.Parse` (Task 1) ; `AllPluginsLoaded` (Task 1) ; `AdminModule.Execute`, `ServerText`, `IsAdmin` (Task 3).
- Produces: `internal sealed class SimpleAdminBridge` : `static SimpleAdminBridge? TryFind(ILogger)`, `Register(Menu menu, string permission, Func<HudText, string> label, Action<CCSPlayerController, string> select)`, `Unregister()`.

Code adaptateur (réflexion sur un plugin tiers) : vérifié par build et checklist (avec et sans CS2-SimpleAdmin).

- [ ] **Step 1: Pont**

`src/RetakeV4/Modules/Admin/SimpleAdminBridge.cs`
```csharp
using System.Reflection;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Admin;

// Optional CS2-SimpleAdmin integration without a compile-time dependency: methods are resolved on the public interface
// ICS2_SimpleAdminApi by exact signature, so an incompatible version fails loudly at registration instead of mid-game.
internal sealed class SimpleAdminBridge
{
    private const string ApiTypeName = "CS2_SimpleAdminApi.ICS2_SimpleAdminApi";
    private const string CategoryId = "retakev4";

    private readonly object _api;
    private readonly Type _apiType;
    private readonly ILogger _logger;
    private readonly List<string> _menus = new();

    private SimpleAdminBridge(object api, Type apiType, ILogger logger)
    {
        _api = api;
        _apiType = apiType;
        _logger = logger;
    }

    public static SimpleAdminBridge? TryFind(ILogger logger)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var apiType = assembly.GetType(ApiTypeName);
            if (apiType is null)
            {
                continue;
            }
            try
            {
                var capability = apiType.GetField("PluginCapability", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var api = capability?.GetType().GetMethod("Get", Type.EmptyTypes)?.Invoke(capability, null);
                if (api is not null)
                {
                    return new SimpleAdminBridge(api, apiType, logger);
                }
            }
            catch (TargetInvocationException ex)
            {
                logger.LogWarning(ex.InnerException ?? ex, "CS2-SimpleAdmin API found but not available");
            }
        }
        return null;
    }

    // Root actions become SimpleAdmin menu entries; a root submenu becomes a SimpleAdmin menu with one option per child.
    public void Register(Menu menu, string permission, Func<HudText, string> label, Action<CCSPlayerController, string> select)
    {
        var title = label(menu.Title);
        Invoke("RegisterMenuCategory", new[] { typeof(string), typeof(string), typeof(string) }, CategoryId, title, permission);
        foreach (var item in menu.Items)
        {
            Func<CCSPlayerController, object> factory = item.Submenu is { } submenu
                ? admin => SubmenuFor(admin, submenu, label, select)
                : admin =>
                {
                    select(admin, item.Id);
                    return CreateMenuWithBack(title, admin);
                };
            Invoke(
                "RegisterMenu",
                new[] { typeof(string), typeof(string), typeof(string), typeof(Func<CCSPlayerController, object>), typeof(string), typeof(string) },
                CategoryId, item.Id, label(item.Label), factory, permission, null);
            _menus.Add(item.Id);
        }
    }

    public void Unregister()
    {
        foreach (var menuId in _menus)
        {
            try
            {
                Invoke("UnregisterMenu", new[] { typeof(string), typeof(string) }, CategoryId, menuId);
            }
            catch (Exception ex) when (ex is TargetInvocationException or MissingMethodException)
            {
                _logger.LogWarning(ex, "Could not unregister SimpleAdmin menu {Menu}", menuId);
            }
        }
        _menus.Clear();
    }

    private object SubmenuFor(CCSPlayerController admin, Menu submenu, Func<HudText, string> label, Action<CCSPlayerController, string> select)
    {
        var menu = CreateMenuWithBack(label(submenu.Title), admin);
        foreach (var child in submenu.Items)
        {
            Action<CCSPlayerController> action = player => select(player, child.Id);
            Invoke(
                "AddMenuOption",
                new[] { typeof(object), typeof(string), typeof(Action<CCSPlayerController>), typeof(bool), typeof(string) },
                menu, label(child.Label), action, false, null);
        }
        return menu;
    }

    private object CreateMenuWithBack(string title, CCSPlayerController admin) =>
        Invoke("CreateMenuWithBack", new[] { typeof(string), typeof(string), typeof(CCSPlayerController) }, title, CategoryId, admin)
        ?? throw new InvalidOperationException("SimpleAdmin returned no menu");

    private object? Invoke(string name, Type[] signature, params object?[] args) =>
        (_apiType.GetMethod(name, signature) ?? throw new MissingMethodException(_apiType.FullName, name)).Invoke(_api, args);
}
```

- [ ] **Step 2: Branchement**

Dans `src/RetakeV4/Modules/Admin/AdminModule.cs` :
- ajouter `using System.Reflection;` et le champ `private SimpleAdminBridge? _bridge;` ;
- dans `Load`, ajouter à la fin :
```csharp
        if (_config.SimpleAdminBridge)
        {
            hooks.OnBus<AllPluginsLoaded>(_ => RegisterBridge());
        }
```
- remplacer `Unload` par :
```csharp
    public void Unload()
    {
        _bridge?.Unregister();
        _bridge = null;
        _context = null;
    }
```
- ajouter :
```csharp
    private void RegisterBridge()
    {
        _bridge?.Unregister();
        _bridge = SimpleAdminBridge.TryFind(Context.Logger);
        if (_bridge is null)
        {
            Context.Logger.LogInformation("CS2-SimpleAdmin not found: the Retake admin menu is available with !retake");
            return;
        }
        try
        {
            _bridge.Register(AdminMenu.Build(), AdminFlag, ServerText, OnBridgeSelection);
            Context.Logger.LogInformation("Retake admin entries registered in CS2-SimpleAdmin");
        }
        catch (Exception ex) when (ex is TargetInvocationException or MissingMethodException or ArgumentException or InvalidOperationException)
        {
            Context.Logger.LogWarning(ex, "CS2-SimpleAdmin API is not compatible: Retake entries were not added to its menu");
            _bridge.Unregister();
            _bridge = null;
        }
    }

    // Called by CS2-SimpleAdmin's menu: guarded like any other handler of this module.
    private void OnBridgeSelection(CCSPlayerController admin, string itemId) =>
        _context?.Guard.Run(Name, "simpleadmin", () =>
        {
            if (AdminMenu.Parse(itemId) is { } selection && IsAdmin(admin))
            {
                Execute(admin, selection);
            }
        });
```

- [ ] **Step 3: Vérifier**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Modules/Admin
git commit -m "feat: entrées Retake dans le menu CS2-SimpleAdmin (réflexion, optionnel)"
```

---

### Task 5: Module Links

**Files:**
- Create: `src/RetakeV4/Modules/Links/LinksConfig.cs`, `src/RetakeV4/Modules/Links/LinksConfigValidator.cs`, `src/RetakeV4/Modules/Links/LinksModule.cs`
- Modify: `src/RetakeV4/RetakeV4Plugin.cs`
- Test: `tests/RetakeV4.Integration.Tests/Modules/Links/LinksConfigValidatorTests.cs`

**Interfaces:**
- Consumes: `ModuleConfig`, `IConfigValidator<T>`, `ConfigIssue`, `ValidationResult<T>`.
- Produces:
  - `sealed record LinkConfig { IReadOnlyList<string> Commands; string Message }`, `sealed record LinksConfig : ModuleConfig { Version 1, IReadOnlyList<LinkConfig> Links = [] }`.
  - `LinksConfigValidator` (`MaxLinks = 32`, `MaxMessageLength = 512`, noms en minuscules).
  - `LinksModule` (`Name = "Links"`, dépend de `Core`, `links.json`) : une commande `css_<nom>` par nom validé.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/Modules/Links/LinksConfigValidatorTests.cs`
```csharp
using RetakeV4.Modules.Links;

namespace RetakeV4.Integration.Tests.Modules.Links;

public class LinksConfigValidatorTests
{
    private static readonly LinksConfig Defaults = new();
    private readonly LinksConfigValidator _validator = new();

    private static LinkConfig Link(string message, params string[] commands) => new() { Commands = commands, Message = message };

    [Fact]
    public void Defaults_AreEmptyAndValid()
    {
        var result = _validator.Validate(Defaults, Defaults, "links.json");
        Assert.Empty(result.Issues);
        Assert.Empty(result.Config.Links);
    }

    [Fact]
    public void ValidLinks_AreKept_WithLowercaseCommands()
    {
        var result = _validator.Validate(Defaults with { Links = new[] { Link("Discord: https://discord.gg/x", "Discord", "dc") } }, Defaults, "links.json");
        Assert.Empty(result.Issues);
        Assert.Equal(new[] { "discord", "dc" }, Assert.Single(result.Config.Links).Commands);
    }

    [Fact]
    public void InvalidLinks_AreSkipped_AndTheRestIsKept()
    {
        var config = Defaults with
        {
            Links = new[]
            {
                Link("ok", "site"),
                Link("reserved", "guns"),
                Link("plugin prefix", "retake_info"),
                Link("bad name", "dis cord"),
                Link("duplicate", "site"),
                Link("   ", "rules"),
                Link(new string('x', 513), "long"),
                Link("no command"),
                null!,
            },
        };
        var result = _validator.Validate(config, Defaults, "links.json");
        Assert.Equal(new[] { "site" }, Assert.Single(result.Config.Links).Commands);
        Assert.Equal(8, result.Issues.Count);
    }

    [Fact]
    public void TooManyLinks_AreCapped()
    {
        var links = Enumerable.Range(0, 40).Select(i => Link("msg", $"link{i}")).ToList();
        var result = _validator.Validate(Defaults with { Links = links }, Defaults, "links.json");
        Assert.Equal(LinksConfigValidator.MaxLinks, result.Config.Links.Count);
        Assert.Equal(8, result.Issues.Count);
    }

    [Fact]
    public void MissingList_FallsBackToEmpty()
    {
        var result = _validator.Validate(Defaults with { Links = null! }, Defaults, "links.json");
        Assert.Empty(result.Config.Links);
        Assert.Single(result.Issues);
    }
}
```

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Modules.Links` introuvable).

- [ ] **Step 2: Implémentation**

`src/RetakeV4/Modules/Links/LinksConfig.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Links;

public sealed record LinkConfig
{
    public IReadOnlyList<string> Commands { get; init; } = Array.Empty<string>();

    public string Message { get; init; } = string.Empty;
}

public sealed record LinksConfig : ModuleConfig
{
    public LinksConfig() => Version = 1;

    public IReadOnlyList<LinkConfig> Links { get; init; } = Array.Empty<LinkConfig>();
}
```

`src/RetakeV4/Modules/Links/LinksConfigValidator.cs`
```csharp
using System.Text.RegularExpressions;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Links;

public sealed partial class LinksConfigValidator : IConfigValidator<LinksConfig>
{
    public const int MaxLinks = 32;
    public const int MaxMessageLength = 512;

    // Commands the plugin already registers (weapon menu aliases, !awp): a link must never shadow them.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "guns", "gans", "gun", "g", "gns", "gnus", "weapon", "waepon", "weapons", "waepons", "waffen", "menu", "allocator", "select", "awp",
    };

    [GeneratedRegex("^[a-z0-9_]{1,32}$")]
    private static partial Regex CommandName();

    public ValidationResult<LinksConfig> Validate(LinksConfig config, LinksConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        if (config.Links is null)
        {
            issues.Add(new ConfigIssue(file, nameof(LinksConfig.Links), "missing; using an empty list"));
            return new ValidationResult<LinksConfig>(config with { Links = defaults.Links }, issues);
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<LinkConfig>();
        for (var i = 0; i < config.Links.Count; i++)
        {
            var key = $"Links[{i}]";
            if (Check(config.Links[i], used, kept.Count) is { } problem)
            {
                issues.Add(new ConfigIssue(file, key, $"{problem}; link ignored"));
                continue;
            }
            var commands = config.Links[i].Commands.Select(Normalize).ToList();
            used.UnionWith(commands);
            kept.Add(config.Links[i] with { Commands = commands });
        }
        return new ValidationResult<LinksConfig>(config with { Links = kept }, issues);
    }

    private static string? Check(LinkConfig? link, IReadOnlySet<string> used, int keptCount)
    {
        if (link is null)
        {
            return "empty entry";
        }
        if (keptCount >= MaxLinks)
        {
            return $"more than {MaxLinks} links";
        }
        var commands = (link.Commands ?? Array.Empty<string>()).Select(Normalize).ToList();
        if (commands.Count == 0)
        {
            return "no command";
        }
        if (commands.FirstOrDefault(c => !IsAllowed(c, used)) is { } refused)
        {
            return $"command '{refused}' is invalid, reserved or already used";
        }
        if (commands.Distinct(StringComparer.Ordinal).Count() != commands.Count)
        {
            return "duplicate command";
        }
        if (string.IsNullOrWhiteSpace(link.Message) || link.Message.Length > MaxMessageLength)
        {
            return $"message must be 1 to {MaxMessageLength} characters";
        }
        return null;
    }

    private static bool IsAllowed(string command, IReadOnlySet<string> used) =>
        CommandName().IsMatch(command)
        && !command.StartsWith("retake", StringComparison.Ordinal)
        && !Reserved.Contains(command)
        && !used.Contains(command);

    private static string Normalize(string? command) => (command ?? string.Empty).Trim().ToLowerInvariant();
}
```

`src/RetakeV4/Modules/Links/LinksModule.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Links;

// Community commands (!discord, !site...) from links.json; the message is server content, shown as written.
public sealed class LinksModule : IRetakeModule
{
    private LinksConfig _config = new();

    public string Name => "Links";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("links.json", new LinksConfig(), new LinksConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        foreach (var link in _config.Links)
        {
            var message = StringExtensions.ReplaceColorTags(link.Message);
            foreach (var command in link.Commands)
            {
                context.Hooks.Command($"css_{command}", "Community link", (player, _) => Show(player, message));
            }
        }
    }

    public void Unload()
    {
    }

    private static void Show(CCSPlayerController? player, string message)
    {
        if (player is { IsValid: true })
        {
            player.PrintToChat($" {message}");
            return;
        }
        Server.PrintToConsole(message);
    }
}
```

Dans `src/RetakeV4/RetakeV4Plugin.cs`, ajouter `using RetakeV4.Modules.Links;` et `new LinksModule(),` après `new AdminModule(),`.

- [ ] **Step 3: Vérifier le succès**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: `0 Avertissement(s)`, `Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/RetakeV4/Modules/Links src/RetakeV4/RetakeV4Plugin.cs tests/RetakeV4.Integration.Tests/Modules/Links
git commit -m "feat: module Links (commandes communautaires définies dans links.json)"
```

---

### Task 6: Checklist en jeu phase 4b, documentation et vérification finale

**Files:**
- Modify: `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`

- [ ] **Step 1: Checklist**

Ajouter à `docs/CHECKLIST-INGAME.md` :
```markdown

## Phase 4b — Menu admin, SimpleAdmin et Links
- [ ] `!retake` sans la permission `@retakev4/admin` : refusé ; `!retake` depuis la console serveur : message « s'utilise en jeu ».
- [ ] `!retake` (admin) : menu « Admin Retake » avec Éditeur de spawns, Forcer le site, Mélanger les équipes.
- [ ] Menu → Éditeur de spawns : l'éditeur s'ouvre (comme `css_retake_edit`) ; `!retake edit` fait de même ; `!retake foo` affiche l'usage.
- [ ] Menu → Forcer le site → « Site B au prochain round » : round suivant sur B ; « jusqu'à annulation » puis « Annuler le forçage » ; un site sans spawn est refusé avec le même message que la console.
- [ ] Menu → Mélanger les équipes : annonce dans le chat, équipes mélangées à la fin du round.
- [ ] Sans CS2-SimpleAdmin : log « CS2-SimpleAdmin not found », aucune erreur.
- [ ] Avec CS2-SimpleAdmin : catégorie « Admin Retake » dans son menu admin, les trois entrées fonctionnent (le forçage ouvre un sous-menu) ; `css_plugins reload RetakeV4` ne duplique pas les entrées.
- [ ] `admin.json` → `SimpleAdminBridge: false` : rien n'est ajouté au menu SimpleAdmin.
- [ ] `links.json` avec `{ "Commands": ["discord", "dc"], "Message": "{green}Discord :{default} https://discord.gg/xxx" }` : `!discord` et `!dc` affichent le message en couleur au joueur.
- [ ] `links.json` avec une commande `guns` ou `retake_test` : entrée ignorée, avertissement dans les logs, les autres liens marchent.
```

- [ ] **Step 2: CLAUDE.md**

Dans la section Règles de `CLAUDE.md`, ajouter :
```markdown
- Actions d'administration : le module Admin vérifie `@retakev4/admin` puis publie une demande (`SpawnEditorRequested`, `ForceSiteRequested`, `ScrambleRequested`) ; le module propriétaire l'applique. Le menu admin (`AdminMenu`, Domain) sert au HUD et au pont SimpleAdmin.
```

- [ ] **Step 3: Vérification finale**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo && dotnet test tests/RetakeV4.Domain.Tests --nologo -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`
puis (outil PowerShell) `pwsh -NoProfile -File scripts/package-dev.ps1`
Expected: 0 warning, `Failed: 0`, couverture Domain ≥ 80 %, `Package ready`.

- [ ] **Step 4: Commit**

```bash
git add docs/CHECKLIST-INGAME.md CLAUDE.md
git commit -m "docs: checklist en jeu phase 4b et règles d'administration"
```
