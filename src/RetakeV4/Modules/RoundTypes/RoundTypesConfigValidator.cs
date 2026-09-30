using RetakeV4.Configuration;
using RetakeV4.Domain.RoundTypes;

namespace RetakeV4.Modules.RoundTypes;

public sealed class RoundTypesConfigValidator : IConfigValidator<RoundTypesConfig>
{
    public ValidationResult<RoundTypesConfig> Validate(RoundTypesConfig config, RoundTypesConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var roundTypes = CleanRoundTypes(UpgradeLegacyEntries(config, defaults, file, issues), defaults.RoundTypes, file, issues);
        var names = roundTypes.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        var sequence = CleanSequence(config.Sequence, names, file, issues);
        var specific = names.Contains(config.Specific) ? config.Specific : Fallback(roundTypes[0].Name, config.Specific, file, issues);
        return new ValidationResult<RoundTypesConfig>(
            config with { RoundTypes = roundTypes, Sequence = sequence, Specific = specific }, issues);
    }

    private const int FirstVersionWithWeaponPools = 2;

    // A pre-v2 file only has round type names: without this upgrade every round would be played with the
    // class defaults (no weapon pool, deagle only) instead of the built-in V3 loadouts.
    private static IReadOnlyList<RoundTypeDefinitionConfig>? UpgradeLegacyEntries(
        RoundTypesConfig config, RoundTypesConfig defaults, string file, List<ConfigIssue> issues)
    {
        if (config.Version >= FirstVersionWithWeaponPools || config.RoundTypes is null)
        {
            return config.RoundTypes;
        }
        return config.RoundTypes.Select(entry =>
        {
            var builtIn = defaults.RoundTypes.FirstOrDefault(d => d.Name == entry?.Name);
            if (entry is null || builtIn is null || !HasNoWeaponPools(entry))
            {
                return entry!;
            }
            issues.Add(new ConfigIssue(file, $"RoundTypes[{entry.Name}]", $"has no weapon pools (pre-v2 file); using the built-in {entry.Name} definition"));
            return builtIn;
        }).ToList();
    }

    private static bool HasNoWeaponPools(RoundTypeDefinitionConfig entry) =>
        PoolSize(entry.Primaries) + PoolSize(entry.Secondaries) == 0;

    private static int PoolSize(WeaponPoolConfig? pool) =>
        pool is null ? 0 : (pool.T?.Count ?? 0) + (pool.CT?.Count ?? 0) + (pool.Any?.Count ?? 0);

    private static IReadOnlyList<RoundTypeDefinitionConfig> CleanRoundTypes(
        IReadOnlyList<RoundTypeDefinitionConfig>? roundTypes, IReadOnlyList<RoundTypeDefinitionConfig> defaults, string file, List<ConfigIssue> issues)
    {
        var kept = new List<RoundTypeDefinitionConfig>();
        foreach (var roundType in roundTypes ?? Array.Empty<RoundTypeDefinitionConfig>())
        {
            if (roundType is null || string.IsNullOrWhiteSpace(roundType.Name) || kept.Any(k => k.Name == roundType.Name))
            {
                issues.Add(new ConfigIssue(file, nameof(RoundTypesConfig.RoundTypes), $"blank or duplicate round type '{roundType?.Name}' removed"));
                continue;
            }
            kept.Add(RoundTypeDefinitionValidation.Clean(roundType, file, issues));
        }
        if (kept.Count > 0)
        {
            return kept;
        }
        issues.Add(new ConfigIssue(file, nameof(RoundTypesConfig.RoundTypes), "no valid round type; using defaults"));
        return defaults;
    }

    private static IReadOnlyList<RoundTypeSequenceEntry> CleanSequence(
        IReadOnlyList<RoundTypeSequenceEntry>? sequence, IReadOnlySet<string> names, string file, List<ConfigIssue> issues)
    {
        var kept = new List<RoundTypeSequenceEntry>();
        foreach (var entry in sequence ?? Array.Empty<RoundTypeSequenceEntry>())
        {
            if (entry?.RoundType is null || !names.Contains(entry.RoundType) || entry.Count == 0 || entry.Count < -1)
            {
                issues.Add(new ConfigIssue(file, nameof(RoundTypesConfig.Sequence), $"entry '{entry?.RoundType}' x{entry?.Count} removed (unknown type or invalid count)"));
                continue;
            }
            kept.Add(entry);
        }
        return kept;
    }

    private static string Fallback(string replacement, string invalid, string file, List<ConfigIssue> issues)
    {
        issues.Add(new ConfigIssue(file, nameof(RoundTypesConfig.Specific), $"unknown round type '{invalid}'; using '{replacement}'"));
        return replacement;
    }
}
