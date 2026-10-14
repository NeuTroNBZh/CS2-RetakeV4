using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class StatsWeaponsTests
{
    [Theory]
    [InlineData("weapon_AK47", "ak47")]
    [InlineData(" m4a1_silencer ", "m4a1_silencer")]
    [InlineData(null, "")]
    public void Normalize_StripsPrefixAndCase(string? raw, string expected) => Assert.Equal(expected, StatsWeapons.Normalize(raw));

    [Theory]
    [InlineData("weapon_ak47", true)]
    [InlineData("deagle", true)]
    [InlineData("weapon_knife", false)]
    [InlineData("weapon_knife_karambit", false)]
    [InlineData("weapon_bayonet", false)]
    [InlineData("hegrenade", false)]
    [InlineData("inferno", false)]
    [InlineData("weapon_taser", false)]
    [InlineData("weapon_c4", false)]
    [InlineData("", false)]
    public void IsBullet(string weapon, bool expected) => Assert.Equal(expected, StatsWeapons.IsBullet(weapon));

    [Theory]
    [InlineData("hegrenade", true)]
    [InlineData("inferno", true)]
    [InlineData("weapon_molotov", true)]
    [InlineData("flashbang", false)]
    [InlineData("ak47", false)]
    public void IsUtility(string weapon, bool expected) => Assert.Equal(expected, StatsWeapons.IsUtility(weapon));

    [Theory]
    [InlineData("weapon_hegrenade", GrenadeType.He)]
    [InlineData("weapon_flashbang", GrenadeType.Flash)]
    [InlineData("weapon_smokegrenade", GrenadeType.Smoke)]
    [InlineData("weapon_incgrenade", GrenadeType.Molotov)]
    [InlineData("weapon_molotov", GrenadeType.Molotov)]
    [InlineData("weapon_decoy", GrenadeType.Decoy)]
    public void GrenadeOf_KnownGrenades(string weapon, GrenadeType expected) => Assert.Equal(expected, StatsWeapons.GrenadeOf(weapon));

    [Fact]
    public void GrenadeOf_Gun_IsNull() => Assert.Null(StatsWeapons.GrenadeOf("weapon_ak47"));

    [Theory]
    [InlineData("weapon_ak47", false, 215)]
    [InlineData("m4a1_silencer", false, 225)]
    [InlineData("awp", false, 200)]
    [InlineData("awp", true, 100)]
    [InlineData("aug", true, 150)]
    [InlineData("ssg08", true, 230)]
    [InlineData("negev", false, 150)]
    [InlineData("deagle", false, 230)]
    [InlineData("glock", false, 240)]
    [InlineData("unknown_gun", false, 250)]
    [InlineData("m4a1_silencer_off", false, 225)]
    [InlineData("weapon_usp_silencer_off", false, 240)]
    [InlineData("_off", false, 250)]
    public void MaxSpeed_ComesFromTheWeaponTable(string weapon, bool scoped, int expected) =>
        Assert.Equal(expected, StatsWeapons.MaxSpeed(weapon, scoped));

    [Theory]
    [InlineData("ak47", 73f, false, false, true)]
    [InlineData("ak47", 74f, false, false, false)]
    [InlineData("awp", 33f, true, false, true)]
    [InlineData("awp", 35f, true, false, false)]
    [InlineData("awp", 67f, false, false, true)]
    [InlineData("unknown_gun", 84f, false, false, true)]
    [InlineData("ak47", 0f, false, true, false)]
    public void IsStill_UpTo34PercentOfMaxSpeed_OnTheGround(string weapon, float speed, bool scoped, bool airborne, bool expected) =>
        Assert.Equal(expected, StatsWeapons.IsStill(weapon, new ShotMotion(speed, false, scoped, airborne, false)));

    [Fact]
    public void Horizontal_IgnoresTheVerticalSpeed() => Assert.Equal(5f, ShotMotion.Horizontal(3f, 4f));

    [Fact]
    public void EveryTableWeapon_IsABulletWeapon() =>
        Assert.All(StatsWeapons.SpeedTableWeapons, weapon => Assert.True(StatsWeapons.IsBullet(weapon)));
}
