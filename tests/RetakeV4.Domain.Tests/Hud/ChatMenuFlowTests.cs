using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Tests.Hud;

public class ChatMenuFlowTests
{
    private static readonly Menu Root = new("root", HudText.Of("title"), new[]
    {
        new MenuItem("primary", HudText.Raw("Primary"), MenuItemKind.Submenu,
            Submenu: new Menu("weapons", HudText.Raw("Primary"), new[] { new MenuItem("ak", HudText.Raw("AK"), MenuItemKind.Choice) })),
        new MenuItem("awp", HudText.Raw("AWP"), MenuItemKind.Toggle),
    });

    [Fact]
    public void EnteringASubmenu_ShowsTheNextLevel()
    {
        var (next, outcome) = MenuNavigator.Open(Root).Activate(0);
        Assert.True(ChatMenuFlow.ShowsNextLevel(outcome));
        Assert.Equal("weapons", next.Current.Id);
    }

    [Fact]
    public void GoingBack_ShowsTheNextLevel()
    {
        var (inside, _) = MenuNavigator.Open(Root).Activate(0);
        var (_, outcome) = inside.Activate(1);
        Assert.True(ChatMenuFlow.ShowsNextLevel(outcome));
    }

    // A chat menu is not refreshed in place: after a choice it closes instead of reprinting the list.
    [Fact]
    public void Choosing_EndsTheChatMenu()
    {
        var (inside, _) = MenuNavigator.Open(Root).Activate(0);
        Assert.False(ChatMenuFlow.ShowsNextLevel(inside.Activate(0).Outcome));
        Assert.False(ChatMenuFlow.ShowsNextLevel(MenuNavigator.Open(Root).Activate(1).Outcome));
    }

    [Fact]
    public void Closing_EndsTheChatMenu() =>
        Assert.False(ChatMenuFlow.ShowsNextLevel(MenuNavigator.Open(Root).Activate(2).Outcome));

    private static Menu Long(int count) =>
        new("long", HudText.Raw("Long"), Enumerable.Range(1, count).Select(i => new MenuItem($"w{i}", HudText.Raw($"w{i}"), MenuItemKind.Choice)).ToList());

    // The chat menu paginates by itself: every item of the level is listed, then the exit line.
    [Fact]
    public void AllLines_ListEveryItem_ThenTheExitLine()
    {
        var lines = MenuNavigator.Open(Long(12)).AllLines();
        Assert.Equal(Enumerable.Range(1, 12).Select(i => $"w{i}").Append(MenuNavigator.CloseId), lines.Select(l => l.Id));
    }

    [Fact]
    public void AllLines_InASubmenu_EndWithBack()
    {
        var (inside, _) = MenuNavigator.Open(Root).Activate(0);
        Assert.Equal(new[] { "ak", MenuNavigator.BackId }, inside.AllLines().Select(l => l.Id));
    }

    [Fact]
    public void Choose_ByLineId_WorksBeyondTheFirstPage()
    {
        var (_, outcome) = MenuNavigator.Open(Long(12)).Choose("w11");
        Assert.Equal(MenuOutcome.Selected("w11"), outcome);
    }

    [Fact]
    public void Choose_Submenu_Back_And_Close()
    {
        var (inside, entered) = MenuNavigator.Open(Root).Choose("primary");
        Assert.Equal(("weapons", MenuOutcomeKind.None), (inside.Current.Id, entered.Kind));
        var (back, _) = inside.Choose(MenuNavigator.BackId);
        Assert.Equal("root", back.Current.Id);
        Assert.Equal(MenuOutcome.Closed, MenuNavigator.Open(Root).Choose(MenuNavigator.CloseId).Outcome);
    }

    // A line that no longer exists (the menu was refreshed) changes nothing: the level is shown again.
    [Fact]
    public void Choose_UnknownLine_KeepsTheLevel()
    {
        var navigator = MenuNavigator.Open(Root);
        var (next, outcome) = navigator.Choose("gone");
        Assert.Same(navigator, next);
        Assert.True(ChatMenuFlow.ShowsNextLevel(outcome));
    }
}
