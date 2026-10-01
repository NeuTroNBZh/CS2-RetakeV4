using RetakeV4.Configuration;

namespace RetakeV4.Modules.MapVote;

public sealed class MapVoteConfigValidator : IConfigValidator<MapVoteConfig>
{
    public ValidationResult<MapVoteConfig> Validate(MapVoteConfig config, MapVoteConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        int InRange(int value, int min, int max, int fallback, string key)
        {
            if (value >= min && value <= max)
            {
                return value;
            }
            issues.Add(new ConfigIssue(file, key, $"must be {min} to {max}; using {fallback}"));
            return fallback;
        }
        var result = config with
        {
            TriggerRoundsBeforeEnd = InRange(config.TriggerRoundsBeforeEnd, 1, 10, defaults.TriggerRoundsBeforeEnd, nameof(config.TriggerRoundsBeforeEnd)),
            VoteSeconds = InRange(config.VoteSeconds, 10, 120, defaults.VoteSeconds, nameof(config.VoteSeconds)),
            ChangeDelaySeconds = InRange(config.ChangeDelaySeconds, 3, 30, defaults.ChangeDelaySeconds, nameof(config.ChangeDelaySeconds)),
            RtvPercentage = InRange(config.RtvPercentage, 1, 100, defaults.RtvPercentage, nameof(config.RtvPercentage)),
            RtvMinPlayers = InRange(config.RtvMinPlayers, 1, 64, defaults.RtvMinPlayers, nameof(config.RtvMinPlayers)),
            RtvMinRounds = InRange(config.RtvMinRounds, 0, 30, defaults.RtvMinRounds, nameof(config.RtvMinRounds)),
            ExcludedMaps = config.ExcludedMaps ?? Array.Empty<string>(),
        };
        return new ValidationResult<MapVoteConfig>(result, issues);
    }
}
