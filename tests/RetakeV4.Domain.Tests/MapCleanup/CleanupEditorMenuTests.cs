using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class CleanupEditorMenuTests
{
    [Fact]
    public void Build_ListsChoicesAndActions_AndMarksTheEffectiveKind()
    {
        var menu = CleanupEditorMenu.Build(new CleanupEditorView("func_door", "door.vmdl", null, CleanupKind.Door, CleanupKind.Ignore, true, true));
        Assert.Equal(CleanupEditorMenu.MenuId, menu.Id);
        Assert.Contains(menu.Items, i => i.Id == "set:Ignore" && i.IsOn);
        Assert.Contains(menu.Items, i => i.Id == "set:Door" && !i.IsOn);
        Assert.Contains(menu.Items, i => i.Id == "auto" && !i.IsOn);
        Assert.Contains(menu.Items, i => i.Id == "save");
        Assert.Contains(menu.Items, i => i.Id == "exit" && i.Submenu is not null);
    }

    [Fact]
    public void Build_NothingAimed_OnlyPickTestSaveExit()
    {
        var menu = CleanupEditorMenu.Build(new CleanupEditorView(null, null, null, null, null, false, false));
        Assert.Equal(new[] { "pick", "test", "save", "exit:discard" }, menu.Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData("pick", CleanupEditorCommandKind.Pick, null)]
    [InlineData("set:Vent", CleanupEditorCommandKind.Set, CleanupKind.Vent)]
    [InlineData("auto", CleanupEditorCommandKind.Auto, null)]
    [InlineData("test", CleanupEditorCommandKind.Test, null)]
    [InlineData("save", CleanupEditorCommandKind.Save, null)]
    [InlineData("exit:save", CleanupEditorCommandKind.ExitSave, null)]
    [InlineData("exit:discard", CleanupEditorCommandKind.ExitDiscard, null)]
    public void Parse_KnownIds(string id, CleanupEditorCommandKind kind, CleanupKind? value)
    {
        Assert.Equal(new CleanupEditorCommand(kind, value), CleanupEditorMenu.Parse(id));
    }

    [Theory]
    [InlineData("set:Banana")]
    [InlineData("set:1")]
    [InlineData("nope")]
    public void Parse_Unknown_IsNull(string id)
    {
        Assert.Null(CleanupEditorMenu.Parse(id));
    }

    [Theory]
    [InlineData("models/props/de_nuke/windows/nuke_window_93x76.vmdl", "nuke_window_93x76")]
    [InlineData("maps/de_nuke/entities/unnamed_2_61814_21692.vmdl", "unnamed_2_61814_21692")]
    [InlineData(null, "-")]
    public void ShortModel_KeepsOnlyTheFileName(string? model, string expected)
    {
        Assert.Equal(expected, CleanupEditorMenu.ShortModel(model));
    }
}
