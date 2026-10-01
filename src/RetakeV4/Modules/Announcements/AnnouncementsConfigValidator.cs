using RetakeV4.Configuration;

namespace RetakeV4.Modules.Announcements;

public sealed class AnnouncementsConfigValidator : IConfigValidator<AnnouncementsConfig>
{
    public const int MinIntervalSeconds = 30;
    public const int MaxMessageLength = 512;

    public ValidationResult<AnnouncementsConfig> Validate(AnnouncementsConfig config, AnnouncementsConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var interval = config.IntervalSeconds;
        if (interval < MinIntervalSeconds)
        {
            issues.Add(new ConfigIssue(file, nameof(config.IntervalSeconds), $"must be at least {MinIntervalSeconds}; using {defaults.IntervalSeconds}"));
            interval = defaults.IntervalSeconds;
        }
        var messages = Keep(config.Messages, nameof(config.Messages), file, issues);
        var maps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (map, list) in config.MapMessages ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            var key = map.Trim().ToLowerInvariant();
            var kept = Keep(list, $"{nameof(config.MapMessages)}.{map}", file, issues);
            if (maps.TryGetValue(key, out var existing))
            {
                issues.Add(new ConfigIssue(file, $"{nameof(config.MapMessages)}.{map}", $"map '{key}' is listed twice; messages merged"));
                kept = existing.Concat(kept).ToList();
            }
            maps[key] = kept;
        }
        var welcome = config.Welcome ?? string.Empty;
        if (welcome.Length > MaxMessageLength)
        {
            issues.Add(new ConfigIssue(file, nameof(config.Welcome), $"longer than {MaxMessageLength} characters; ignored"));
            welcome = string.Empty;
        }
        return new ValidationResult<AnnouncementsConfig>(
            config with { IntervalSeconds = interval, Messages = messages, MapMessages = maps, Welcome = welcome }, issues);
    }

    private static IReadOnlyList<string> Keep(IReadOnlyList<string>? messages, string key, string file, List<ConfigIssue> issues)
    {
        var kept = new List<string>();
        foreach (var message in messages ?? Array.Empty<string>())
        {
            if (message is { Length: > MaxMessageLength })
            {
                issues.Add(new ConfigIssue(file, key, $"a message is longer than {MaxMessageLength} characters; ignored"));
                continue;
            }
            if (!string.IsNullOrWhiteSpace(message))
            {
                kept.Add(message);
            }
        }
        return kept;
    }
}
