using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class EloCalculatorTests
{
    [Fact]
    public void EqualTeams_WinnerGainsHalfK_LoserLosesIt()
    {
        var deltas = EloCalculator.Deltas(new[] { P(Ct1, TeamSide.CT), P(T1, TeamSide.T) }, TeamSide.CT, true, Rules.Elo);
        Assert.Equal(12, deltas[Ct1]);
        Assert.Equal(-12, deltas[T1]);
    }

    [Fact]
    public void WinningOutnumbered_GainsMore()
    {
        var participants = new[] { P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T), P(T3, TeamSide.T) };
        var deltas = EloCalculator.Deltas(participants, TeamSide.CT, true, Rules.Elo);
        Assert.Equal(15, deltas[Ct1]);
        Assert.Equal(15, deltas[Ct2]);
        Assert.Equal(-15, deltas[T3]);
    }

    [Fact]
    public void Favourite_GainsLittle()
    {
        var deltas = EloCalculator.Deltas(new[] { P(Ct1, TeamSide.CT, 1400), P(T1, TeamSide.T, 1000) }, TeamSide.CT, true, Rules.Elo);
        Assert.Equal(2, deltas[Ct1]);
        Assert.Equal(-2, deltas[T1]);
    }

    [Theory]
    [InlineData(false, TeamSide.CT)]
    [InlineData(true, null)]
    public void UnrankedOrDraw_ChangesNothing(bool ranked, TeamSide? winner)
    {
        var deltas = EloCalculator.Deltas(new[] { P(Ct1, TeamSide.CT), P(T1, TeamSide.T) }, winner, ranked, Rules.Elo);
        Assert.All(deltas.Values, d => Assert.Equal(0, d));
    }

    [Fact]
    public void OneSideEmpty_ChangesNothing()
    {
        var deltas = EloCalculator.Deltas(new[] { P(Ct1, TeamSide.CT) }, TeamSide.CT, true, Rules.Elo);
        Assert.Equal(0, deltas[Ct1]);
    }

    [Theory]
    [InlineData(1000, 12, 1012)]
    [InlineData(105, -12, 100)]
    [InlineData(100, -12, 100)]
    public void Apply_RespectsTheFloor(int before, int delta, int expected) =>
        Assert.Equal(expected, EloCalculator.Apply(before, delta, Rules.Elo));
}
