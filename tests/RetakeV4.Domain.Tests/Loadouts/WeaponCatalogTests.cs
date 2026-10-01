using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Tests.Loadouts;

public class WeaponCatalogTests
{
    [Theory]
    [InlineData("weapon_ak47")]
    [InlineData("weapon_m4a1")]
    [InlineData("weapon_m4a1_silencer")]
    [InlineData("weapon_awp")]
    [InlineData("weapon_mp9")]
    [InlineData("weapon_negev")]
    public void Primaries_AreKnown(string id)
    {
        Assert.True(WeaponCatalog.IsPrimary(id));
        Assert.False(WeaponCatalog.IsSecondary(id));
    }

    [Theory]
    [InlineData("weapon_glock")]
    [InlineData("weapon_usp_silencer")]
    [InlineData("weapon_deagle")]
    [InlineData("weapon_revolver")]
    [InlineData("weapon_elite")]
    public void Secondaries_AreKnown(string id)
    {
        Assert.True(WeaponCatalog.IsSecondary(id));
        Assert.False(WeaponCatalog.IsPrimary(id));
    }

    [Theory]
    [InlineData("weapon_hegrenade")]
    [InlineData("weapon_flashbang")]
    [InlineData("weapon_smokegrenade")]
    [InlineData("weapon_molotov")]
    [InlineData("weapon_incgrenade")]
    [InlineData("weapon_decoy")]
    public void Grenades_AreKnown(string id) => Assert.True(WeaponCatalog.IsGrenade(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("weapon_ak48")]
    [InlineData("WEAPON_AK47")]
    [InlineData("weapon_knife")]
    [InlineData("weapon_c4")]
    public void UnknownIds_AreRejected(string? id)
    {
        Assert.False(WeaponCatalog.IsPrimary(id));
        Assert.False(WeaponCatalog.IsSecondary(id));
        Assert.False(WeaponCatalog.IsGrenade(id));
    }
}
