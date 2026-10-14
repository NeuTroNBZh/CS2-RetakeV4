using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class RoundAnalyzerEventsTests
{
    private static RoundSummary Analyze(RoundRecord record) => RoundAnalyzer.Analyze(record, Rules);

    private static RoundContext TwoVsTwo() => Context(P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T));

    private static ShotFired Moving(ulong shooter, int tick, string weapon, float speed, bool scoped = false, bool airborne = false) =>
        Shot(shooter, tick * 10, tick, weapon) with { Motion = new ShotMotion(speed, false, scoped, airborne, false) };

    private static PlayerEventFact Ev(ulong player, PlayerEventType type, int timeMs, string? value = null) => new(player, type, timeMs, value, null);

    [Fact]
    public void Shots_AreSplitByMotion_AndHitsFollowTheirShot()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Moving(Ct1, 5, "ak47", 10f));
            r.Add(Moving(Ct1, 6, "ak47", 200f));
            r.Add(Moving(Ct1, 7, "awp", 20f, scoped: true));
            r.Add(Moving(Ct1, 9, "ak47", 0f, airborne: true));
            r.Add(Shot(Ct1, 80, 8));
            r.Add(Hurt(Ct1, T1, 10, 90, 50, 5));
            r.Add(Hurt(Ct1, T1, 10, 80, 70, 7, weapon: "awp"));
            r.Add(Hurt(Ct1, T1, 10, 70, 80, 8));
        }));
        var ct = Of(s, Ct1);
        Assert.Equal(5, ct.Shots);
        Assert.Equal(3, ct.Hits);
        Assert.Equal(2, ct.ShotsStill);
        Assert.Equal(2, ct.ShotsMoving);
        Assert.Equal(1, ct.ShotsScoped);
        Assert.Equal(2, ct.HitsStill);
        Assert.Equal(0, ct.HitsMoving);
        Assert.Equal(1, ct.HitsScoped);
        Assert.Equal(30, Of(s, T1).DamageTaken);
    }

    [Fact]
    public void PlayerEvents_AreCounted_ForParticipantsOnly()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Ev(Ct1, PlayerEventType.Reload, 100));
            r.Add(Ev(Ct1, PlayerEventType.ReloadFullClip, 200));
            r.Add(Ev(Ct1, PlayerEventType.Jump, 300));
            r.Add(Ev(Ct1, PlayerEventType.Jump, 400));
            r.Add(Ev(Ct1, PlayerEventType.DefuseBegin, 500, "kit"));
            r.Add(Ev(Ct1, PlayerEventType.DefuseAbort, 600));
            r.Add(Ev(Ct1, PlayerEventType.DefuseBegin, 700));
            r.Add(Ev(T1, PlayerEventType.PlantBegin, 100));
            r.Add(Ev(T1, PlayerEventType.PlantAbort, 200));
            r.Add(Ev(99, PlayerEventType.Jump, 300));
        }));
        var ct = Of(s, Ct1);
        Assert.Equal(2, ct.Reloads);
        Assert.Equal(1, ct.ReloadsFull);
        Assert.Equal(2, ct.Jumps);
        Assert.Equal(2, ct.DefuseAttempts);
        Assert.Equal(1, ct.DefuseAborts);
        Assert.Equal(1, Of(s, T1).PlantAttempts);
        Assert.Equal(1, Of(s, T1).PlantAborts);
        Assert.DoesNotContain(s.Players, p => p.SteamId == 99);
    }

    // Spawn equipment arrives during the freeze; weapons fall from a dead player.
    [Fact]
    public void Pickups_BeforeFreezeEnd_AndDropsAtDeath_AreNotCounted()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.T, r =>
        {
            r.MarkFreezeEnd(1000);
            r.Add(Ev(Ct1, PlayerEventType.Pickup, 500, "ak47"));
            r.Add(Ev(Ct1, PlayerEventType.Pickup, 1500, "awp"));
            r.Add(Ev(Ct1, PlayerEventType.Drop, 800, "ak47"));
            r.Add(Ev(Ct1, PlayerEventType.Drop, 2000, "ak47"));
            r.Add(Kill(T1, Ct1, 3000));
            r.Add(Ev(Ct1, PlayerEventType.Drop, 3000, "awp"));
            r.Add(Ev(Ct1, PlayerEventType.Drop, 3100, "deagle"));
        }));
        Assert.Equal(1, Of(s, Ct1).Pickups);
        Assert.Equal(1, Of(s, Ct1).Drops);
    }

    [Fact]
    public void WithoutFreezeEnd_EveryPickupCounts()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.T, r => r.Add(Ev(Ct1, PlayerEventType.Pickup, 0, "ak47"))));
        Assert.Equal(1, Of(s, Ct1).Pickups);
    }

    [Fact]
    public void DamageTaken_CountsEverySource_CappedByHealth()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.T, r =>
        {
            r.Add(Hurt(T1, Ct1, 60, 40, 100, 1));
            r.Add(Hurt(Ct2, Ct1, 30, 10, 200, 2));
            r.Add(Hurt(null, Ct1, 50, 0, 300, 3, weapon: "world"));
        }));
        Assert.Equal(100, Of(s, Ct1).DamageTaken);
        Assert.Equal(60, Of(s, T1).Damage);
    }

    [Fact]
    public void BombCarry_RunsFromPickupToDropPlantOrDeath()
    {
        var s = Analyze(Record(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T), P(T3, TeamSide.T)), TeamSide.T, r =>
        {
            r.Add(Ev(T1, PlayerEventType.BombPickup, 0, "start"));
            r.Add(Ev(T1, PlayerEventType.BombDrop, 4000));
            r.Add(Ev(T2, PlayerEventType.BombPickup, 5000));
            r.Add(new BombPlantedFact(T2, 9000));
            r.Add(Ev(T3, PlayerEventType.BombPickup, 20000));
            r.Add(Kill(Ct1, T3, 23000));
        }));
        Assert.Equal(4000, Of(s, T1).BombCarryMs);
        Assert.Equal(4000, Of(s, T2).BombCarryMs);
        Assert.Equal(3000, Of(s, T3).BombCarryMs);
        Assert.Equal(0, Of(s, Ct1).BombCarryMs);
    }

    [Fact]
    public void BombCarry_EndsWithTheRound_AndIgnoresADropByAnotherPlayer()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Ev(T1, PlayerEventType.BombPickup, 30000));
            r.Add(Ev(T2, PlayerEventType.BombDrop, 35000));
        }));
        Assert.Equal(10000, Of(s, T1).BombCarryMs);
        Assert.Equal(0, Of(s, T2).BombCarryMs);
    }

    [Theory]
    [InlineData(600, true)]
    [InlineData(999, true)]
    [InlineData(1000, false)]
    [InlineData(null, false)]
    public void ClutchDefuse_NeedsLessThanOneSecondLeft(int? remainingMs, bool expected)
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r => r.Add(new BombDefusedFact(Ct1, true, 30000) { RemainingMs = remainingMs })));
        Assert.Equal(expected, Of(s, Ct1).ClutchDefuse);
        Assert.True(Of(s, Ct1).Defused);
    }

    [Fact]
    public void EndStates_GiveHealthAndArmor_DeadPlayersHaveZero()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Kill(Ct1, T1, 1000));
            r.Add(new PlayerEndState(Ct1, 87, 50));
            r.Add(new PlayerEndState(T1, 0, 100));
            r.Add(new PlayerEndState(Ct2, -5, -1));
        }));
        Assert.Equal(87, Of(s, Ct1).HealthEnd);
        Assert.Equal(50, Of(s, Ct1).ArmorEnd);
        Assert.Equal(0, Of(s, T1).HealthEnd);
        Assert.Equal(0, Of(s, T1).ArmorEnd);
        Assert.Equal(0, Of(s, Ct2).HealthEnd);
        Assert.Equal(0, Of(s, Ct2).ArmorEnd);
        Assert.Null(Of(s, T2).HealthEnd);
        Assert.Null(Of(s, T2).ArmorEnd);
    }
}
