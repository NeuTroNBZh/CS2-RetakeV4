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
    ExitSave,
    ExitDiscard,
}

public sealed record CleanupEditorCommand(CleanupEditorCommandKind Kind, CleanupKind? Value = null);

public static class CleanupEditorMenu
{
    public const string MenuId = "mapcleanup.editor";
    private const string SetPrefix = "set:";

    private static readonly CleanupKind[] Choices = { CleanupKind.Door, CleanupKind.Window, CleanupKind.Vent, CleanupKind.Ignore };

    public static Menu Build(CleanupEditorView view)
    {
        var items = new List<MenuItem> { PickItem(view) };
        if (view.ClassName is not null)
        {
            items.AddRange(Choices.Select(kind => new MenuItem(SetPrefix + kind, KindLabel(kind), MenuItemKind.Choice,
                IsOn: view.HasOverride && view.Effective == kind)));
            items.Add(new MenuItem("auto", HudText.Of("mapcleanup.editor.auto", DetectedLabel(view.Detected)), MenuItemKind.Choice, IsOn: !view.HasOverride));
        }
        items.Add(new MenuItem("test", HudText.Of("mapcleanup.editor.test"), MenuItemKind.Action));
        items.Add(new MenuItem("save", HudText.Of("mapcleanup.editor.save"), MenuItemKind.Action));
        items.Add(ExitItem(view.Dirty));
        return new Menu(MenuId, HudText.Of("mapcleanup.editor.title"), items);
    }

    public static CleanupEditorCommand? Parse(string itemId)
    {
        if (itemId.StartsWith(SetPrefix, StringComparison.Ordinal))
        {
            var name = itemId[SetPrefix.Length..];
            return Choices.Where(k => k.ToString() == name).Select(k => new CleanupEditorCommand(CleanupEditorCommandKind.Set, k)).FirstOrDefault();
        }
        return itemId switch
        {
            "pick" => new CleanupEditorCommand(CleanupEditorCommandKind.Pick),
            "auto" => new CleanupEditorCommand(CleanupEditorCommandKind.Auto),
            "test" => new CleanupEditorCommand(CleanupEditorCommandKind.Test),
            "save" => new CleanupEditorCommand(CleanupEditorCommandKind.Save),
            "exit:save" => new CleanupEditorCommand(CleanupEditorCommandKind.ExitSave),
            "exit:discard" => new CleanupEditorCommand(CleanupEditorCommandKind.ExitDiscard),
            _ => null,
        };
    }

    private static MenuItem PickItem(CleanupEditorView view) => new("pick", view.ClassName is null
        ? HudText.Of("mapcleanup.editor.none_aimed")
        : HudText.Of("mapcleanup.editor.aimed", view.ClassName, view.ModelName ?? "-", view.TargetName ?? "-"), MenuItemKind.Action);

    // Leaving with unsaved corrections asks first; otherwise it closes directly.
    private static MenuItem ExitItem(bool dirty) => dirty
        ? new MenuItem("exit", HudText.Of("mapcleanup.editor.exit"), MenuItemKind.Submenu, Submenu: new Menu("mapcleanup.exit",
            HudText.Of("mapcleanup.editor.exit"), new[]
            {
                new MenuItem("exit:save", HudText.Of("mapcleanup.editor.exit_save"), MenuItemKind.Action),
                new MenuItem("exit:discard", HudText.Of("mapcleanup.editor.exit_discard"), MenuItemKind.Action),
            }))
        : new MenuItem("exit:discard", HudText.Of("mapcleanup.editor.exit"), MenuItemKind.Action);

    private static HudText KindLabel(CleanupKind kind) => HudText.Of($"mapcleanup.kind.{kind.ToString().ToLowerInvariant()}");

    private static HudText DetectedLabel(CleanupKind? kind) => kind is null ? HudText.Raw("-") : KindLabel(kind.Value);
}
