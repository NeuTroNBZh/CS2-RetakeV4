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

    [Fact]
    public void Evaluate_Plants_WhenEverythingIsReady() =>
        Assert.Equal(AutoPlantDecision.Plant, PlantRules.Evaluate(false, 2, new PlayerId(1), BombSite.A));

    [Fact]
    public void Evaluate_ReportsWarmup() =>
        Assert.Equal(AutoPlantDecision.Warmup, PlantRules.Evaluate(true, 5, new PlayerId(1), BombSite.A));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Evaluate_ReportsNotEnoughPlayers(int players) =>
        Assert.Equal(AutoPlantDecision.NotEnoughPlayers, PlantRules.Evaluate(false, players, new PlayerId(1), BombSite.A));

    [Fact]
    public void Evaluate_ReportsMissingPlanter() =>
        Assert.Equal(AutoPlantDecision.NoPlanter, PlantRules.Evaluate(false, 4, null, BombSite.B));

    [Fact]
    public void Evaluate_ReportsMissingSite() =>
        Assert.Equal(AutoPlantDecision.NoSite, PlantRules.Evaluate(false, 4, new PlayerId(1), null));
}
