using RetakeV4.Domain.Common;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class SpawnArgsTests
{
    [Theory]
    [InlineData("T", TeamSide.T)]
    [InlineData("t", TeamSide.T)]
    [InlineData("2", TeamSide.T)]
    [InlineData("CT", TeamSide.CT)]
    [InlineData(" ct ", TeamSide.CT)]
    [InlineData("3", TeamSide.CT)]
    public void SpawnArgs_AcceptV3NumbersAndLetters(string value, TeamSide expected) => Assert.Equal(expected, SpawnArgs.Team(value));

    [Theory]
    [InlineData("A", BombSite.A)]
    [InlineData("0", BombSite.A)]
    [InlineData("b", BombSite.B)]
    [InlineData("1", BombSite.B)]
    public void Sites_AcceptV3NumbersAndLetters(string value, BombSite expected) => Assert.Equal(expected, SpawnArgs.Site(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("spec")]
    public void UnknownTeamsAndSites_AreRejected(string? value)
    {
        Assert.Null(SpawnArgs.Team(value));
        Assert.Null(SpawnArgs.Site(value));
    }

    [Theory]
    [InlineData("plant", true)]
    [InlineData("C4", true)]
    [InlineData(null, false)]
    [InlineData("no", false)]
    public void PlantFlag_IsOptional(string? value, bool expected) => Assert.Equal(expected, SpawnArgs.IsPlantFlag(value));

    [Fact]
    public void ForceSite_ParsesOnceStickyAndOff()
    {
        Assert.Equal(new SiteForce(BombSite.A, ForceSiteMode.Once), ForceSiteCommand.Parse("a", null)?.Force);
        Assert.Equal(new SiteForce(BombSite.B, ForceSiteMode.Sticky), ForceSiteCommand.Parse("B", "sticky")?.Force);
        Assert.Equal(new SiteForce(BombSite.B, ForceSiteMode.Once), ForceSiteCommand.Parse("1", "once")?.Force);
        var off = ForceSiteCommand.Parse("off", null);
        Assert.NotNull(off);
        Assert.Null(off.Force);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("C", null)]
    [InlineData("A", "forever")]
    public void ForceSite_RejectsInvalidArguments(string? site, string? mode) => Assert.Null(ForceSiteCommand.Parse(site, mode));
}
