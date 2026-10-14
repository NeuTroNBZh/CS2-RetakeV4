using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class MovementAnalyzerTests
{
    private static readonly StatsRules MoveRules = Rules with
    {
        Movement = new MovementRules(SmokeRadius: 72f, BaitDistance: 394f, BaitWindowSeconds: 5.0, AfkSeconds: 15.0),
    };

    private static RoundContext TwoVsTwo() => Context(P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T));

    private static RoundContext ThreeVsTwo() =>
        Context(P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(Ct3, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T));

    private static MovementSample S(ulong player, int timeMs, float x = 0f, float y = 0f, float z = 0f, float speed = 250f,
        MovementFlags flags = MovementFlags.None, float yaw = 0f, float pitch = 0f) =>
        new(player, timeMs, new Vec3(x, y, z), speed, yaw, pitch, flags, 100);

    // One still sample every 125 ms (8 per second), both ends included.
    private static void Still(RoundRecorder r, ulong player, int fromMs, int toMs, float x = 0f, float yaw = 0f, float pitch = 0f)
    {
        for (var t = fromMs; t <= toMs; t += 125)
        {
            r.Add(S(player, t, x, speed: 0f, yaw: yaw, pitch: pitch));
        }
    }

    private static RoundSummary Analyze(RoundRecord record) => RoundAnalyzer.Analyze(record, MoveRules);

    private static MovementRoundStats M(RoundSummary summary, ulong id) => Of(summary, id).Movement!;

    [Fact]
    public void WithoutMovementRules_OrFreezeEnd_MovementIsNull()
    {
        var record = Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(S(Ct1, 1000));
        });
        Assert.Null(Of(RoundAnalyzer.Analyze(record, Rules), Ct1).Movement);
        Assert.Null(Of(Analyze(Record(TwoVsTwo(), TeamSide.CT)), Ct1).Movement);
        Assert.NotNull(Of(Analyze(record), Ct1).Movement);
    }

    [Fact]
    public void AliveMs_RunsFromFreezeEndToDeathLeaveOrRoundEnd()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(Kill(T1, Ct2, 21000));
            r.Add(new PlayerLeftRound(T2, 5000));
        }));
        Assert.Equal(39000, M(s, Ct1).AliveMs);
        Assert.Equal(20000, M(s, Ct2).AliveMs);
        Assert.Equal(4000, M(s, T2).AliveMs);
        Assert.Equal(0, M(s, Ct1).AliveSamples);
        Assert.Null(M(s, Ct1).WalkPct);
    }

    [Fact]
    public void States_ArePartitioned_WithTheirBounds()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(S(Ct1, 1000, speed: 9.9f));
            r.Add(S(Ct1, 1125, speed: 10f, flags: MovementFlags.Walking));
            r.Add(S(Ct1, 1250, speed: 9.9f, flags: MovementFlags.Walking));
            r.Add(S(Ct1, 1375, speed: 10f));
            r.Add(S(Ct1, 1500, speed: 0f, flags: MovementFlags.Crouched));
            r.Add(S(Ct1, 1625, speed: 250f, flags: MovementFlags.Crouched | MovementFlags.Airborne));
            r.Add(S(Ct1, 1750, speed: 120f, flags: MovementFlags.Walking));
            r.Add(S(Ct1, 1875, speed: 250f));
        }));
        var m = M(s, Ct1);
        Assert.Equal(8, m.AliveSamples);
        Assert.Equal(2, m.StillSamples);
        Assert.Equal(2, m.WalkSamples);
        Assert.Equal(2, m.RunSamples);
        Assert.Equal(1, m.CrouchSamples);
        Assert.Equal(1, m.AirSamples);
        Assert.Equal(25.0, m.StillPct);
        Assert.Equal(12.5, m.AirPct);
    }

    [Fact]
    public void Samples_OutsideTheLivePhase_AfterDeath_OrOfOthers_AreIgnored()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(S(Ct1, 2000));
            r.Add(S(Ct1, 40001));
            r.Add(S(Ct2, 2000));
            r.Add(Kill(T1, Ct2, 3000));
            r.Add(S(Ct2, 3000));
            r.Add(S(Ct2, 3500));
            r.Add(S(99, 2000));
        }));
        Assert.Equal(1, M(s, Ct1).AliveSamples);
        Assert.Equal(1, M(s, Ct2).AliveSamples);
        Assert.DoesNotContain(s.Players, p => p.SteamId == 99);
    }

    [Fact]
    public void Distance_SumsTheHorizontalSteps()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(S(Ct1, 1000, 0, 0, 0));
            r.Add(S(Ct1, 1125, 300, 400, 50));
            r.Add(S(Ct1, 1250, 300, 400, 0));
        }));
        Assert.Equal(500.0, M(s, Ct1).DistanceUnits, 3);
        Assert.Equal(12.7, M(s, Ct1).DistanceMeters);
        Assert.Equal(1270L, M(s, Ct1).DistanceCm);
    }

    [Fact]
    public void Afk_CountsQuietPeriodsOfAtLeastAfkSeconds()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            Still(r, Ct1, 1000, 16000);
            Still(r, Ct2, 1000, 15875);
            r.Add(S(Ct2, 16000, 500));
        }));
        Assert.Equal(15000, M(s, Ct1).AfkMs);
        Assert.Equal(0, M(s, Ct2).AfkMs);
    }

    [Fact]
    public void Afk_SmallMovesAndTurnsDoNotBreakIt_BiggerOnesDo()
    {
        var s = Analyze(Record(ThreeVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            Still(r, Ct1, 1000, 8000);
            Still(r, Ct1, 8125, 16000, x: 78f);
            Still(r, Ct2, 1000, 8000);
            Still(r, Ct2, 8125, 16000, x: 79f);
            Still(r, T1, 1000, 8000, yaw: 179f);
            Still(r, T1, 8125, 16000, yaw: -176f);
            Still(r, T2, 1000, 8000);
            Still(r, T2, 8125, 16000, yaw: 5.5f);
            Still(r, Ct3, 1000, 8000);
            Still(r, Ct3, 8125, 16000, pitch: 6f);
        }));
        Assert.Equal(15000, M(s, Ct1).AfkMs);
        Assert.Equal(0, M(s, Ct2).AfkMs);
        Assert.Equal(15000, M(s, T1).AfkMs);
        Assert.Equal(0, M(s, T2).AfkMs);
        Assert.Equal(0, M(s, Ct3).AfkMs);
    }

    [Fact]
    public void Afk_AShotEndsThePeriod()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            Still(r, Ct1, 1000, 16000);
            r.Add(Shot(Ct1, 8000, 800, "hegrenade"));
            Still(r, Ct2, 1000, 36000);
            r.Add(Shot(Ct2, 18000, 1800));
        }));
        Assert.Equal(0, M(s, Ct1).AfkMs);
        Assert.Equal(16875 + 18000, M(s, Ct2).AfkMs);
    }
}
