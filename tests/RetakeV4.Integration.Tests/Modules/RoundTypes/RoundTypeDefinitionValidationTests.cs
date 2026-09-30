using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Modules.RoundTypes;

namespace RetakeV4.Integration.Tests.Modules.RoundTypes;

public class RoundTypeDefinitionValidationTests
{
    private static RoundTypeDefinitionConfig Clean(RoundTypeDefinitionConfig definition, List<ConfigIssue> issues) =>
        RoundTypeDefinitionValidation.Clean(definition, "roundtypes.json", issues);

    [Theory]
    [MemberData(nameof(DefaultDefinitions))]
    public void V3Defaults_AreValid(RoundTypeDefinitionConfig definition)
    {
        var issues = new List<ConfigIssue>();
        Clean(definition, issues);
        Assert.Empty(issues);
    }

    public static IEnumerable<object[]> DefaultDefinitions() => new[]
    {
        new object[] { RoundTypeDefaults.Pistol() },
        new object[] { RoundTypeDefaults.Mid() },
        new object[] { RoundTypeDefaults.FullBuy() },
    };

    [Fact]
    public void V3Defaults_MatchV3Loadouts()
    {
        var fullBuy = RoundTypeDefaults.FullBuy().ToDomain();
        Assert.Equal(new TeamDefault("weapon_ak47", "weapon_deagle"), fullBuy.DefaultFor(TeamSide.T));
        Assert.Equal(new TeamDefault("weapon_m4a1", "weapon_deagle"), fullBuy.DefaultFor(TeamSide.CT));
        Assert.True(fullBuy.Awp.Enabled);
        var pistol = RoundTypeDefaults.Pistol().ToDomain();
        Assert.Equal(ArmorKind.Kevlar, pistol.Armor);
        Assert.Equal(new TeamDefault(null, "weapon_usp_silencer"), pistol.DefaultFor(TeamSide.CT));
        Assert.Equal(DefuseKitMode.Chance, pistol.DefuseKit.Mode);
        Assert.True(pistol.DefuseKit.GuaranteeMinimum);
        Assert.Equal("weapon_mac10", RoundTypeDefaults.Mid().ToDomain().DefaultFor(TeamSide.T).Primary);
    }

    [Fact]
    public void UnknownOrMisplacedWeapons_AreRemoved()
    {
        var definition = RoundTypeDefaults.FullBuy() with
        {
            Primaries = new WeaponPoolConfig { T = new[] { "weapon_ak47", "weapon_ak48", "weapon_glock" } },
        };
        var issues = new List<ConfigIssue>();
        var cleaned = Clean(definition, issues);
        Assert.Equal(new[] { "weapon_ak47" }, cleaned.Primaries.T);
        Assert.Equal(2, issues.Count(i => i.Key.Contains("Primaries")));
    }

    [Fact]
    public void DefaultOutsideOfPool_IsReplacedByFirstPoolWeapon()
    {
        var definition = RoundTypeDefaults.FullBuy() with
        {
            Defaults = new TeamDefaultsConfig
            {
                T = new DefaultWeaponsConfig { Primary = "weapon_m4a1", Secondary = "weapon_usp_silencer" },
                CT = RoundTypeDefaults.FullBuy().Defaults.CT,
            },
        };
        var issues = new List<ConfigIssue>();
        var cleaned = Clean(definition, issues);
        Assert.Equal("weapon_ak47", cleaned.Defaults.T.Primary);
        Assert.Equal("weapon_glock", cleaned.Defaults.T.Secondary);
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public void OutOfRangeNumbers_AreClamped()
    {
        var definition = RoundTypeDefaults.FullBuy() with
        {
            Awp = new AwpConfig { Enabled = true, MaxPerTeam = -1, MinActivePlayers = -3, Chance = 150 },
            DefuseKit = new DefuseKitConfig { Mode = DefuseKitMode.Quota, Quota = -2, Chance = -10 },
            Zeus = new ZeusConfig { Enabled = true, Chance = 101 },
        };
        var issues = new List<ConfigIssue>();
        var cleaned = Clean(definition, issues);
        Assert.Equal(0, cleaned.Awp.MaxPerTeam);
        Assert.Equal(0, cleaned.Awp.MinActivePlayers);
        Assert.Equal(100, cleaned.Awp.Chance);
        Assert.Equal(0, cleaned.DefuseKit.Quota);
        Assert.Equal(0, cleaned.DefuseKit.Chance);
        Assert.Equal(100, cleaned.Zeus.Chance);
        Assert.Equal(6, issues.Count);
    }

    [Fact]
    public void NullSections_AreReplacedByDefaults()
    {
        var definition = RoundTypeDefaults.Mid() with { Primaries = null!, Defaults = null!, Awp = null!, GrenadePool = " " };
        var issues = new List<ConfigIssue>();
        var cleaned = Clean(definition, issues);
        Assert.NotNull(cleaned.Primaries);
        Assert.NotNull(cleaned.Defaults.T);
        Assert.NotNull(cleaned.Awp);
        Assert.Equal("Default", cleaned.GrenadePool);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void RoundTypesConfig_ExposesDefinitionsByName()
    {
        var definitions = new RoundTypesConfig().ToDefinitions();
        Assert.Equal(new[] { "Pistol", "Mid", "FullBuy" }, definitions.Keys);
        Assert.Equal("FullBuy", definitions["FullBuy"].Name);
    }
}
