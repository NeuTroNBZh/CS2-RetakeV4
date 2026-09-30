using RetakeV4.Domain.Common;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Common;

public class ChanceTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void NonPositiveChance_NeverSucceeds(double percent) =>
        Assert.False(Chance.Roll(percent, new FixedRandom { DoubleValue = 0.0 }));

    [Theory]
    [InlineData(100.0)]
    [InlineData(150.0)]
    public void FullChance_AlwaysSucceeds(double percent) =>
        Assert.True(Chance.Roll(percent, new FixedRandom { DoubleValue = 0.9999 }));

    [Theory]
    [InlineData(0.29, true)]
    [InlineData(0.30, true)]
    [InlineData(0.31, false)]
    public void PartialChance_ComparesDrawToPercent(double draw, bool expected) =>
        Assert.Equal(expected, Chance.Roll(30.0, new FixedRandom { DoubleValue = draw }));

    [Fact]
    public void Distribution_IsCloseToPercent()
    {
        var random = new SystemRandom(new Random(5));
        var successes = Enumerable.Range(0, 10_000).Count(_ => Chance.Roll(30.0, random));
        Assert.InRange(successes, 2700, 3300);
    }

    [Fact]
    public void PlantStep_RunsAfterLoadout() =>
        Assert.True(PreparationOrder.Plant > PreparationOrder.Loadout);
}
