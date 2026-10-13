namespace RetakeV4.Domain.Stats;

public static class StatsWeapons
{
    public const int HeadHitgroup = 1;

    private static readonly HashSet<string> NotBullets = new(StringComparer.Ordinal)
    {
        "hegrenade", "flashbang", "smokegrenade", "molotov", "incgrenade", "decoy", "inferno", "taser", "c4", "world",
    };

    public static string Normalize(string? weapon)
    {
        var name = (weapon ?? string.Empty).Trim().ToLowerInvariant();
        return name.StartsWith("weapon_", StringComparison.Ordinal) ? name["weapon_".Length..] : name;
    }

    public static bool IsBullet(string weapon)
    {
        var name = Normalize(weapon);
        return name.Length > 0
            && !NotBullets.Contains(name)
            && !name.StartsWith("knife", StringComparison.Ordinal)
            && !name.Contains("bayonet", StringComparison.Ordinal);
    }

    public static bool IsUtility(string weapon) => Normalize(weapon) is "hegrenade" or "inferno" or "molotov" or "incgrenade";

    public static GrenadeType? GrenadeOf(string weapon) => Normalize(weapon) switch
    {
        "hegrenade" => GrenadeType.He,
        "flashbang" => GrenadeType.Flash,
        "smokegrenade" => GrenadeType.Smoke,
        "molotov" or "incgrenade" => GrenadeType.Molotov,
        "decoy" => GrenadeType.Decoy,
        _ => null,
    };
}
