using RetakeV4.Domain.Common;
using RetakeV4.Modules.Allocation;

namespace RetakeV4.Integration.Tests.Modules.Allocation;

public class GrenadesConfigTests
{
    private static readonly GrenadesConfig Defaults = new();
    private readonly GrenadesConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid_AndContainTheSixteenV3Kits()
    {
        var result = _validator.Validate(Defaults, Defaults, "grenades.json");
        Assert.Empty(result.Issues);
        var kits = result.Config.KitsFor("Default");
        Assert.Equal(16, kits.Count);
        Assert.Equal(6, kits.Count(k => k.Team is null));
        Assert.Equal(7, kits.Count(k => k.Team == TeamSide.CT));
        Assert.Equal(3, kits.Count(k => k.Team == TeamSide.T));
        Assert.Contains(kits, k => k.Grenades.Count == 0);
    }

    [Fact]
    public void UnknownPool_GivesNoKits() => Assert.Empty(Defaults.KitsFor("Nope"));

    [Fact]
    public void UnknownGrenades_AndNullKits_AreRemoved()
    {
        var pools = new Dictionary<string, IReadOnlyList<GrenadeKitConfig>>
        {
            ["Default"] = new GrenadeKitConfig?[]
            {
                null,
                new GrenadeKitConfig { Team = GrenadeTeam.T, Grenades = new[] { "weapon_molotov", "weapon_nuke" } },
            }!,
        };
        var result = _validator.Validate(Defaults with { Pools = pools }, Defaults, "grenades.json");
        var kit = Assert.Single(result.Config.KitsFor("Default"));
        Assert.Equal(new[] { "weapon_molotov" }, kit.Grenades);
        Assert.Equal(TeamSide.T, kit.Team);
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void MissingPools_FallBackToDefaults()
    {
        var result = _validator.Validate(Defaults with { Pools = null! }, Defaults, "grenades.json");
        Assert.Equal(16, result.Config.KitsFor("Default").Count);
        Assert.Single(result.Issues);
    }
}
