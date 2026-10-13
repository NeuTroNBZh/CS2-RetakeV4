using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class RoundAnalyzerUtilityTests
{
    private static RoundSummary Analyze(RoundRecord record) => RoundAnalyzer.Analyze(record, Rules);

    private static RoundContext TwoVsTwo() => Context(P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T));

    [Fact]
    public void Shots_CountBulletWeaponsOnly()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Shot(Ct1, 10, 1, "ak47"));
            r.Add(Shot(Ct1, 20, 2, "ak47"));
            r.Add(Shot(Ct1, 30, 3, "knife"));
            r.Add(Shot(Ct1, 40, 4, "hegrenade"));
            r.Add(Shot(Ct1, 50, 5, "taser"));
        }));
        Assert.Equal(2, Of(s, Ct1).Shots);
        Assert.Equal(2, Of(s, Ct1).Weapons.Single(w => w.Weapon == "ak47").Shots);
    }

    [Fact]
    public void Hits_AreDistinctPerTick_SoShotgunPelletsCountOnce()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(Shot(Ct1, 10, 100, "nova"));
            r.Add(Hurt(Ct1, T1, 20, 80, 10, 100, "nova", hitgroup: 1));
            r.Add(Hurt(Ct1, T1, 20, 60, 10, 100, "nova", hitgroup: 2));
            r.Add(Hurt(Ct1, T2, 20, 80, 10, 100, "nova", hitgroup: 1));
            r.Add(Hurt(Ct1, T1, 20, 40, 30, 130, "hegrenade"));
            r.Add(Hurt(Ct1, 0, 20, 80, 40, 140, "nova", victimIsBot: true));
        }));
        var player = Of(s, Ct1);
        Assert.Equal(1, player.Shots);
        Assert.Equal(1, player.Hits);
        Assert.Equal(1, player.HeadshotHits);
        Assert.Equal(1, player.Weapons.Single(w => w.Weapon == "nova").Hits);
    }

    [Fact]
    public void Flashes_SplitEnemiesAndTeammates_AndIgnoreSelf()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(new PlayerBlinded(Ct1, T1, 5, 2.5f, 100));
            r.Add(new PlayerBlinded(Ct1, T2, 5, 1.25f, 100));
            r.Add(new PlayerBlinded(Ct1, Ct2, 5, 3f, 100));
            r.Add(new PlayerBlinded(Ct1, Ct1, 5, 3f, 100));
            r.Add(new PlayerBlinded(Ct1, T1, 6, 0f, 200));
        }));
        var player = Of(s, Ct1);
        Assert.Equal(2, player.EnemiesFlashed);
        Assert.Equal(1, player.TeammatesFlashed);
        Assert.Equal(3.75, player.BlindSecondsInflicted, 3);
    }

    [Fact]
    public void Objectives_MarkPlanterDefuserAndMvp()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(new BombPlantedFact(T1, 1000));
            r.Add(new BombDefusedFact(Ct2, true, 30000));
            r.Add(new MvpAwarded(Ct2, 3));
        }));
        Assert.True(Of(s, T1).Planted);
        Assert.True(Of(s, Ct2).Defused);
        Assert.True(Of(s, Ct2).DefusedWithKit);
        Assert.True(Of(s, Ct2).Mvp);
        Assert.False(Of(s, Ct1).Mvp);
    }

    [Fact]
    public void Grenades_LinkThrowDetonationBlindsAndDamage()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
        {
            r.Add(new GrenadeThrown(Ct1, GrenadeType.Flash, 100, new Vec3(0, 0, 0)));
            r.Add(new PlayerBlinded(Ct1, T1, 42, 2f, 1490));
            r.Add(new PlayerBlinded(Ct1, Ct2, 42, 1f, 1490));
            r.Add(new GrenadeDetonated(Ct1, GrenadeType.Flash, 42, 1500, new Vec3(1, 1, 1)));
            r.Add(new GrenadeThrown(Ct1, GrenadeType.He, 2000, null));
            r.Add(new GrenadeDetonated(Ct1, GrenadeType.He, 43, 3500, new Vec3(2, 2, 2)));
            r.Add(Hurt(Ct1, T1, 57, 43, 3500, 300, "hegrenade"));
            r.Add(Hurt(Ct1, Ct2, 10, 90, 3500, 300, "hegrenade"));
            r.Add(new GrenadeThrown(T2, GrenadeType.Smoke, 4000, null));
        }));
        Assert.Equal(3, s.Grenades.Count);
        var flash = s.Grenades.Single(g => g.Type == GrenadeType.Flash);
        Assert.Equal(100, flash.ThrownMs);
        Assert.Equal(1500, flash.DetonatedMs);
        Assert.Equal(1, flash.EnemiesBlinded);
        Assert.Equal(1, flash.TeammatesBlinded);
        Assert.Equal(2.0, flash.BlindSeconds, 3);
        var he = s.Grenades.Single(g => g.Type == GrenadeType.He);
        Assert.Equal(57, he.Damage);
        var smoke = s.Grenades.Single(g => g.Type == GrenadeType.Smoke);
        Assert.Null(smoke.DetonatedMs);
        Assert.Equal(4000, smoke.ThrownMs);
    }

    [Fact]
    public void Detonation_WithoutAThrow_IsKept()
    {
        var s = Analyze(Record(TwoVsTwo(), TeamSide.CT, r =>
            r.Add(new GrenadeDetonated(T1, GrenadeType.Molotov, null, 800, new Vec3(3, 3, 3)))));
        var molotov = Assert.Single(s.Grenades);
        Assert.Null(molotov.ThrownMs);
        Assert.Equal(800, molotov.DetonatedMs);
    }
}
