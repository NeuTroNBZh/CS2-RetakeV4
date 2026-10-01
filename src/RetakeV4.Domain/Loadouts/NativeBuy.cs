using RetakeV4.Domain.Common;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Loadouts;

public enum BuyRequestKind
{
    Weapon,
    AutoManaged,
    Capture,
}

public sealed record BuyRequest(BuyRequestKind Kind, string? Weapon = null);

// Turns the CS2 "buy" command and "item_pickup" event into a weapon id (method and tables from agora's CommandAllocator).
public static class NativeBuyResolver
{
    private static readonly HashSet<string> AutoManaged = new(StringComparer.Ordinal)
    {
        "vest", "vesthelm", "assaultsuit", "kevlar", "itemkevlar", "itemassaultsuit", "helmet", "defuser", "itemdefuser",
        "hegrenade", "incgrenade", "molotov", "flashbang", "smokegrenade", "decoy", "taser",
    };

    private static readonly IReadOnlyDictionary<string, string> ExtraAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["m4a1s"] = "weapon_m4a1_silencer",
        ["m4a4"] = "weapon_m4a1",
        ["usp"] = "weapon_usp_silencer",
        ["usps"] = "weapon_usp_silencer",
        ["p2000"] = "weapon_hkp2000",
        ["galil"] = "weapon_galilar",
        ["sg553"] = "weapon_sg556",
        ["cz75"] = "weapon_cz75a",
        ["r8"] = "weapon_revolver",
        ["dualberettas"] = "weapon_elite",
        ["mac"] = "weapon_mac10",
        ["ump"] = "weapon_ump45",
        ["mp5"] = "weapon_mp5sd",
        ["scout"] = "weapon_ssg08",
    };

    private static readonly IReadOnlyDictionary<string, string> Aliases = WeaponCatalog.Guns
        .ToDictionary(Normalize, id => id, StringComparer.Ordinal)
        .Concat(ExtraAliases)
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<long, string> ByDefindex = new Dictionary<long, string>
    {
        [1] = "weapon_deagle", [2] = "weapon_elite", [3] = "weapon_fiveseven", [4] = "weapon_glock", [7] = "weapon_ak47",
        [8] = "weapon_aug", [9] = "weapon_awp", [10] = "weapon_famas", [11] = "weapon_g3sg1", [13] = "weapon_galilar",
        [14] = "weapon_m249", [16] = "weapon_m4a1", [17] = "weapon_mac10", [19] = "weapon_p90", [23] = "weapon_mp5sd",
        [24] = "weapon_ump45", [25] = "weapon_xm1014", [26] = "weapon_bizon", [27] = "weapon_mag7", [28] = "weapon_negev",
        [29] = "weapon_sawedoff", [30] = "weapon_tec9", [32] = "weapon_hkp2000", [33] = "weapon_mp7", [34] = "weapon_mp9",
        [35] = "weapon_nova", [36] = "weapon_p250", [38] = "weapon_scar20", [39] = "weapon_sg556", [40] = "weapon_ssg08",
        [60] = "weapon_m4a1_silencer", [61] = "weapon_usp_silencer", [63] = "weapon_cz75a", [64] = "weapon_revolver",
    };

    public static string Normalize(string raw)
    {
        var token = raw.Trim().ToLowerInvariant();
        if (token.StartsWith("weapon_", StringComparison.Ordinal))
        {
            token = token["weapon_".Length..];
        }
        return new string(token.Where(char.IsLetterOrDigit).ToArray());
    }

    // Order matters: anything auto-managed is refused, a numeric (buy-menu) or unknown payload is captured at pickup.
    public static BuyRequest Resolve(IEnumerable<string> arguments)
    {
        var tokens = arguments
            .SelectMany(a => a.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(Normalize)
            .Where(t => t.Length > 0 && t != "buy")
            .ToList();
        if (tokens.Any(AutoManaged.Contains))
        {
            return new BuyRequest(BuyRequestKind.AutoManaged);
        }
        if (tokens.Count == 0 || tokens.Any(t => t.All(char.IsDigit)))
        {
            return new BuyRequest(BuyRequestKind.Capture);
        }
        var weapon = tokens.Select(t => Aliases.GetValueOrDefault(t)).FirstOrDefault(w => w is not null);
        return weapon is null ? new BuyRequest(BuyRequestKind.Capture) : new BuyRequest(BuyRequestKind.Weapon, weapon);
    }

    public static string? FromPickup(long defindex, string? item)
    {
        if (ByDefindex.TryGetValue(defindex, out var weapon))
        {
            return weapon;
        }
        return item is null ? null : Aliases.GetValueOrDefault(Normalize(item));
    }

    public static bool IsAutoManaged(string? item) => item is not null && AutoManaged.Contains(Normalize(item));
}

// Weapon entities (index -> designer name) before and after a captured buy: what the purchase added, and what it made the
// player drop (CS2 drops the gun held in the same slot).
public sealed record InventoryDiff(IReadOnlyList<uint> Added, IReadOnlyList<uint> Dropped);

public static class InventorySnapshot
{
    public static InventoryDiff Diff(IReadOnlyDictionary<uint, string> before, IReadOnlyDictionary<uint, string> after) => new(
        after.Keys.Where(index => !before.ContainsKey(index)).Order().ToList(),
        before.Keys.Where(index => !after.ContainsKey(index)).Order().ToList());
}

public enum BuyOutcome
{
    SetWeapon,
    AwpVolunteer,
    NotAvailable,
}

public sealed record BuyDecision(BuyOutcome Outcome, WeaponMenuSelection? Selection = null);

public static class NativeBuy
{
    public const int Cash = 16000;

    private static readonly BuyDecision NotAvailable = new(BuyOutcome.NotAvailable);

    // A captured buy really hands an item over and makes the player drop his gun: only acceptable before the round starts,
    // where giving the round's guns back cannot be used to refill ammo.
    public static bool CanCapture(RoundPhase phase, bool alive) => phase == RoundPhase.FreezeTime && alive;

    public static BuyDecision Decide(string weapon, TeamSide team, RoundTypeDefinition? current)
    {
        if (weapon == WeaponCatalog.Awp)
        {
            return new BuyDecision(BuyOutcome.AwpVolunteer);
        }
        WeaponSlot? slot = WeaponCatalog.IsPrimary(weapon) ? WeaponSlot.Primary : WeaponCatalog.IsSecondary(weapon) ? WeaponSlot.Secondary : null;
        if (current is null || slot is null)
        {
            return NotAvailable;
        }
        var selection = new WeaponMenuSelection(team, current.Name, slot.Value, weapon);
        return WeaponMenu.IsAllowed(selection, new[] { current }) ? new BuyDecision(BuyOutcome.SetWeapon, selection) : NotAvailable;
    }
}

public enum AllocationMode
{
    Menu,
    NativeBuy,
    Both,
}

public static class AllocationModes
{
    public static bool UsesMenu(AllocationMode mode) => mode != AllocationMode.NativeBuy;

    public static bool UsesNativeBuy(AllocationMode mode) => mode != AllocationMode.Menu;

    // The buy menu is only open when it is used to choose weapons; money is fixed because nothing is really bought.
    public static IReadOnlyList<(string Name, string Value)> Cvars(AllocationMode mode) => UsesNativeBuy(mode)
        ? new[] { ("mp_buy_anywhere", "1"), ("mp_buytime", "9999"), ("mp_maxmoney", NativeBuy.Cash.ToString(System.Globalization.CultureInfo.InvariantCulture)) }
        : new[] { ("mp_buy_anywhere", "0"), ("mp_buytime", "0"), ("mp_maxmoney", "0") };

    public static string HowToKey(AllocationMode mode) => mode switch
    {
        AllocationMode.Menu => "allocation.howto.menu",
        AllocationMode.NativeBuy => "allocation.howto.native",
        _ => "allocation.howto.both",
    };
}
