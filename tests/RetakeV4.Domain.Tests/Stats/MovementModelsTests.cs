using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class MovementModelsTests
{
    [Fact]
    public void Flags_UseTheReplayBits()
    {
        Assert.Equal((byte)1, (byte)MovementFlags.Crouched);
        Assert.Equal((byte)2, (byte)MovementFlags.Walking);
        Assert.Equal((byte)4, (byte)MovementFlags.Airborne);
        Assert.Equal((MovementFlags)7, MovementSample.FlagsOf(true, true, true));
        Assert.Equal(MovementFlags.Crouched, MovementSample.FlagsOf(true, false, false));
        Assert.Equal(MovementFlags.None, MovementSample.FlagsOf(false, false, false));
        var sample = new MovementSample(1, 0, new Vec3(1, 2, 3), 0f, 0f, 0f, MovementFlags.Walking | MovementFlags.Airborne, 100);
        Assert.False(sample.Crouched);
        Assert.True(sample.Walking);
        Assert.True(sample.Airborne);
    }

    [Fact]
    public void Eyes_AreAboveTheOrigin_LowerWhenCrouched()
    {
        Assert.Equal(new Vec3(1, 2, 67), new MovementSample(1, 0, new Vec3(1, 2, 3), 0f, 0f, 0f, MovementFlags.None, 100).Eyes);
        Assert.Equal(new Vec3(1, 2, 49), new MovementSample(1, 0, new Vec3(1, 2, 3), 0f, 0f, 0f, MovementFlags.Crouched, 100).Eyes);
    }

    [Fact]
    public void Percentages_AreOfTheAliveSamples_NullWithoutSamples()
    {
        var stats = new MovementRoundStats { AliveSamples = 3, WalkSamples = 1, RunSamples = 2, InSmokeSamples = 1 };
        Assert.Equal(33.33, stats.WalkPct);
        Assert.Equal(66.67, stats.RunPct);
        Assert.Equal(0.0, stats.CrouchPct);
        Assert.Equal(0.0, stats.StillPct);
        Assert.Equal(0.0, stats.AirPct);
        Assert.Equal(33.33, stats.InSmokePct);
        Assert.Equal(0.0, stats.BehindSmokePct);
        var empty = new MovementRoundStats();
        Assert.Null(empty.WalkPct);
        Assert.Null(empty.RunPct);
        Assert.Null(empty.CrouchPct);
        Assert.Null(empty.StillPct);
        Assert.Null(empty.AirPct);
        Assert.Null(empty.InSmokePct);
        Assert.Null(empty.BehindSmokePct);
    }

    [Fact]
    public void Distance_IsInMetersAndCentimeters()
    {
        Assert.Equal(12.7, new MovementRoundStats { DistanceUnits = 500 }.DistanceMeters);
        Assert.Equal(1270L, new MovementRoundStats { DistanceUnits = 500 }.DistanceCm);
        Assert.Equal(635L, new MovementRoundStats { DistanceUnits = 250 }.DistanceCm);
        Assert.Equal(0L, new MovementRoundStats().DistanceCm);
    }

    [Fact]
    public void BaitFlag_IsNullWithoutAChance()
    {
        Assert.Null(new MovementRoundStats().BaitFlag);
        Assert.False(new MovementRoundStats { BaitChance = true }.BaitFlag);
        Assert.True(new MovementRoundStats { BaitChance = true, Bait = true }.BaitFlag);
    }

    [Fact]
    public void WithoutSamples_DropsOnlyTheSamples()
    {
        var record = Record(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(0);
            r.Add(new MovementSample(Ct1, 10, default, 0f, 0f, 0f, MovementFlags.None, 100));
            r.AddSmoke(1, 10, default);
            r.Add(Kill(Ct1, T1, 500));
        });
        var summary = RoundAnalyzer.Analyze(record, Rules);
        var light = summary.WithoutSamples();
        Assert.Empty(light.Record.Samples);
        Assert.Single(summary.Record.Samples);
        Assert.Single(light.Record.Smokes);
        Assert.Single(light.Record.Kills);
        Assert.Equal(summary.Players, light.Players);
    }
}
