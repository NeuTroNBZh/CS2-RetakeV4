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
}
