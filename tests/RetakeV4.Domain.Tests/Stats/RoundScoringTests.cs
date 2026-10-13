using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class RoundScoringTests
{
    [Fact]
    public void Process_SetsRatingAndElo()
    {
        var record = Record(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T, 990)), TeamSide.CT, r =>
        {
            r.Add(Hurt(Ct1, T1, 100, 0, 900, 10));
            r.Add(Kill(Ct1, T1, 1000));
        });
        var summary = RoundScoring.Process(record, Rules);
        var winner = Of(summary, Ct1);
        var loser = Of(summary, T1);
        Assert.Equal(12, winner.EloDelta);
        Assert.Equal(1012, winner.EloAfter);
        Assert.Equal(-12, loser.EloDelta);
        Assert.Equal(978, loser.EloAfter);
        Assert.Equal(990, loser.EloBefore);
        Assert.True(winner.Rating > loser.Rating);
        Assert.Equal(RetakeRating.For(winner, Rules.Rating), winner.Rating);
    }

    [Fact]
    public void UnrankedRound_KeepsElo()
    {
        var record = Record(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)), TeamSide.CT, minPlayersPerSide: 2);
        var summary = RoundScoring.Process(record, Rules);
        Assert.All(summary.Players, p =>
        {
            Assert.Equal(0, p.EloDelta);
            Assert.Equal(p.EloBefore, p.EloAfter);
        });
    }
}
