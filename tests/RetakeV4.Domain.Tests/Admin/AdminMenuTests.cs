using RetakeV4.Domain.Admin;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Admin;

public class AdminMenuTests
{
    [Fact]
    public void Root_OffersEditorForceSiteAndScramble()
    {
        var menu = AdminMenu.Build();
        Assert.Equal(AdminMenu.MenuId, menu.Id);
        Assert.Equal(new[] { AdminMenu.EditorId, "forcesite", AdminMenu.ScrambleId }, menu.Items.Select(i => i.Id));
        Assert.Equal(MenuItemKind.Submenu, menu.Items[1].Kind);
    }

    [Fact]
    public void EveryItem_ParsesToItsAction()
    {
        var menu = AdminMenu.Build();
        Assert.Equal(new AdminSelection(AdminAction.SpawnEditor), AdminMenu.Parse(AdminMenu.EditorId));
        Assert.Equal(new AdminSelection(AdminAction.Scramble), AdminMenu.Parse(AdminMenu.ScrambleId));
        var forces = menu.Items[1].Submenu!.Items.Select(i => AdminMenu.Parse(i.Id)?.Force?.Force).ToList();
        Assert.Equal(
            new SiteForce?[]
            {
                new(BombSite.A, ForceSiteMode.Once), new(BombSite.A, ForceSiteMode.Sticky),
                new(BombSite.B, ForceSiteMode.Once), new(BombSite.B, ForceSiteMode.Sticky), null,
            },
            forces);
        Assert.Equal(new AdminSelection(AdminAction.ForceSite, new ForceSiteRequest(null)), AdminMenu.Parse(AdminMenu.ForceOffId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("forcesite")]
    [InlineData("force:C:Once")]
    [InlineData("force:A:Forever")]
    [InlineData("force:A")]
    [InlineData("allocation.weapons")]
    public void Parse_RejectsUnknownItems(string itemId) => Assert.Null(AdminMenu.Parse(itemId));

    [Theory]
    [InlineData(null, RetakeCommandKind.Menu)]
    [InlineData("", RetakeCommandKind.Menu)]
    [InlineData("edit", RetakeCommandKind.Editor)]
    [InlineData(" EDIT ", RetakeCommandKind.Editor)]
    [InlineData("editor", RetakeCommandKind.Editor)]
    [InlineData("scramble", RetakeCommandKind.Unknown)]
    public void RetakeCommand_ParsesTheSubcommand(string? argument, RetakeCommandKind expected) =>
        Assert.Equal(expected, RetakeCommand.Parse(argument));
}
