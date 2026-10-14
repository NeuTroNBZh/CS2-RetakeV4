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

    // Only CT samples: no enemy is ever sampled, so nothing is behind a smoke.
    [Fact]
    public void InSmoke_UsesTheEyes_AndTheRadius()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.AddSmoke(1, 2000, new Vec3(0, 0, 0));
            r.Add(S(Ct1, 1875, 0, 0, -64));
            r.Add(S(Ct1, 2000, 72, 0, -64));
            r.Add(S(Ct1, 2125, 72, 0, -46, flags: MovementFlags.Crouched));
            r.Add(S(Ct2, 2000, 73, 0, -64));
            r.Add(S(Ct2, 2125, 0, 0, 8));
        }));
        Assert.Equal(2, M(s, Ct1).InSmokeSamples);
        Assert.Equal(66.67, M(s, Ct1).InSmokePct);
        Assert.Equal(1, M(s, Ct2).InSmokeSamples);
        Assert.Equal(0, M(s, Ct1).BehindSmokeSamples);
        Assert.Equal(0, M(s, Ct2).BehindSmokeSamples);
        Assert.Null(M(s, T1).InSmokePct);
    }

    [Fact]
    public void Smoke_IsActiveUntilExpiredOrEighteenSeconds()
    {
        var s = Analyze(Record(ThreeVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.AddSmoke(1, 2000, new Vec3(0, 0, 0));
            r.AddSmoke(2, 2000, new Vec3(1000, 0, 0));
            r.AddSmoke(3, 2000, new Vec3(2000, 0, 0));
            r.EndSmoke(1, 5000);
            r.EndSmoke(3, 30000);
            r.Add(S(Ct1, 4875, 0, 0, -64));
            r.Add(S(Ct1, 5000, 0, 0, -64));
            r.Add(S(Ct2, 19875, 1000, 0, -64));
            r.Add(S(Ct2, 20000, 1000, 0, -64));
            r.Add(S(Ct3, 19875, 2000, 0, -64));
            r.Add(S(Ct3, 20000, 2000, 0, -64));
        }));
        Assert.Equal(1, M(s, Ct1).InSmokeSamples);
        Assert.Equal(1, M(s, Ct2).InSmokeSamples);
        Assert.Equal(1, M(s, Ct3).InSmokeSamples);
    }

    [Fact]
    public void BehindSmoke_IsASmokeOnTheLineToTheEnemy()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.AddSmoke(1, 2000, new Vec3(0, 0, 0));
            r.Add(S(Ct1, 3000, -300, 0, -64));
            r.Add(S(T1, 3000, 300, 0, -64));
            r.Add(S(Ct1, 3125, -300, 72, -64));
            r.Add(S(T1, 3125, 300, 72, -64));
            r.Add(S(Ct1, 3250, -300, 73, -64));
            r.Add(S(T1, 3250, 300, 73, -64));
        }));
        Assert.Equal(2, M(s, Ct1).BehindSmokeSamples);
        Assert.Equal(66.67, M(s, Ct1).BehindSmokePct);
        Assert.Equal(2, M(s, T1).BehindSmokeSamples);
        Assert.Equal(0, M(s, Ct1).InSmokeSamples);
    }

    [Fact]
    public void BehindSmoke_LooksAtTheNearestEnemy_AndNotFromInsideASmoke()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.AddSmoke(1, 2000, new Vec3(0, 0, 0));
            r.Add(S(Ct1, 3000, -300, 0, -64));
            r.Add(S(T1, 3000, 300, 0, -64));
            r.Add(S(T2, 3000, -300, 200, -64));
            r.Add(S(Ct2, 3125, 0, 0, -64));
            r.Add(S(T1, 3125, 300, 0, -64));
        }));
        Assert.Equal(0, M(s, Ct1).BehindSmokeSamples);
        Assert.Equal(2, M(s, T1).BehindSmokeSamples);
        Assert.Equal(0, M(s, T2).BehindSmokeSamples);
        Assert.Equal(1, M(s, Ct2).InSmokeSamples);
        Assert.Equal(0, M(s, Ct2).BehindSmokeSamples);
    }

    [Fact]
    public void FirstContact_IsTheFirstBulletShot_OrEnemyDamage()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(Shot(Ct1, 1500, 150, "hegrenade"));
            r.Add(Hurt(T1, Ct1, 10, 90, 2000, 200));
            r.Add(Shot(Ct1, 2500, 250));
            r.Add(Hurt(Ct1, Ct2, 5, 95, 1800, 180));
            r.Add(Hurt(null, Ct2, 20, 75, 1900, 190));
            r.Add(Shot(Ct2, 4000, 400));
            r.Add(Shot(T2, 500, 50));
            r.Add(Shot(T2, 1200, 120, "knife"));
            r.Add(Hurt(T2, 0, 10, 90, 1100, 110, victimIsBot: true));
        }));
        Assert.Equal(1000, M(s, Ct1).FirstContactMs);
        Assert.Equal(1000, M(s, T1).FirstContactMs);
        Assert.Equal(3000, M(s, Ct2).FirstContactMs);
        Assert.Null(M(s, T2).FirstContactMs);
        Assert.Equal(1, M(s, Ct1).EngageOrder);
        Assert.Equal(2, M(s, Ct2).EngageOrder);
        Assert.Equal(1, M(s, T1).EngageOrder);
        Assert.Null(M(s, T2).EngageOrder);
    }

    [Fact]
    public void FirstContact_IgnoresSelfDamage()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(Hurt(Ct1, Ct1, 10, 90, 2000, 200));
        }));
        Assert.Null(M(s, Ct1).FirstContactMs);
        Assert.Null(M(s, Ct1).EngageOrder);
    }

    [Fact]
    public void EngageOrder_RanksWithinTheSide_TiesShareTheRank()
    {
        var s = Analyze(Record(ThreeVsTwo(), TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(Shot(Ct2, 2000, 200));
            r.Add(Shot(Ct1, 3000, 300));
            r.Add(Shot(Ct3, 3000, 301));
            r.Add(Shot(T1, 2500, 250));
        }));
        Assert.Equal(1, M(s, Ct2).EngageOrder);
        Assert.Equal(2, M(s, Ct1).EngageOrder);
        Assert.Equal(2, M(s, Ct3).EngageOrder);
        Assert.Equal(1, M(s, T1).EngageOrder);
        Assert.Equal(2000, M(s, Ct1).FirstContactMs);
    }

    // Ct2 dies to T1 at 10 s; Ct1 stands `distance` units away (BaitDistance 394, window 5 s). Ct3 has no sample.
    private static RoundRecord BaitRound(Action<RoundRecorder>? more = null, float distance = 393f, int durationMs = 40000, bool killPosition = true) =>
        Record(ThreeVsTwo(), TeamSide.T, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(S(Ct1, 9500, speed: 0f));
            r.Add(S(Ct2, 9500, distance));
            r.Add(Kill(T1, Ct2, 10000) with { VictimPosition = killPosition ? new Vec3(distance, 0, 0) : null });
            more?.Invoke(r);
        }, durationMs: durationMs);

    [Fact]
    public void Bait_WhenTheKillerSurvivesTheWindowUnanswered()
    {
        var s = Analyze(BaitRound());
        Assert.True(M(s, Ct1).BaitChance);
        Assert.True(M(s, Ct1).Bait);
        Assert.True(M(s, Ct1).BaitFlag);
        Assert.False(M(s, Ct3).BaitChance);
        Assert.Null(M(s, Ct3).BaitFlag);
        Assert.False(M(s, Ct2).BaitChance);
    }

    [Fact]
    public void Bait_UsesTheVictimsLastSample_WithoutAKillPosition() =>
        Assert.True(M(Analyze(BaitRound(killPosition: false)), Ct1).Bait);

    [Fact]
    public void Bait_NeedsATeammateCloserThanBaitDistance()
    {
        var m = M(Analyze(BaitRound(distance: 394f)), Ct1);
        Assert.False(m.BaitChance);
        Assert.Null(m.BaitFlag);
    }

    [Theory]
    [InlineData(15000, false)]
    [InlineData(15001, true)]
    public void Bait_DamageToTheKillerWithinTheWindow_IsAnAnsweredChance(int damageMs, bool bait)
    {
        var m = M(Analyze(BaitRound(r => r.Add(Hurt(Ct1, T1, 30, 70, damageMs, damageMs / 10)))), Ct1);
        Assert.True(m.BaitChance);
        Assert.Equal(bait, m.Bait);
    }

    [Fact]
    public void Bait_KillingTheKiller_IsAnAnsweredChance()
    {
        var m = M(Analyze(BaitRound(r => r.Add(Kill(Ct1, T1, 12000)))), Ct1);
        Assert.True(m.BaitChance);
        Assert.False(m.Bait);
    }

    [Theory]
    [InlineData(15000, false)]
    [InlineData(15001, true)]
    public void Bait_KillerDyingInTheWindow_AtTheWindowBoundary(int killerDiesMs, bool chance)
    {
        var m = M(Analyze(BaitRound(r => r.Add(Kill(Ct3, T1, killerDiesMs)))), Ct1);
        Assert.Equal(chance, m.BaitChance);
        Assert.Equal(chance, m.Bait);
    }

    [Theory]
    [InlineData(14999, false)]
    [InlineData(15000, true)]
    public void Bait_RoundEndingBeforeTheWindow_AtTheWindowBoundary(int durationMs, bool chance) =>
        Assert.Equal(chance, M(Analyze(BaitRound(durationMs: durationMs)), Ct1).BaitChance);

    [Fact]
    public void Bait_NoChance_WhenThePlayerDiesInTheWindow() =>
        Assert.False(M(Analyze(BaitRound(r => r.Add(Kill(T2, Ct1, 12000)))), Ct1).BaitChance);

    [Fact]
    public void Bait_IgnoresTeamKillsAndUnknownKillers()
    {
        foreach (var killer in new ulong?[] { Ct3, null })
        {
            var s = Analyze(Record(ThreeVsTwo(), TeamSide.T, r =>
            {
                r.MarkFreezeEnd(1000);
                r.Add(S(Ct1, 9500, speed: 0f));
                r.Add(Kill(killer, Ct2, 10000) with { VictimPosition = new Vec3(100, 0, 0) });
                r.Add(Kill(killer, Ct2, 10000) with { VictimPosition = new Vec3(100, 0, 0) });
            }));
            Assert.False(M(s, Ct1).BaitChance);
        }
    }

    // The player answers the first death (damage at 11 s); the same death arrives again at 12 s, unanswered in its own window.
    [Fact]
    public void Bait_ADuplicatedDeath_IsCountedOnce()
    {
        var m = M(Analyze(BaitRound(r =>
        {
            r.Add(Hurt(Ct1, T1, 30, 70, 11000, 1100));
            r.Add(Kill(T1, Ct2, 12000) with { VictimPosition = new Vec3(393, 0, 0) });
        })), Ct1);
        Assert.True(m.BaitChance);
        Assert.False(m.Bait);
    }

    [Fact]
    public void Bait_ASuicide_IsNoChance()
    {
        var s = Analyze(Record(ThreeVsTwo(), TeamSide.T, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(S(Ct1, 9500, speed: 0f));
            r.Add(Kill(Ct2, Ct2, 10000) with { VictimPosition = new Vec3(100, 0, 0) });
        }));
        Assert.False(M(s, Ct1).BaitChance);
        Assert.False(M(s, Ct2).BaitChance);
    }

    [Fact]
    public void Bait_SelfDamage_IsNoAnswer()
    {
        var m = M(Analyze(BaitRound(r => r.Add(Hurt(Ct1, Ct1, 10, 90, 11000, 1100)))), Ct1);
        Assert.True(m.BaitChance);
        Assert.True(m.Bait);
    }

    [Fact]
    public void Bait_TheVictimIsNeverItsOwnChance_EvenWithASampleNearTheKill()
    {
        var s = Analyze(BaitRound(r => r.Add(S(Ct2, 9990, 393f, speed: 0f))));
        Assert.False(M(s, Ct2).BaitChance);
        Assert.False(M(s, Ct2).Bait);
    }

    [Fact]
    public void Bait_DamageOrKillFlaggedAsAimedAtABot_IsNoAnswer()
    {
        var damage = M(Analyze(BaitRound(r => r.Add(Hurt(Ct1, T1, 30, 70, 11000, 1100, victimIsBot: true)))), Ct1);
        Assert.True(damage.BaitChance);
        Assert.True(damage.Bait);
        var kill = M(Analyze(BaitRound(r => r.Add(Kill(Ct1, T1, 12000, victimIsBot: true)))), Ct1);
        Assert.True(kill.BaitChance);
        Assert.True(kill.Bait);
    }
}
