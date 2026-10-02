using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.MapVote;

public static class MapVoteMenu
{
    public const string MenuId = "mapvote.menu";
    private const string Prefix = "map:";

    // Chat only: the vote runs during live rounds, where the movement-driven menus would freeze or steer players.
    public static Menu Build(IReadOnlyList<string> maps, string? chosen) => new(MenuId, HudText.Of("mapvote.menu.title"),
        maps.Select(m => new MenuItem(Prefix + m, HudText.Raw(m), MenuItemKind.Choice, IsOn: m == chosen)).ToList(), ChatOnly: true);

    public static string? Parse(string itemId, IReadOnlyList<string> maps) =>
        itemId.StartsWith(Prefix, StringComparison.Ordinal) && maps.Contains(itemId[Prefix.Length..]) ? itemId[Prefix.Length..] : null;
}
