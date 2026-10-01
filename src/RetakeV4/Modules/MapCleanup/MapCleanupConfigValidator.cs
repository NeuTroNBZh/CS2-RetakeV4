using RetakeV4.Configuration;

namespace RetakeV4.Modules.MapCleanup;

public sealed class MapCleanupConfigValidator : IConfigValidator<MapCleanupConfig>
{
    public const int MaxEntitiesLimit = 4096;

    public ValidationResult<MapCleanupConfig> Validate(MapCleanupConfig config, MapCleanupConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var chance = config.DoorOpenChancePercent;
        if (chance is < 0 or > 100)
        {
            issues.Add(new ConfigIssue(file, nameof(config.DoorOpenChancePercent), $"must be 0 to 100; using {defaults.DoorOpenChancePercent}"));
            chance = defaults.DoorOpenChancePercent;
        }
        var max = config.MaxEntitiesPerRound;
        if (max is < 1 or > MaxEntitiesLimit)
        {
            issues.Add(new ConfigIssue(file, nameof(config.MaxEntitiesPerRound), $"must be 1 to {MaxEntitiesLimit}; using {defaults.MaxEntitiesPerRound}"));
            max = defaults.MaxEntitiesPerRound;
        }
        return new ValidationResult<MapCleanupConfig>(config with { DoorOpenChancePercent = chance, MaxEntitiesPerRound = max }, issues);
    }
}
