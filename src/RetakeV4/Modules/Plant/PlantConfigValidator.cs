using RetakeV4.Configuration;

namespace RetakeV4.Modules.Plant;

public sealed class PlantConfigValidator : IConfigValidator<PlantConfig>
{
    public ValidationResult<PlantConfig> Validate(PlantConfig config, PlantConfig defaults, string file) =>
        config.PlantCheckSeconds >= 0f
            ? new ValidationResult<PlantConfig>(config, Array.Empty<ConfigIssue>())
            : new ValidationResult<PlantConfig>(
                config with { PlantCheckSeconds = defaults.PlantCheckSeconds },
                new[] { new ConfigIssue(file, nameof(PlantConfig.PlantCheckSeconds), "must be >= 0 (0 disables the check); using default") });
}
