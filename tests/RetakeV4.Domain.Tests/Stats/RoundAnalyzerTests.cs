using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class RoundAnalyzerTests
{
    private static RoundSummary Analyze(RoundRecord record) => RoundAnalyzer.Analyze(record, Rules);

    private static RoundContext TwoVsTwo() => Context(P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T));

    private static RoundContext ThreeVsThree() => Context(
        P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(Ct3, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T), P(T3, TeamSide.T));

    [Fact]
    public void Kill_CountsForAttackerAndVictim()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r => r.Add(Kill(Ct1, T1, 1000, weapon: "weapon_ak47", headshot: true))));
        var killer = Of(s, Ct1);
        Assert.Equal(1, killer.Kills);
        Assert.Equal(1, killer.HeadshotKills);
        Assert.True(killer.Survived);
        Assert.Equal(1, Of(s, T1).Deaths);
        Assert.False(Of(s, T1).Survived);
        var weapon = Assert.Single(killer.Weapons);
        Assert.Equal("ak47", weapon.Weapon);
        Assert.Equal(1, weapon.Kills);
        Assert.Equal(1, weapon.HeadshotKills);
    }

    [Fact]
    public void Assist_CountsOnlyForTheKillersTeammate()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Kill(Ct1, T1, 1000, assister: Ct2, flashAssist: true));
            r.Add(Kill(Ct1, T2, 2000, assister: T1));
        }));
        Assert.Equal(1, Of(s, Ct2).Assists);
        Assert.Equal(1, Of(s, Ct2).FlashAssists);
        Assert.Equal(0, Of(s, T1).Assists);
    }

    [Fact]
    public void TeamKill_IsNotAKill_ButKillsTheVictim()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.T, r => r.Add(Kill(Ct1, Ct2, 1000))));
        Assert.Equal(0, Of(s, Ct1).Kills);
        Assert.Equal(1, Of(s, Ct1).TeamKills);
        Assert.Equal(1, Of(s, Ct2).Deaths);
        Assert.False(Of(s, Ct1).EntryKill);
    }

    [Fact]
    public void Suicide_IsADeathWithoutKill()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.T, r =>
        {
            r.Add(Kill(null, Ct1, 1000, weapon: "world"));
            r.Add(Kill(Ct2, Ct2, 2000, weapon: "hegrenade"));
        }));
        foreach (var id in new[] { Ct1, Ct2 })
        {
            Assert.Equal(1, Of(s, id).Deaths);
            Assert.Equal(0, Of(s, id).Kills);
            Assert.Equal(0, Of(s, id).TeamKills);
            Assert.False(Of(s, id).EntryDeath);
        }
    }

    [Fact]
    public void KillOnABot_IsIgnored()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r => r.Add(Kill(Ct1, 0, 1000, victimIsBot: true))));
        Assert.Equal(0, Of(s, Ct1).Kills);
        Assert.False(Of(s, Ct1).EntryKill);
    }

    [Fact]
    public void Entry_IsTheEarliestEnemyKill()
    {
        var s = Analyze(Record(ThreeVsThree(), TeamSide.CT, r =>
        {
            r.Add(Kill(T3, T1, 500));
            r.Add(Kill(Ct2, T2, 1500));
            r.Add(Kill(Ct1, T3, 1000));
        }));
        Assert.True(Of(s, Ct1).EntryKill);
        Assert.True(Of(s, T3).EntryDeath);
        Assert.False(Of(s, Ct2).EntryKill);
        Assert.False(Of(s, T1).EntryDeath);
    }

    [Fact]
    public void Revenge_WithinTheWindow_IsATrade()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Kill(T1, Ct1, 1000));
            r.Add(Kill(Ct2, T1, 6000));
        }));
        Assert.Equal(1, Of(s, Ct2).TradeKills);
        Assert.True(Of(s, Ct1).TradedDeath);
        Assert.True(Of(s, Ct1).Kast);
    }

    [Fact]
    public void Revenge_AfterTheWindow_IsNotATrade()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Kill(T1, Ct1, 1000));
            r.Add(Kill(Ct2, T1, 6001));
        }));
        Assert.Equal(0, Of(s, Ct2).TradeKills);
        Assert.False(Of(s, Ct1).TradedDeath);
        Assert.False(Of(s, Ct1).Kast);
    }

    [Fact]
    public void OneRevenge_TradesOnlyOneDeath()
    {
        var s = Analyze(Record(ThreeVsThree(), TeamSide.CT, r =>
        {
            r.Add(Kill(T1, Ct1, 1000));
            r.Add(Kill(T1, Ct2, 1500));
            r.Add(Kill(Ct3, T1, 2000));
        }));
        Assert.Equal(1, Of(s, Ct3).TradeKills);
        Assert.True(Of(s, Ct1).TradedDeath);
        Assert.False(Of(s, Ct2).TradedDeath);
    }

    [Fact]
    public void Kast_ByCriterion()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.T, r =>
        {
            r.Add(Kill(Ct1, T1, 1000, assister: Ct2));
            r.Add(Kill(T2, Ct2, 2000));
            r.Add(Kill(T2, Ct1, 9000));
        }));
        Assert.True(Of(s, Ct1).Kast);   // kill
        Assert.True(Of(s, Ct2).Kast);   // assist, dead, not traded
        Assert.False(Of(s, T1).Kast);   // dead, nothing else
        Assert.True(Of(s, T2).Kast);    // survived (and kills)
    }

    [Fact]
    public void Damage_IsCappedByRemainingHealth_AndCountsEnemiesOnly()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Hurt(Ct1, T1, 80, 20, 100, 10));
            r.Add(Hurt(Ct2, T1, 120, 0, 200, 20));
            r.Add(Hurt(Ct1, Ct2, 30, 70, 300, 30));
            r.Add(Hurt(Ct2, T2, 40, 60, 400, 40, weapon: "hegrenade"));
            r.Add(Hurt(Ct2, 0, 50, 50, 500, 50, victimIsBot: true));
        }));
        Assert.Equal(80, Of(s, Ct1).Damage);
        Assert.Equal(60, Of(s, Ct2).Damage);
        Assert.Equal(40, Of(s, Ct2).UtilityDamage);
        Assert.Equal(20, Of(s, Ct2).Weapons.Single(w => w.Weapon == "ak47").Damage);
    }

    [Fact]
    public void LastAlive_AgainstTwo_IsA1v2Clutch()
    {
        var s = Analyze(Record(ThreeVsThree(), TeamSide.CT, r =>
        {
            r.Add(Kill(Ct1, T3, 500));
            r.Add(Kill(T1, Ct1, 1000));
            r.Add(Kill(T2, Ct2, 2000));
            r.Add(Kill(Ct3, T1, 3000));
            r.Add(Kill(Ct3, T2, 4000));
        }));
        Assert.Equal(2, Of(s, Ct3).ClutchVs);
        Assert.True(Of(s, Ct3).ClutchWon);
        Assert.Equal(1, Of(s, T2).ClutchVs);
        Assert.False(Of(s, T2).ClutchWon);
        Assert.Equal(0, Of(s, Ct1).ClutchVs);
    }

    [Fact]
    public void OneVsOne_FromTheStart_IsAClutchForBoth()
    {
        var s = Analyze(Record(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)), TeamSide.T));
        Assert.Equal(1, Of(s, Ct1).ClutchVs);
        Assert.False(Of(s, Ct1).ClutchWon);
        Assert.Equal(1, Of(s, T1).ClutchVs);
        Assert.True(Of(s, T1).ClutchWon);
    }

    [Fact]
    public void WinningSideWithEveryoneAlive_HasNoClutch()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Kill(Ct1, T1, 1000));
            r.Add(Kill(Ct1, T2, 2000));
        }));
        Assert.Equal(0, Of(s, Ct1).ClutchVs);
        Assert.Equal(0, Of(s, Ct2).ClutchVs);
        Assert.Equal(2, Of(s, T2).ClutchVs);
    }

    [Fact]
    public void KilledThenLeft_CountsOneDeath()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Kill(Ct1, T1, 1000));
            r.Add(Kill(Ct1, T1, 1100));
            r.Add(new PlayerLeftRound(T1, 1500));
            r.Add(new PlayerLeftRound(T2, 2000));
            r.Add(new PlayerLeftRound(T2, 2500));
        }));
        Assert.Equal(1, Of(s, T1).Deaths);
        Assert.Equal(1, Of(s, Ct1).Kills);
        Assert.Equal(1, Of(s, T2).Deaths);
        Assert.False(Of(s, T2).Survived);
        Assert.False(Of(s, T2).Kast);
    }

    [Fact]
    public void QuietRound_EveryoneSurvivesWithKast()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.T));
        Assert.Equal(4, s.Players.Count);
        Assert.All(s.Players, p =>
        {
            Assert.Equal(0, p.Kills);
            Assert.Equal(0, p.Deaths);
            Assert.True(p.Survived);
            Assert.True(p.Kast);
        });
        Assert.True(Of(s, T1).Won);
        Assert.False(Of(s, Ct1).Won);
    }

    [Fact]
    public void Draw_NobodyWins()
    {
        var s = Analyze(Record(TwoVsTwo(), null));
        Assert.All(s.Players, p => Assert.False(p.Won));
    }

    [Fact]
    public void Elo_IsCopiedFromTheParticipant_BeforeScoring()
    {
        var s = Analyze(Record(Context(P(Ct1, TeamSide.CT, 1234), P(T1, TeamSide.T)), TeamSide.CT));
        Assert.Equal(1234, Of(s, Ct1).EloBefore);
        Assert.Equal(1234, Of(s, Ct1).EloAfter);
        Assert.Equal(0, Of(s, Ct1).EloDelta);
    }
}
