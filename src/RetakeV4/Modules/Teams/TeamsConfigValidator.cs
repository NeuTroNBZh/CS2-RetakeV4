using RetakeV4.Configuration;

namespace RetakeV4.Modules.Teams;

public sealed class TeamsConfigValidator : IConfigValidator<TeamsConfig>
{
    private const int MinPlayers = 2;
    private const int MaxServerPlayers = 64;

    public ValidationResult<TeamsConfig> Validate(TeamsConfig config, TeamsConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var result = config;
        if (config.MaxPlayers is < MinPlayers or > MaxServerPlayers)
        {
            issues.Add(new ConfigIssue(file, nameof(TeamsConfig.MaxPlayers), $"must be between {MinPlayers} and {MaxServerPlayers}; using default"));
            result = result with { MaxPlayers = defaults.MaxPlayers };
        }
        if (config.TeamBalanceRatio is <= 0 or >= 1)
        {
            issues.Add(new ConfigIssue(file, nameof(TeamsConfig.TeamBalanceRatio), "must be strictly between 0 and 1; using default"));
            result = result with { TeamBalanceRatio = defaults.TeamBalanceRatio };
        }
        if (config.ScrambleAfterTWins < 0)
        {
            issues.Add(new ConfigIssue(file, nameof(TeamsConfig.ScrambleAfterTWins), "must be >= 0 (0 disables); using default"));
            result = result with { ScrambleAfterTWins = defaults.ScrambleAfterTWins };
        }
        return new ValidationResult<TeamsConfig>(result with { PriorityFlags = CleanFlags(config.PriorityFlags, file, issues) }, issues);
    }

    private static IReadOnlyList<PriorityFlagConfig> CleanFlags(IReadOnlyList<PriorityFlagConfig>? flags, string file, List<ConfigIssue> issues)
    {
        var kept = new List<PriorityFlagConfig>();
        foreach (var flag in flags ?? Array.Empty<PriorityFlagConfig>())
        {
            var validName = flag.Flag.StartsWith('@') || flag.Flag.StartsWith('#');
            if (!validName || flag.Priority <= 0)
            {
                issues.Add(new ConfigIssue(file, nameof(TeamsConfig.PriorityFlags), $"entry '{flag.Flag}' (priority {flag.Priority}) removed: flag must start with @ or #, priority must be > 0"));
                continue;
            }
            kept.Add(flag);
        }
        return kept;
    }
}
