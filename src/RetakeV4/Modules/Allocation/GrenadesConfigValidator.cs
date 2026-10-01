using RetakeV4.Configuration;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

public sealed class GrenadesConfigValidator : IConfigValidator<GrenadesConfig>
{
    public ValidationResult<GrenadesConfig> Validate(GrenadesConfig config, GrenadesConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        if (config.Pools is null)
        {
            issues.Add(new ConfigIssue(file, nameof(GrenadesConfig.Pools), "missing; using defaults"));
            return new ValidationResult<GrenadesConfig>(config with { Pools = defaults.Pools }, issues);
        }
        var pools = config.Pools.ToDictionary(
            p => p.Key,
            p => CleanPool(p.Key, p.Value, file, issues));
        return new ValidationResult<GrenadesConfig>(config with { Pools = pools }, issues);
    }

    private static IReadOnlyList<GrenadeKitConfig> CleanPool(string name, IReadOnlyList<GrenadeKitConfig>? kits, string file, List<ConfigIssue> issues)
    {
        var kept = new List<GrenadeKitConfig>();
        foreach (var kit in kits ?? Array.Empty<GrenadeKitConfig>())
        {
            if (kit is null)
            {
                issues.Add(new ConfigIssue(file, $"Pools[{name}]", "null kit removed"));
                continue;
            }
            var grenades = (kit.Grenades ?? Array.Empty<string>()).Where(WeaponCatalog.IsGrenade).ToList();
            if (grenades.Count != (kit.Grenades?.Count ?? 0))
            {
                issues.Add(new ConfigIssue(file, $"Pools[{name}]", "unknown grenade(s) removed from a kit"));
            }
            kept.Add(kit with { Grenades = grenades });
        }
        return kept;
    }
}
