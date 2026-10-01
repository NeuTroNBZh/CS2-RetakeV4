using RetakeV4.Configuration;

namespace RetakeV4.Modules.InstaDefuse;

public sealed class InstaDefuseConfigValidator : IConfigValidator<InstaDefuseConfig>
{
    public ValidationResult<InstaDefuseConfig> Validate(InstaDefuseConfig config, InstaDefuseConfig defaults, string file) =>
        config.InfernoDistance >= 0f
            ? new ValidationResult<InstaDefuseConfig>(config, Array.Empty<ConfigIssue>())
            : new ValidationResult<InstaDefuseConfig>(
                config with { InfernoDistance = defaults.InfernoDistance },
                new[] { new ConfigIssue(file, nameof(InstaDefuseConfig.InfernoDistance), "must be >= 0; using default") });
}
