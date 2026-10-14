using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class ScoreboardRankTests
{
    [Fact]
    public void LoadedElo_IsShownAsAPremierRating() =>
        Assert.Equal(new ScoreboardRank(ScoreboardRank.PremierRankType, 1248), ScoreboardRank.For(enabled: true, loaded: true, elo: 1248));

    [Fact]
    public void EloNotLoadedYet_ShowsNothing() =>
        Assert.Equal(ScoreboardRank.None, ScoreboardRank.For(enabled: true, loaded: false, elo: 1000));

    [Fact]
    public void Disabled_ShowsNothing() =>
        Assert.Equal(ScoreboardRank.None, ScoreboardRank.For(enabled: false, loaded: true, elo: 1248));

    [Fact]
    public void None_IsAnEmptyRank()
    {
        Assert.Equal(0, ScoreboardRank.None.RankType);
        Assert.Equal(0, ScoreboardRank.None.Value);
    }

    [Fact]
    public void PremierRankType_IsTheScoreboardRatingType() => Assert.Equal(11, ScoreboardRank.PremierRankType);

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void IsAllowed_OnlyWhenEnabled_AndServerGuidelinesAreOff(bool enabled, bool followsGuidelines, bool expected) =>
        Assert.Equal(expected, ScoreboardRank.IsAllowed(enabled, followsGuidelines));
}
