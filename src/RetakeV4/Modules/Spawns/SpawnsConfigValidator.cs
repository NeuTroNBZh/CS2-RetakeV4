using RetakeV4.Configuration;

namespace RetakeV4.Modules.Spawns;

public sealed class SpawnsConfigValidator : IConfigValidator<SpawnsConfig>
{
    public ValidationResult<SpawnsConfig> Validate(SpawnsConfig config, SpawnsConfig defaults, string file) =>
        config.MaxSameSiteInRow >= 0
            ? new ValidationResult<SpawnsConfig>(config, Array.Empty<ConfigIssue>())
            : new ValidationResult<SpawnsConfig>(
                config with { MaxSameSiteInRow = defaults.MaxSameSiteInRow },
                new[] { new ConfigIssue(file, nameof(SpawnsConfig.MaxSameSiteInRow), "must be >= 0; using default") });
}
