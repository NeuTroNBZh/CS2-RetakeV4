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
        var exit = ExitLine();
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

    // Unpaged view for displays that paginate by themselves (chat menus): every item, then back or close.
    public IReadOnlyList<MenuLine> AllLines() => Current.Items.Select(ToLine).Append(ExitLine()).ToList();

    // Activation by line id, independent of the page: an id no longer in the level changes nothing.
    public (MenuNavigator Next, MenuOutcome Outcome) Choose(string lineId)
    {
        if (lineId == (Path.IsEmpty ? CloseId : BackId))
        {
            return Path.IsEmpty ? (this, MenuOutcome.Closed) : (Pop(), MenuOutcome.None);
        }
        var item = Current.Items.FirstOrDefault(i => i.Id == lineId);
        return item is null ? (this, MenuOutcome.None) : ActivateItem(item, 0);
    }

    public MenuNavigator MoveWrapping(int delta)
    {
        var count = Lines().Count;
        return this with { Cursor = ((Cursor + delta) % count + count) % count };
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

    private MenuLine ExitLine() => Path.IsEmpty
        ? new MenuLine(CloseId, HudText.Of("hud.menu.close"), MenuLineKind.Close)
        : new MenuLine(BackId, HudText.Of("hud.menu.back"), MenuLineKind.Back);

    private static MenuLine ToLine(MenuItem item) => new(item.Id, item.Label, MenuLineKind.Item, item.Kind, item.IsOn, item.Team);

    private static int PageCount(int itemCount) => (itemCount + ItemsPerPage - 1) / ItemsPerPage;

    private static int ClampPage(int page, int itemCount) =>
        itemCount < MaxLines ? 0 : Math.Clamp(page, 0, PageCount(itemCount) - 1);
}
