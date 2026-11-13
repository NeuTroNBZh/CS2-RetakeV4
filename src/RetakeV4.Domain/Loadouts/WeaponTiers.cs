namespace RetakeV4.Domain.Loadouts;

public enum WeaponTier
{
    Light = 1,
    Medium = 2,
    Strong = 3,
}

// Strength in a retake, not the buy price: the M249 is expensive but weak, the auto-snipers decide a fight on their own.
public static class WeaponTiers
{
    private static readonly Dictionary<string, WeaponTier> Tiers = new(StringComparer.Ordinal)
    {
        ["weapon_ak47"] = WeaponTier.Strong,
        ["weapon_m4a1"] = WeaponTier.Strong,
        ["weapon_m4a1_silencer"] = WeaponTier.Strong,
        ["weapon_aug"] = WeaponTier.Strong,
        ["weapon_sg556"] = WeaponTier.Strong,
        ["weapon_awp"] = WeaponTier.Strong,
        ["weapon_scar20"] = WeaponTier.Strong,
        ["weapon_g3sg1"] = WeaponTier.Strong,
        ["weapon_famas"] = WeaponTier.Medium,
        ["weapon_galilar"] = WeaponTier.Medium,
        ["weapon_ssg08"] = WeaponTier.Medium,
        ["weapon_mp9"] = WeaponTier.Medium,
        ["weapon_mp7"] = WeaponTier.Medium,
        ["weapon_mp5sd"] = WeaponTier.Medium,
        ["weapon_p90"] = WeaponTier.Medium,
        ["weapon_xm1014"] = WeaponTier.Medium,
        ["weapon_mag7"] = WeaponTier.Medium,
        ["weapon_mac10"] = WeaponTier.Light,
        ["weapon_ump45"] = WeaponTier.Light,
        ["weapon_bizon"] = WeaponTier.Light,
        ["weapon_nova"] = WeaponTier.Light,
        ["weapon_sawedoff"] = WeaponTier.Light,
        ["weapon_m249"] = WeaponTier.Light,
        ["weapon_negev"] = WeaponTier.Light,
        ["weapon_deagle"] = WeaponTier.Strong,
        ["weapon_revolver"] = WeaponTier.Strong,
        ["weapon_p250"] = WeaponTier.Medium,
        ["weapon_fiveseven"] = WeaponTier.Medium,
        ["weapon_tec9"] = WeaponTier.Medium,
        ["weapon_cz75a"] = WeaponTier.Medium,
        ["weapon_glock"] = WeaponTier.Light,
        ["weapon_hkp2000"] = WeaponTier.Light,
        ["weapon_usp_silencer"] = WeaponTier.Light,
        ["weapon_elite"] = WeaponTier.Light,
    };

    public static WeaponTier Of(string weapon) => Tiers.TryGetValue(weapon, out var tier) ? tier : WeaponTier.Medium;

    public static bool IsKnown(string weapon) => Tiers.ContainsKey(weapon);
}
