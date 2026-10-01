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
