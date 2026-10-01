using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class SpawnSetTests
{
    private static SpawnPoint Spawn(TeamSide team, BombSite site, float x, bool canPlant = false) =>
        new(Guid.NewGuid(), team, site, canPlant, new Vec3(x, 0f, 0f), new ViewAngles(0f, 90f));

    [Fact]
    public void Loaded_IsClean_AndChangesMakeItDirty()
    {
        var a = Spawn(TeamSide.T, BombSite.A, 0f);
        var set = SpawnSet.Loaded(new[] { a });
        Assert.False(set.Dirty);
        Assert.True(set.Add(Spawn(TeamSide.CT, BombSite.B, 10f)).Dirty);
        Assert.True(set.Remove(a.Id).Dirty);
        Assert.True(set.Update(a.Id, new SpawnChange(CanPlant: true)).Dirty);
        Assert.False(set.Add(Spawn(TeamSide.CT, BombSite.B, 10f)).MarkSaved().Dirty);
    }

    [Fact]
    public void UnknownIds_LeaveTheSetUnchanged()
    {
        var set = SpawnSet.Loaded(new[] { Spawn(TeamSide.T, BombSite.A, 0f) });
        Assert.Same(set, set.Remove(Guid.NewGuid()));
        Assert.Same(set, set.Update(Guid.NewGuid(), new SpawnChange(Team: TeamSide.CT)));
    }

    [Fact]
    public void Update_ChangesOnlyTheGivenFields()
    {
        var a = Spawn(TeamSide.T, BombSite.A, 0f);
        var updated = Assert.Single(SpawnSet.Loaded(new[] { a }).Update(a.Id, new SpawnChange(Site: BombSite.B, CanPlant: true)).Spawns);
        Assert.Equal(a with { Site = BombSite.B, CanPlant = true }, updated);
    }

    [Fact]
    public void Nearest_IsTheClosestWithinTheDistance()
    {
        var near = Spawn(TeamSide.T, BombSite.A, 50f);
        var far = Spawn(TeamSide.CT, BombSite.A, 120f);
        var set = SpawnSet.Loaded(new[] { far, near });
        Assert.Equal(near, set.Nearest(new Vec3(40f, 0f, 0f), 150f));
        Assert.Null(set.Nearest(new Vec3(400f, 0f, 0f), 150f));
        Assert.Null(SpawnSet.Loaded(Array.Empty<SpawnPoint>()).Nearest(Vec3Zero(), 150f));
    }

    [Fact]
    public void Labels_AreNumberedFromOne_AndShowThePlantFlag()
    {
        var a = Spawn(TeamSide.T, BombSite.A, 0f, canPlant: true);
        var b = Spawn(TeamSide.CT, BombSite.B, 10f);
        var set = SpawnSet.Loaded(new[] { a, b });
        Assert.Equal("[T][A][C4] #01", set.Label(a));
        Assert.Equal("[CT][B] #02", set.Label(b));
        Assert.Equal(-1, set.IndexOf(Guid.NewGuid()));
        Assert.Equal(1, set.Count(TeamSide.CT));
    }

    private static Vec3 Vec3Zero() => new(0f, 0f, 0f);
}
