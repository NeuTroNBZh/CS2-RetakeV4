using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

public enum GrenadeTeam
{
    Any,
    T,
    CT,
}

public sealed record GrenadeKitConfig
{
    public GrenadeTeam Team { get; init; } = GrenadeTeam.Any;

    public IReadOnlyList<string> Grenades { get; init; } = Array.Empty<string>();

    public GrenadeKit ToDomain() => new(
        Team switch { GrenadeTeam.T => TeamSide.T, GrenadeTeam.CT => TeamSide.CT, _ => null },
        Grenades);
}

public sealed record GrenadesConfig : ModuleConfig
{
    private const string Smoke = "weapon_smokegrenade";
    private const string Flash = "weapon_flashbang";
    private const string He = "weapon_hegrenade";
    private const string Molotov = "weapon_molotov";
    private const string Incendiary = "weapon_incgrenade";

    public GrenadesConfig() => Version = 1;

    public IReadOnlyDictionary<string, IReadOnlyList<GrenadeKitConfig>> Pools { get; init; } =
        new Dictionary<string, IReadOnlyList<GrenadeKitConfig>> { ["Default"] = V3Kits() };

    public IReadOnlyList<GrenadeKit> KitsFor(string pool) =>
        Pools.TryGetValue(pool, out var kits) ? kits.Select(k => k.ToDomain()).ToList() : Array.Empty<GrenadeKit>();

    private static IReadOnlyList<GrenadeKitConfig> V3Kits() => new[]
    {
        Kit(GrenadeTeam.Any), Kit(GrenadeTeam.Any, Smoke), Kit(GrenadeTeam.Any, Flash), Kit(GrenadeTeam.Any, He),
        Kit(GrenadeTeam.Any, Smoke, Flash), Kit(GrenadeTeam.Any, He, Flash),
        Kit(GrenadeTeam.CT, Flash, Flash), Kit(GrenadeTeam.CT, Smoke, He), Kit(GrenadeTeam.CT, Incendiary), Kit(GrenadeTeam.CT, He),
        Kit(GrenadeTeam.CT, Flash), Kit(GrenadeTeam.CT, Incendiary, Flash), Kit(GrenadeTeam.CT, Smoke, Incendiary, Flash),
        Kit(GrenadeTeam.T, Molotov), Kit(GrenadeTeam.T, Molotov, Flash), Kit(GrenadeTeam.T, Smoke, Flash),
    };

    private static GrenadeKitConfig Kit(GrenadeTeam team, params string[] grenades) => new() { Team = team, Grenades = grenades };
}
