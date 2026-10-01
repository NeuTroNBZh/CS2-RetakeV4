using RetakeV4.Domain.Common;
using RetakeV4.Domain.Plant;

namespace RetakeV4.Domain.Tests.Plant;

public class PlantRulesTests
{
    [Fact]
    public void CanAutoPlant_WithPlanterSiteAndPlayers() =>
        Assert.True(PlantRules.CanAutoPlant(false, 2, new PlayerId(1), BombSite.A));

    [Fact]
    public void CanAutoPlant_NotDuringWarmup() =>
        Assert.False(PlantRules.CanAutoPlant(true, 5, new PlayerId(1), BombSite.A));

    [Fact]
    public void CanAutoPlant_NotWithFewerThanTwoPlayers() =>
        Assert.False(PlantRules.CanAutoPlant(false, 1, new PlayerId(1), BombSite.A));

    [Fact]
    public void CanAutoPlant_NotWithoutPlanter() =>
        Assert.False(PlantRules.CanAutoPlant(false, 4, null, BombSite.B));

    [Fact]
    public void CanAutoPlant_NotWithoutSite() =>
        Assert.False(PlantRules.CanAutoPlant(false, 4, new PlayerId(1), null));
}
