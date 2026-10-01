using RetakeV4.Domain.Hud;
using RetakeV4.Domain.MapVote;

namespace RetakeV4.Domain.Tests.MapVote;

public class MapVoteMenuTests
{
    private static readonly string[] Maps = { "de_ancient", "de_nuke" };

    [Fact]
    public void Build_OneChoicePerMap_ChatOnly_ChosenMarked()
    {
        var menu = MapVoteMenu.Build(Maps, "de_nuke");
        Assert.Equal(MapVoteMenu.MenuId, menu.Id);
        Assert.True(menu.ChatOnly);
        Assert.Equal(new[] { "map:de_ancient", "map:de_nuke" }, menu.Items.Select(i => i.Id));
        Assert.All(menu.Items, i => Assert.Equal(MenuItemKind.Choice, i.Kind));
        Assert.True(menu.Items[1].IsOn);
        Assert.False(menu.Items[0].IsOn);
    }

    [Theory]
    [InlineData("map:de_nuke", "de_nuke")]
    [InlineData("map:de_dust2", null)]
    [InlineData("de_nuke", null)]
    public void Parse(string id, string? expected) => Assert.Equal(expected, MapVoteMenu.Parse(id, Maps));

    [Fact]
    public void OtherMenus_AreNotChatOnlyByDefault() =>
        Assert.False(new Menu("x", HudText.Raw("x"), Array.Empty<MenuItem>()).ChatOnly);
}
