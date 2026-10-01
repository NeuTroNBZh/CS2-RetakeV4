namespace RetakeV4.Domain.Loadouts;

public static class WeaponCatalog
{
    public const string Awp = "weapon_awp";

    private static readonly HashSet<string> Primaries = new(StringComparer.Ordinal)
    {
        "weapon_ak47", "weapon_m4a1", "weapon_m4a1_silencer", "weapon_famas", "weapon_galilar", "weapon_aug", "weapon_sg556",
        "weapon_awp", "weapon_ssg08", "weapon_scar20", "weapon_g3sg1",
        "weapon_mp9", "weapon_mac10", "weapon_mp7", "weapon_mp5sd", "weapon_ump45", "weapon_p90", "weapon_bizon",
        "weapon_nova", "weapon_xm1014", "weapon_mag7", "weapon_sawedoff", "weapon_m249", "weapon_negev",
    };

    private static readonly HashSet<string> Secondaries = new(StringComparer.Ordinal)
    {
        "weapon_glock", "weapon_hkp2000", "weapon_usp_silencer", "weapon_p250", "weapon_fiveseven",
        "weapon_tec9", "weapon_cz75a", "weapon_deagle", "weapon_revolver", "weapon_elite",
    };

    private static readonly HashSet<string> Grenades = new(StringComparer.Ordinal)
    {
        "weapon_hegrenade", "weapon_flashbang", "weapon_smokegrenade", "weapon_molotov", "weapon_incgrenade", "weapon_decoy",
    };

    public static IReadOnlyCollection<string> Guns { get; } = Primaries.Concat(Secondaries).ToList();

    public static bool IsPrimary(string? id) => id is not null && Primaries.Contains(id);

    public static bool IsSecondary(string? id) => id is not null && Secondaries.Contains(id);

    public static bool IsGrenade(string? id) => id is not null && Grenades.Contains(id);
}
