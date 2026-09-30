using System.Text.RegularExpressions;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Core;

public sealed partial class CoreConfigValidator : IConfigValidator<CoreConfig>
{
    private const float MinWatchdogInterval = 0.05f;
    private const float MaxWatchdogInterval = 5f;

    [GeneratedRegex(@"^[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)*\.cfg\z")]
    private static partial Regex SafeCfgPath();

    public ValidationResult<CoreConfig> Validate(CoreConfig config, CoreConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var result = config;
        if (!SafeCfgPath().IsMatch(config.ExecConfig ?? string.Empty))
        {
            issues.Add(new ConfigIssue(file, nameof(CoreConfig.ExecConfig), "must be a relative .cfg path (letters, digits, _ - /); using default"));
            result = result with { ExecConfig = defaults.ExecConfig };
        }
        if (config.WarmupFallbackSeconds < 0f)
        {
            issues.Add(new ConfigIssue(file, nameof(CoreConfig.WarmupFallbackSeconds), "must be >= 0; using default"));
            result = result with { WarmupFallbackSeconds = defaults.WarmupFallbackSeconds };
        }
        if (config.WatchdogIntervalSeconds is < MinWatchdogInterval or > MaxWatchdogInterval)
        {
            issues.Add(new ConfigIssue(file, nameof(CoreConfig.WatchdogIntervalSeconds), $"must be between {MinWatchdogInterval} and {MaxWatchdogInterval}; using default"));
            result = result with { WatchdogIntervalSeconds = defaults.WatchdogIntervalSeconds };
        }
        return new ValidationResult<CoreConfig>(result, issues);
    }
}
