namespace RetakeV4.Domain.Stats;

public static class StatsWeapons
{
    public const int HeadHitgroup = 1;

    // A shot is "still" up to 34 % of the weapon's max speed (CS2's accurate-movement threshold).
    public const double StillSpeedRatio = 0.34;

    // Knife speed: used for weapons missing from the table.
    public const int DefaultMaxSpeed = 250;

    private const string UnsilencedSuffix = "_off";

    private static readonly HashSet<string> NotBullets = new(StringComparer.Ordinal)
    {
        "hegrenade", "flashbang", "smokegrenade", "molotov", "incgrenade", "decoy", "inferno", "taser", "c4", "world",
    };

    // Max player speed in u/s, unscoped and scoped, from CS2's weapon data (items_game / weapon vdata
    // "max_player_speed" and "max_player_speed_alt"). Valve retunes these now and then: adjust after a game update.
    private static readonly Dictionary<string, (int Speed, int Scoped)> MaxSpeeds = new(StringComparer.Ordinal)
    {
        ["glock"] = (240, 240),
        ["hkp2000"] = (240, 240),
        ["usp_silencer"] = (240, 240),
        ["p250"] = (240, 240),
        ["elite"] = (240, 240),
        ["fiveseven"] = (240, 240),
        ["tec9"] = (240, 240),
        ["cz75a"] = (240, 240),
        ["deagle"] = (230, 230),
        ["revolver"] = (220, 220),
        ["mac10"] = (240, 240),
        ["mp9"] = (240, 240),
        ["mp7"] = (220, 220),
        ["mp5sd"] = (235, 235),
        ["ump45"] = (230, 230),
        ["p90"] = (230, 230),
        ["bizon"] = (240, 240),
        ["nova"] = (220, 220),
        ["xm1014"] = (215, 215),
        ["sawedoff"] = (210, 210),
        ["mag7"] = (225, 225),
        ["m249"] = (195, 195),
        ["negev"] = (150, 150),
        ["galilar"] = (215, 215),
        ["famas"] = (220, 220),
        ["ak47"] = (215, 215),
        ["m4a1"] = (225, 225),
        ["m4a1_silencer"] = (225, 225),
        ["sg556"] = (210, 150),
        ["aug"] = (220, 150),
        ["ssg08"] = (230, 230),
        ["awp"] = (200, 100),
        ["g3sg1"] = (215, 120),
        ["scar20"] = (215, 120),
    };

    public static IReadOnlyCollection<string> SpeedTableWeapons => MaxSpeeds.Keys;

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

    // The unsilenced variants (m4a1_silencer_off, usp_silencer_off) share the speed of their weapon.
    public static int MaxSpeed(string weapon, bool scoped)
    {
        var name = Normalize(weapon);
        if (name.EndsWith(UnsilencedSuffix, StringComparison.Ordinal))
        {
            name = name[..^UnsilencedSuffix.Length];
        }
        return MaxSpeeds.TryGetValue(name, out var speeds) ? (scoped ? speeds.Scoped : speeds.Speed) : DefaultMaxSpeed;
    }

    // On the ground and at no more than 34 % of the weapon's max speed; a shot in the air is a moving shot.
    public static bool IsStill(string weapon, ShotMotion motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        return !motion.Airborne && motion.Speed <= MaxSpeed(weapon, motion.Scoped) * StillSpeedRatio;
    }
}
