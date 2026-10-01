using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Tests.Loadouts;

public class KnivesTests
{
    [Theory]
    [InlineData("weapon_knife", true)]
    [InlineData("weapon_knife_t", true)]
    [InlineData("weapon_knife_karambit", true)]
    [InlineData("weapon_bayonet", true)]
    [InlineData("weapon_ak47", false)]
    [InlineData("weapon_taser", false)]
    public void IsKnife_RecognisesEveryKnifeModel(string designerName, bool expected)
    {
        Assert.Equal(expected, Knives.IsKnife(designerName));
    }

    [Fact]
    public void HasKnife_IsFalse_WhenOnlyGunsRemain()
    {
        Assert.False(Knives.HasKnife(new[] { "weapon_usp_silencer", "weapon_ak47" }));
        Assert.True(Knives.HasKnife(new[] { "weapon_ak47", "weapon_knife_butterfly" }));
        Assert.False(Knives.HasKnife(Array.Empty<string>()));
    }

    [Theory]
    [InlineData(TeamSide.T, "weapon_knife_t")]
    [InlineData(TeamSide.CT, "weapon_knife")]
    public void DefaultFor_GivesTheTeamKnife(TeamSide team, string expected)
    {
        Assert.Equal(expected, Knives.DefaultFor(team));
    }
}
