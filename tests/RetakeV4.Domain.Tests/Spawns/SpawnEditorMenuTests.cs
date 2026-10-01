using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class SpawnEditorMenuTests
{
    private static readonly SpawnPoint A = new(Guid.NewGuid(), TeamSide.T, BombSite.A, true, new Vec3(0f, 0f, 0f), new ViewAngles(0f, 0f));

    private static IReadOnlyList<string> Ids(Menu menu) => menu.Items.Select(i => i.Id).ToList();

    [Fact]
    public void CleanSet_WithoutNearestSpawn()
    {
        var menu = SpawnEditorMenu.Build(new SpawnEditorView(SpawnSet.Loaded(new[] { A }), null, false));
        Assert.Equal(SpawnEditorMenu.MenuId, menu.Id);
        Assert.Equal(new[] { "add", "teleport", SpawnEditorMenu.SaveId, SpawnEditorMenu.ReloadId, SpawnEditorMenu.NoclipId, SpawnEditorMenu.ExitId }, Ids(menu));
        Assert.Equal(MenuItemKind.Action, menu.Items[^1].Kind);
    }

    [Fact]
    public void NearestSpawn_OffersItsEdits()
    {
        var menu = SpawnEditorMenu.Build(new SpawnEditorView(SpawnSet.Loaded(new[] { A }), A, true));
        var nearest = Assert.Single(menu.Items, i => i.Id == "nearest");
        Assert.Equal(new object[] { "[T][A][C4] #01" }, nearest.Label.Args);
        Assert.Equal(
            new[] { SpawnEditorMenu.DeleteId, SpawnEditorMenu.TeamId, SpawnEditorMenu.SiteId, SpawnEditorMenu.PlantId },
            nearest.Submenu!.Items.Select(i => i.Id));
        Assert.True(nearest.Submenu.Items[^1].IsOn);
        Assert.True(Assert.Single(menu.Items, i => i.Id == SpawnEditorMenu.NoclipId).IsOn);
    }

    [Fact]
    public void DirtySet_ExitAsksForConfirmation()
    {
        var dirty = SpawnSet.Loaded(Array.Empty<SpawnPoint>()).Add(A);
        var menu = SpawnEditorMenu.Build(new SpawnEditorView(dirty, null, false));
        var exit = menu.Items[^1];
        Assert.Equal(MenuItemKind.Submenu, exit.Kind);
        Assert.Equal(new[] { SpawnEditorMenu.ExitSaveId, SpawnEditorMenu.ExitDiscardId }, exit.Submenu!.Items.Select(i => i.Id));
        Assert.DoesNotContain(menu.Items, i => i.Id == SpawnEditorMenu.ExitId);
    }

    [Fact]
    public void EmptySet_HasNoTeleportEntry() =>
        Assert.DoesNotContain(
            SpawnEditorMenu.Build(new SpawnEditorView(SpawnSet.Loaded(Array.Empty<SpawnPoint>()), null, false)).Items,
            i => i.Id == "teleport");

    [Fact]
    public void AddChoices_RoundTrip()
    {
        var add = SpawnEditorMenu.Build(new SpawnEditorView(SpawnSet.Loaded(Array.Empty<SpawnPoint>()), null, false)).Items[0].Submenu!;
        var parsed = add.Items.Select(i => SpawnEditorMenu.ParseAdd(i.Id)).ToList();
        Assert.Equal(6, parsed.Count);
        Assert.Contains(new SpawnToAdd(TeamSide.T, BombSite.B, true), parsed);
        Assert.Contains(new SpawnToAdd(TeamSide.CT, BombSite.A, false), parsed);
        Assert.Null(SpawnEditorMenu.ParseAdd("add:X:A"));
        Assert.Null(SpawnEditorMenu.ParseAdd(SpawnEditorMenu.SaveId));
    }

    [Fact]
    public void TeleportEntries_CarryTheSpawnId()
    {
        var menu = SpawnEditorMenu.Build(new SpawnEditorView(SpawnSet.Loaded(new[] { A }), null, false));
        var entry = Assert.Single(menu.Items, i => i.Id == "teleport").Submenu!.Items[0];
        Assert.Equal(A.Id, SpawnEditorMenu.ParseTeleport(entry.Id));
        Assert.Equal("[T][A][C4] #01", entry.Label.Literal);
        Assert.Null(SpawnEditorMenu.ParseTeleport("tp:nope"));
    }
}
