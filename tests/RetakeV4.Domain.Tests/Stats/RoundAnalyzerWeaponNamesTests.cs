using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

// player_hurt names some weapons by their base item (M4A1-S → m4a1, USP-S → hkp2000) while weapon_fire and player_death
// give the real one: a bullet hit takes the weapon of the shot fired by the same player in the same tick.
public class RoundAnalyzerWeaponNamesTests
{
    private static RoundSummary Analyze(RoundRecord record) => RoundAnalyzer.Analyze(record, Rules);

    private static RoundContext OneVsOne() => Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T));

    [Theory]
    [InlineData("m4a1_silencer", "m4a1")]
    [InlineData("usp_silencer", "hkp2000")]
    public void BulletHit_TakesTheWeaponOfTheShotInTheSameTick(string fired, string hurt)
    {
        var s = Analyze(Record(OneVsOne(), TeamSide.CT, r =>
        {
            r.Add(Shot(Ct1, 1000, 100, fired));
            r.Add(Hurt(Ct1, T1, 100, 0, 1000, 100, hurt, hitgroup: StatsWeapons.HeadHitgroup));
            r.Add(Kill(Ct1, T1, 1000, fired, headshot: true));
        }));

        var weapon = Assert.Single(Of(s, Ct1).Weapons);
        Assert.Equal(fired, weapon.Weapon);
        Assert.Equal(1, weapon.Shots);
        Assert.Equal(1, weapon.Hits);
        Assert.Equal(1, weapon.HeadshotHits);
        Assert.Equal(100, weapon.Damage);
        Assert.Equal(1, weapon.Kills);
        Assert.Equal(fired, Assert.Single(s.Record.Damages).Weapon);
    }

    [Fact]
    public void BulletHit_WithoutAShotInTheSameTick_KeepsItsName()
    {
        var s = Analyze(Record(OneVsOne(), TeamSide.CT, r =>
        {
            r.Add(Shot(Ct1, 1000, 100, "m4a1_silencer"));
            r.Add(Hurt(Ct1, T1, 30, 70, 1100, 107, "m4a1"));
        }));

        Assert.Equal("m4a1", Assert.Single(s.Record.Damages).Weapon);
        Assert.Equal(30, Of(s, Ct1).Weapons.Single(w => w.Weapon == "m4a1").Damage);
    }

    [Fact]
    public void UtilityDamage_KeepsItsName_EvenWithAShotInTheSameTick()
    {
        var s = Analyze(Record(OneVsOne(), TeamSide.CT, r =>
        {
            r.Add(Shot(Ct1, 1000, 100, "m4a1_silencer"));
            r.Add(Hurt(Ct1, T1, 40, 60, 1000, 100, "hegrenade"));
        }));

        Assert.Equal("hegrenade", Assert.Single(s.Record.Damages).Weapon);
        Assert.Equal(40, Of(s, Ct1).UtilityDamage);
    }

    [Fact]
    public void ShotOfAnotherPlayerInTheSameTick_DoesNotRenameTheHit()
    {
        var s = Analyze(Record(OneVsOne(), TeamSide.CT, r =>
        {
            r.Add(Shot(T1, 1000, 100, "usp_silencer"));
            r.Add(Hurt(Ct1, T1, 30, 70, 1000, 100, "m4a1"));
        }));

        Assert.Equal("m4a1", Assert.Single(s.Record.Damages).Weapon);
    }
}
