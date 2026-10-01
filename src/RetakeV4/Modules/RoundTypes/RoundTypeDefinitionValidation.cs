using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.RoundTypes;

public static class RoundTypeDefinitionValidation
{
    private const string FallbackSecondary = "weapon_deagle";

    public static RoundTypeDefinitionConfig Clean(RoundTypeDefinitionConfig definition, string file, List<ConfigIssue> issues)
    {
        var key = $"RoundTypes[{definition.Name}]";
        var primaries = CleanPool(definition.Primaries, WeaponCatalog.IsPrimary, $"{key}.Primaries", file, issues);
        var secondaries = CleanPool(definition.Secondaries, WeaponCatalog.IsSecondary, $"{key}.Secondaries", file, issues);
        var defaults = definition.Defaults ?? Missing(new TeamDefaultsConfig(), $"{key}.Defaults", file, issues);
        var context = new PoolContext(primaries.ToDomain(), secondaries.ToDomain(), key, file, issues);
        return definition with
        {
            Primaries = primaries,
            Secondaries = secondaries,
            Defaults = new TeamDefaultsConfig
            {
                T = CleanDefault(defaults.T, TeamSide.T, context),
                CT = CleanDefault(defaults.CT, TeamSide.CT, context),
            },
            Awp = CleanAwp(definition.Awp ?? Missing(new AwpConfig(), $"{key}.Awp", file, issues), key, file, issues),
            DefuseKit = CleanKit(definition.DefuseKit ?? Missing(new DefuseKitConfig(), $"{key}.DefuseKit", file, issues), key, file, issues),
            Zeus = CleanZeus(definition.Zeus ?? Missing(new ZeusConfig(), $"{key}.Zeus", file, issues), key, file, issues),
            GrenadePool = string.IsNullOrWhiteSpace(definition.GrenadePool)
                ? Missing("Default", $"{key}.GrenadePool", file, issues)
                : definition.GrenadePool,
        };
    }

    private sealed record PoolContext(TeamWeapons Primaries, TeamWeapons Secondaries, string Key, string File, List<ConfigIssue> Issues);

    private static WeaponPoolConfig CleanPool(WeaponPoolConfig? pool, Func<string?, bool> isValid, string key, string file, List<ConfigIssue> issues)
    {
        var source = pool ?? Missing(new WeaponPoolConfig(), key, file, issues);
        IReadOnlyList<string> Keep(IReadOnlyList<string>? weapons, string side) =>
            (weapons ?? Array.Empty<string>()).Where(w =>
            {
                if (isValid(w))
                {
                    return true;
                }
                issues.Add(new ConfigIssue(file, $"{key}.{side}", $"'{w}' is not a valid weapon for this slot; removed"));
                return false;
            }).ToList();
        return new WeaponPoolConfig { T = Keep(source.T, "T"), CT = Keep(source.CT, "CT"), Any = Keep(source.Any, "Any") };
    }

    private static DefaultWeaponsConfig CleanDefault(DefaultWeaponsConfig? weapons, TeamSide side, PoolContext context)
    {
        var key = $"{context.Key}.Defaults.{side}";
        var source = weapons ?? Missing(new DefaultWeaponsConfig(), key, context.File, context.Issues);
        var primaryPool = context.Primaries.For(side);
        var secondaryPool = context.Secondaries.For(side);
        var primary = source.Primary is null || primaryPool.Contains(source.Primary)
            ? source.Primary
            : Replace(source.Primary, primaryPool.FirstOrDefault(), $"{key}.Primary", context);
        var secondary = secondaryPool.Contains(source.Secondary) || (secondaryPool.Count == 0 && WeaponCatalog.IsSecondary(source.Secondary))
            ? source.Secondary
            : Replace(source.Secondary, secondaryPool.Count > 0 ? secondaryPool[0] : FallbackSecondary, $"{key}.Secondary", context)!;
        return new DefaultWeaponsConfig { Primary = primary, Secondary = secondary };
    }

    private static string? Replace(string? invalid, string? replacement, string key, PoolContext context)
    {
        context.Issues.Add(new ConfigIssue(context.File, key, $"'{invalid}' is not in the pool; using '{replacement ?? "none"}'"));
        return replacement;
    }

    private static AwpConfig CleanAwp(AwpConfig awp, string key, string file, List<ConfigIssue> issues) => awp with
    {
        MaxPerTeam = AtLeastZero(awp.MaxPerTeam, $"{key}.Awp.MaxPerTeam", file, issues),
        MinActivePlayers = AtLeastZero(awp.MinActivePlayers, $"{key}.Awp.MinActivePlayers", file, issues),
        Chance = Percent(awp.Chance, $"{key}.Awp.Chance", file, issues),
    };

    private static DefuseKitConfig CleanKit(DefuseKitConfig kit, string key, string file, List<ConfigIssue> issues) => kit with
    {
        Quota = AtLeastZero(kit.Quota, $"{key}.DefuseKit.Quota", file, issues),
        Chance = Percent(kit.Chance, $"{key}.DefuseKit.Chance", file, issues),
    };

    private static ZeusConfig CleanZeus(ZeusConfig zeus, string key, string file, List<ConfigIssue> issues) =>
        zeus with { Chance = Percent(zeus.Chance, $"{key}.Zeus.Chance", file, issues) };

    private static int AtLeastZero(int value, string key, string file, List<ConfigIssue> issues)
    {
        if (value >= 0)
        {
            return value;
        }
        issues.Add(new ConfigIssue(file, key, $"{value} is negative; using 0"));
        return 0;
    }

    private static double Percent(double value, string key, string file, List<ConfigIssue> issues)
    {
        var clamped = Math.Clamp(value, 0d, 100d);
        if (!clamped.Equals(value))
        {
            issues.Add(new ConfigIssue(file, key, $"{value} is outside 0-100; using {clamped}"));
        }
        return clamped;
    }

    private static T Missing<T>(T replacement, string key, string file, List<ConfigIssue> issues)
    {
        issues.Add(new ConfigIssue(file, key, "missing; using defaults"));
        return replacement;
    }
}
