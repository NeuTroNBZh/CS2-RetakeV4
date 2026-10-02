using RetakeV4.Configuration;

namespace RetakeV4.Modules.Remote;

public sealed class RemoteConfigValidator : IConfigValidator<RemoteConfig>
{
    public ValidationResult<RemoteConfig> Validate(RemoteConfig config, RemoteConfig defaults, string file) =>
        new(config, Array.Empty<ConfigIssue>());
}
