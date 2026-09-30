using RetakeV4.Configuration;
using RetakeV4.Domain.RoundTypes;

namespace RetakeV4.Modules.RoundTypes;

public sealed class RoundTypesConfigValidator : IConfigValidator<RoundTypesConfig>
{
    public ValidationResult<RoundTypesConfig> Validate(RoundTypesConfig config, RoundTypesConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var roundTypes = CleanRoundTypes(config.RoundTypes, defaults.RoundTypes, file, issues);
        var names = roundTypes.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        var sequence = CleanSequence(config.Sequence, names, file, issues);
        var specific = names.Contains(config.Specific) ? config.Specific : Fallback(roundTypes[0].Name, config.Specific, file, issues);
        return new ValidationResult<RoundTypesConfig>(
            config with { RoundTypes = roundTypes, Sequence = sequence, Specific = specific }, issues);
    }

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
            kept.Add(roundType);
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
