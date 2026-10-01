using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.RoundTypes;

public static class RoundTypeDefaults
{
    private static readonly WeaponPoolConfig V3Secondaries = new()
    {
        T = new[] { "weapon_glock", "weapon_tec9" },
        CT = new[] { "weapon_usp_silencer", "weapon_hkp2000", "weapon_fiveseven" },
        Any = new[] { "weapon_deagle", "weapon_p250", "weapon_cz75a", "weapon_elite", "weapon_revolver" },
    };

    public static RoundTypeDefinitionConfig Pistol() => new()
    {
        Name = "Pistol",
        Armor = ArmorKind.Kevlar,
        Secondaries = V3Secondaries,
        Defaults = new TeamDefaultsConfig
        {
            T = new DefaultWeaponsConfig { Secondary = "weapon_glock" },
            CT = new DefaultWeaponsConfig { Secondary = "weapon_usp_silencer" },
        },
        DefuseKit = new DefuseKitConfig { Mode = DefuseKitMode.Chance, Chance = 34.44444, GuaranteeMinimum = true },
    };

    public static RoundTypeDefinitionConfig Mid() => new()
    {
        Name = "Mid",
        Primaries = new WeaponPoolConfig
        {
            T = new[] { "weapon_mac10", "weapon_galilar", "weapon_sg556" },
            CT = new[] { "weapon_mp9", "weapon_famas", "weapon_aug" },
            Any = new[] { "weapon_p90", "weapon_mp5sd", "weapon_ump45", "weapon_bizon", "weapon_mp7" },
        },
        Secondaries = V3Secondaries,
        Defaults = new TeamDefaultsConfig
        {
            T = new DefaultWeaponsConfig { Primary = "weapon_mac10", Secondary = "weapon_deagle" },
            CT = new DefaultWeaponsConfig { Primary = "weapon_mp9", Secondary = "weapon_deagle" },
        },
    };

    public static RoundTypeDefinitionConfig FullBuy() => new()
    {
        Name = "FullBuy",
        Primaries = new WeaponPoolConfig
        {
            T = new[] { "weapon_ak47", "weapon_galilar", "weapon_sg556", "weapon_mac10", "weapon_g3sg1", "weapon_sawedoff" },
            CT = new[] { "weapon_m4a1", "weapon_m4a1_silencer", "weapon_famas", "weapon_aug", "weapon_mp9", "weapon_scar20", "weapon_mag7" },
            Any = new[] { "weapon_mp7", "weapon_mp5sd", "weapon_ump45", "weapon_p90", "weapon_bizon", "weapon_ssg08", "weapon_nova", "weapon_xm1014", "weapon_m249", "weapon_negev" },
        },
        Secondaries = V3Secondaries,
        Defaults = new TeamDefaultsConfig
        {
            T = new DefaultWeaponsConfig { Primary = "weapon_ak47", Secondary = "weapon_deagle" },
            CT = new DefaultWeaponsConfig { Primary = "weapon_m4a1", Secondary = "weapon_deagle" },
        },
        Awp = new AwpConfig { Enabled = true },
    };
}
