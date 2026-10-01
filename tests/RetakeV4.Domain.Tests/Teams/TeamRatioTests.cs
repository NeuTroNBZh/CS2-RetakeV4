using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Teams;

public class TeamRatioTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 2, 1)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(6, 3, 3)]
    [InlineData(7, 4, 3)]
    [InlineData(8, 4, 4)]
    [InlineData(9, 5, 4)]
    public void Compute_MatchesV3Table(int total, int expectedCt, int expectedT)
    {
        Assert.Equal((expectedCt, expectedT), TeamRatio.Compute(total, 0.499));
    }

    [Fact]
    public void Compute_KeepsAtLeastOnePerSide()
    {
        Assert.Equal((1, 1), TeamRatio.Compute(2, 0.01));
        Assert.Equal((1, 1), TeamRatio.Compute(2, 0.99));
    }
}
