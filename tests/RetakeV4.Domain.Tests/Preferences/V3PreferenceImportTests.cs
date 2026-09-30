using RetakeV4.Domain.Common;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Domain.Tests.Preferences;

public class V3PreferenceImportTests
{
    private const ulong Alice = 76561198000000001UL;

    [Fact]
    public void WeaponTables_AreMergedPerPlayerTeamAndRoundType()
    {
        var rows = new[]
        {
            new V3PreferenceRow("FullBuyPrimary", Alice, 3, "weapon_m4a1_silencer", null),
            new V3PreferenceRow("FullBuySecondary", Alice, 3, "weapon_usp_silencer", null),
            new V3PreferenceRow("MidPrimary", Alice, 2, "weapon_galilar", null),
            new V3PreferenceRow("Pistol", Alice, 2, "weapon_tec9", null),
        };
        var imported = V3PreferenceImport.Convert(rows);
        var fullBuy = Assert.Single(imported, p => p.Key == new PreferenceKey(Alice, TeamSide.CT, "FullBuy"));
        Assert.Equal("weapon_m4a1_silencer", fullBuy.Preference.Primary);
        Assert.Equal("weapon_usp_silencer", fullBuy.Preference.Secondary);
        Assert.Equal("weapon_galilar", imported.Single(p => p.Key.RoundType == "Mid").Preference.Primary);
        var pistol = imported.Single(p => p.Key.RoundType == "Pistol");
        Assert.Null(pistol.Preference.Primary);
        Assert.Equal("weapon_tec9", pistol.Preference.Secondary);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(0, false)]
    public void AwpChance_BecomesAwpVolunteering(int chance, bool expected)
    {
        var imported = V3PreferenceImport.Convert(new[] { new V3PreferenceRow("FullBuyAWPChance", Alice, 2, null, chance) });
        var awp = Assert.Single(imported);
        Assert.Equal(new PreferenceKey(Alice, TeamSide.T, PreferenceKey.AnyRoundType), awp.Key);
        Assert.Equal(expected, awp.Preference.AwpOptIn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void RowsWithoutAPlayableTeam_AreIgnored(int team) =>
        Assert.Empty(V3PreferenceImport.Convert(new[] { new V3PreferenceRow("MidPrimary", Alice, team, "weapon_mp9", null) }));

    [Fact]
    public void UnknownTables_AreIgnored() =>
        Assert.Empty(V3PreferenceImport.Convert(new[] { new V3PreferenceRow("Something", Alice, 2, "weapon_mp9", null) }));

    [Fact]
    public void BlankWeapons_BecomeNull()
    {
        var imported = V3PreferenceImport.Convert(new[] { new V3PreferenceRow("MidPrimary", Alice, 2, "  ", null) });
        Assert.Null(Assert.Single(imported).Preference.Primary);
    }
}
