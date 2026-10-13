using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class RoundRecorderTests
{
    [Fact]
    public void FactsBeforeOpen_AreIgnored_AndCloseReturnsNull()
    {
        var recorder = new RoundRecorder(10, 1);
        Assert.False(recorder.Add(Shot(Ct1, 0, 1)));
        recorder.Add(Kill(Ct1, T1, 100));
        Assert.Null(recorder.Close(new RoundOutcome(TeamSide.CT, 7, 1000)));
    }

    [Fact]
    public void Close_ReturnsEveryFact_AndResets()
    {
        var recorder = new RoundRecorder(10, 1);
        recorder.Open(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)));
        recorder.Add(Shot(Ct1, 10, 1));
        recorder.Add(Hurt(Ct1, T1, 27, 73, 12, 1));
        recorder.Add(Kill(Ct1, T1, 500));
        recorder.Add(new GrenadeThrown(T1, GrenadeType.Flash, 50, null));
        recorder.Add(new GrenadeDetonated(T1, GrenadeType.Flash, 77, 1500, new Vec3(1, 2, 3)));
        recorder.Add(new PlayerBlinded(T1, Ct1, 77, 2.5f, 1500));
        recorder.Add(new BombPlantedFact(T1, 900));
        recorder.Add(new BombDefusedFact(Ct1, true, 30000));
        recorder.Add(new BombExplodedFact(40000));
        recorder.Add(new MvpAwarded(Ct1, 1));
        recorder.Add(new PlayerLeftRound(T1, 600));
        var record = recorder.Close(new RoundOutcome(TeamSide.CT, 7, 31000))!;
        Assert.Single(record.Shots);
        Assert.Single(record.Damages);
        Assert.Single(record.Kills);
        Assert.Single(record.GrenadesThrown);
        Assert.Single(record.GrenadesDetonated);
        Assert.Single(record.Blinds);
        Assert.NotNull(record.Plant);
        Assert.NotNull(record.Defuse);
        Assert.NotNull(record.Explosion);
        Assert.Single(record.Mvps);
        Assert.Single(record.Leavers);
        Assert.True(record.Ranked);
        Assert.False(recorder.IsOpen);
        Assert.Null(recorder.Close(new RoundOutcome(TeamSide.CT, 7, 1)));
    }

    [Fact]
    public void Abandon_DropsTheRound()
    {
        var recorder = new RoundRecorder(10, 1);
        recorder.Open(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)));
        recorder.Add(Kill(Ct1, T1, 500));
        recorder.Abandon();
        Assert.False(recorder.IsOpen);
        Assert.Null(recorder.Close(new RoundOutcome(TeamSide.CT, 7, 1)));
    }

    [Fact]
    public void Open_DiscardsAPreviousUnclosedRound()
    {
        var recorder = new RoundRecorder(10, 1);
        recorder.Open(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)));
        recorder.Add(Kill(Ct1, T1, 500));
        recorder.Open(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)));
        Assert.Empty(recorder.Close(new RoundOutcome(TeamSide.CT, 7, 1))!.Kills);
    }

    [Fact]
    public void Shots_BeyondTheCap_AreDropped_AndCounted()
    {
        var recorder = new RoundRecorder(2, 1);
        recorder.Open(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)));
        Assert.True(recorder.Add(Shot(Ct1, 1, 1)));
        Assert.True(recorder.Add(Shot(Ct1, 2, 2)));
        Assert.False(recorder.Add(Shot(Ct1, 3, 3)));
        Assert.False(recorder.Add(Shot(Ct1, 4, 4)));
        Assert.Equal(2, recorder.ShotsDropped);
        Assert.Equal(2, recorder.Close(new RoundOutcome(TeamSide.CT, 7, 1))!.Shots.Count);
        recorder.Open(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)));
        Assert.Equal(0, recorder.ShotsDropped);
    }

    [Theory]
    [InlineData(1, 1, 1, true)]
    [InlineData(2, 0, 1, false)]
    [InlineData(2, 1, 2, false)]
    [InlineData(2, 2, 2, true)]
    public void Ranked_NeedsTheMinimumOnBothSides(int ct, int t, int min, bool ranked)
    {
        var participants = Enumerable.Range(0, ct).Select(i => P((ulong)(100 + i), TeamSide.CT))
            .Concat(Enumerable.Range(0, t).Select(i => P((ulong)(200 + i), TeamSide.T)))
            .ToArray();
        Assert.Equal(ranked, Record(Context(participants), TeamSide.CT, minPlayersPerSide: min).Ranked);
    }
}
