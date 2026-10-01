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
