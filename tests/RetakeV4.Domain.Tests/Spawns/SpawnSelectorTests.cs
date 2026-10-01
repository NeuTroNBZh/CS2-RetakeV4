using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Spawns;

public class SpawnSelectorTests
{
    private static SpawnPoint Spawn(TeamSide team, BombSite site, bool canPlant = false) =>
        new(Guid.NewGuid(), team, site, canPlant, new Vec3(0, 0, 0), new ViewAngles(0, 0));

    private static SpawnRequest Player(int slot, TeamSide team) => new(new PlayerId(slot), team);

    [Fact]
    public void EachPlayerGetsADistinctSpawnOfTheirTeamAndSite()
    {
        var spawns = new[]
        {
            Spawn(TeamSide.T, BombSite.A, true), Spawn(TeamSide.T, BombSite.A), Spawn(TeamSide.CT, BombSite.A),
            Spawn(TeamSide.CT, BombSite.A), Spawn(TeamSide.CT, BombSite.B), Spawn(TeamSide.T, BombSite.B, true),
        };
        var players = new[] { Player(1, TeamSide.T), Player(2, TeamSide.T), Player(3, TeamSide.CT), Player(4, TeamSide.CT) };
        var result = SpawnSelector.Place(players, spawns, BombSite.A, new SystemRandom(new Random(3)));
        Assert.Equal(4, result.Assignments.Count);
        Assert.Equal(4, result.Assignments.Select(a => a.Spawn.Id).Distinct().Count());
        Assert.All(result.Assignments, a => Assert.Equal(BombSite.A, a.Spawn.Site));
        Assert.All(result.Assignments, a => Assert.Equal(players.Single(p => p.Player == a.Player).Team, a.Spawn.Team));
        Assert.Empty(result.Unplaced);
    }

    [Fact]
    public void Planter_IsATerrorist_OnACanPlantSpawn()
    {
        var spawns = new[] { Spawn(TeamSide.T, BombSite.A), Spawn(TeamSide.T, BombSite.A, true), Spawn(TeamSide.CT, BombSite.A) };
        var players = new[] { Player(1, TeamSide.CT), Player(2, TeamSide.T), Player(3, TeamSide.T) };
        var result = SpawnSelector.Place(players, spawns, BombSite.A, new FixedRandom(1));
        Assert.Equal(new PlayerId(3), result.Planter);
        Assert.True(result.Assignments.Single(a => a.Player == new PlayerId(3)).Spawn.CanPlant);
    }

    [Fact]
    public void Planter_FallsBackToAnyTerroristSpawn()
    {
        var spawns = new[] { Spawn(TeamSide.T, BombSite.A) };
        var result = SpawnSelector.Place(new[] { Player(1, TeamSide.T) }, spawns, BombSite.A, new FixedRandom(0));
        Assert.Equal(new PlayerId(1), result.Planter);
        Assert.Single(result.Assignments);
    }

    [Fact]
    public void NoTerrorist_MeansNoPlanter()
    {
        var result = SpawnSelector.Place(new[] { Player(1, TeamSide.CT) }, new[] { Spawn(TeamSide.CT, BombSite.A) }, BombSite.A, new FixedRandom(0));
        Assert.Null(result.Planter);
    }

    [Fact]
    public void MorePlayersThanSpawns_LeavesExtrasUnplaced()
    {
        var spawns = new[] { Spawn(TeamSide.T, BombSite.A, true), Spawn(TeamSide.CT, BombSite.A) };
        var players = new[] { Player(1, TeamSide.T), Player(2, TeamSide.T), Player(3, TeamSide.CT), Player(4, TeamSide.CT) };
        var result = SpawnSelector.Place(players, spawns, BombSite.A, new FixedRandom(0));
        Assert.Equal(2, result.Assignments.Count);
        Assert.Equal(2, result.Unplaced.Count);
        Assert.NotNull(result.Planter);
        Assert.Contains(result.Assignments, a => a.Player == result.Planter);
    }

    [Fact]
    public void SpawnsOfTheOtherSite_AreIgnored()
    {
        var spawns = new[] { Spawn(TeamSide.T, BombSite.B, true) };
        var result = SpawnSelector.Place(new[] { Player(1, TeamSide.T) }, spawns, BombSite.A, new FixedRandom(0));
        Assert.Empty(result.Assignments);
        Assert.Equal(new[] { new PlayerId(1) }, result.Unplaced);
        Assert.Equal(new PlayerId(1), result.Planter);
    }
}
