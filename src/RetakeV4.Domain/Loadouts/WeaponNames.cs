namespace RetakeV4.Domain.Loadouts;

public static class WeaponNames
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["weapon_ak47"] = "AK-47",
        ["weapon_m4a1"] = "M4A4",
        ["weapon_m4a1_silencer"] = "M4A1-S",
        ["weapon_famas"] = "FAMAS",
        ["weapon_galilar"] = "Galil AR",
        ["weapon_aug"] = "AUG",
        ["weapon_sg556"] = "SG 553",
        ["weapon_awp"] = "AWP",
        ["weapon_ssg08"] = "SSG 08",
        ["weapon_scar20"] = "SCAR-20",
        ["weapon_g3sg1"] = "G3SG1",
        ["weapon_mp9"] = "MP9",
        ["weapon_mac10"] = "MAC-10",
        ["weapon_mp7"] = "MP7",
        ["weapon_mp5sd"] = "MP5-SD",
        ["weapon_ump45"] = "UMP-45",
        ["weapon_p90"] = "P90",
        ["weapon_bizon"] = "PP-Bizon",
        ["weapon_nova"] = "Nova",
        ["weapon_xm1014"] = "XM1014",
        ["weapon_mag7"] = "MAG-7",
        ["weapon_sawedoff"] = "Sawed-Off",
        ["weapon_m249"] = "M249",
        ["weapon_negev"] = "Negev",
        ["weapon_glock"] = "Glock-18",
        ["weapon_hkp2000"] = "P2000",
        ["weapon_usp_silencer"] = "USP-S",
        ["weapon_p250"] = "P250",
        ["weapon_fiveseven"] = "Five-SeveN",
        ["weapon_tec9"] = "Tec-9",
        ["weapon_cz75a"] = "CZ75-Auto",
        ["weapon_deagle"] = "Desert Eagle",
        ["weapon_revolver"] = "R8 Revolver",
        ["weapon_elite"] = "Dual Berettas",
    };

    public static string Display(string? weaponId) => weaponId switch
    {
        null => "-",
        _ when Names.TryGetValue(weaponId, out var name) => name,
        _ => weaponId.Replace("weapon_", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
    };
}
