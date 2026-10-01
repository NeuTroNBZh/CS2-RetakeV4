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

    // The center panel wraps: going up from the first line lands on the last, and down from the last on the first.
    [Fact]
    public void MoveWrapping_GoesAroundTheEnds()
    {
        var navigator = MenuNavigator.Open(Root());
        var last = navigator.Lines().Count - 1;
        Assert.Equal(last, navigator.MoveWrapping(-1).Cursor);
        Assert.Equal(0, navigator.MoveWrapping(-1).MoveWrapping(1).Cursor);
        Assert.Equal(1, navigator.MoveWrapping(1).Cursor);
    }
}
