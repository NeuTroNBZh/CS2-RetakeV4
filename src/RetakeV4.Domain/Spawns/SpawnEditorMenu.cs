using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Spawns;

public sealed record SpawnEditorView(SpawnSet Set, SpawnPoint? Nearest, bool Noclip);

public sealed record SpawnToAdd(TeamSide Team, BombSite Site, bool CanPlant);

public enum NearestAction
{
    Delete,
    Team,
    Site,
    Plant,
}

// The spawn id travels with the action: the nearest spawn may change between the menu refresh and the click.
public sealed record NearestEdit(NearestAction Action, Guid Spawn);

public static class SpawnEditorMenu
{
    public const string MenuId = "spawns.editor";
    public const string SaveId = "save";
    public const string ReloadId = "reload";
    public const string NoclipId = "noclip";
    public const string ExitId = "exit";
    public const string ExitSaveId = "exit:save";
    public const string ExitDiscardId = "exit:discard";

    private const string AddPrefix = "add:";
    private const string TeleportPrefix = "tp:";
    private const string NearestPrefix = "nearest:";
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

    public static string NearestItemId(NearestEdit edit) => $"{NearestPrefix}{edit.Action}:{edit.Spawn}";

    public static NearestEdit? ParseNearest(string itemId)
    {
        var parts = itemId.Split(':');
        return parts.Length == 3 && parts[0] + ":" == NearestPrefix
            && Enum.TryParse<NearestAction>(parts[1], out var action) && Enum.IsDefined(action)
            && Guid.TryParse(parts[2], out var spawn)
                ? new NearestEdit(action, spawn)
                : null;
    }

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
            new MenuItem(Nearest(NearestAction.Delete, nearest), HudText.Of("spawns.editor.menu.delete"), MenuItemKind.Action),
            new MenuItem(Nearest(NearestAction.Team, nearest), HudText.Of("spawns.editor.menu.set_team", otherTeam.ToString()), MenuItemKind.Action),
            new MenuItem(Nearest(NearestAction.Site, nearest), HudText.Of("spawns.editor.menu.set_site", otherSite.ToString()), MenuItemKind.Action),
            new MenuItem(Nearest(NearestAction.Plant, nearest), HudText.Of("spawns.editor.menu.can_plant"), MenuItemKind.Toggle, IsOn: nearest.CanPlant),
        });
    }

    private static string Nearest(NearestAction action, SpawnPoint spawn) => NearestItemId(new NearestEdit(action, spawn.Id));

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
