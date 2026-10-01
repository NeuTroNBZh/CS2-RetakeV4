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
}
