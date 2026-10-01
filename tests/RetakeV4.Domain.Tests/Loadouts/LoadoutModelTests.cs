using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Tests.Loadouts;

public class LoadoutModelTests
{
    [Fact]
    public void TeamWeapons_For_CombinesSideAndAny_WithoutDuplicates()
    {
        var pool = new TeamWeapons(new[] { "weapon_ak47" }, new[] { "weapon_m4a1" }, new[] { "weapon_awp", "weapon_ak47" });
        Assert.Equal(new[] { "weapon_ak47", "weapon_awp" }, pool.For(TeamSide.T));
        Assert.Equal(new[] { "weapon_m4a1", "weapon_awp", "weapon_ak47" }, pool.For(TeamSide.CT));
    }

    [Fact]
    public void TeamWeapons_Empty_HasNothing() => Assert.Empty(TeamWeapons.Empty.For(TeamSide.T));

    [Fact]
    public void Definition_DefaultFor_PicksTheSide()
    {
        var t = new TeamDefault("weapon_ak47", "weapon_glock");
        var ct = new TeamDefault("weapon_m4a1", "weapon_usp_silencer");
        var definition = new RoundTypeDefinition("FullBuy", ArmorKind.KevlarHelmet, TeamWeapons.Empty, TeamWeapons.Empty, t, ct,
            new AwpSettings(false, 1, 5, 30), new DefuseKitSettings(DefuseKitMode.All, 1, 100, false), new ZeusSettings(false, 20), "Default");
        Assert.Equal(t, definition.DefaultFor(TeamSide.T));
        Assert.Equal(ct, definition.DefaultFor(TeamSide.CT));
    }
}
