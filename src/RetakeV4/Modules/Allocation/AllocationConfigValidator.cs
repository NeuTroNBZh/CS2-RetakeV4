using RetakeV4.Configuration;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

public sealed class AllocationConfigValidator : IConfigValidator<AllocationConfig>
{
    private const int MaxReminderMinutes = 120;

    public ValidationResult<AllocationConfig> Validate(AllocationConfig config, AllocationConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var database = DatabaseConfigValidator.Validate(config.Database, defaults.Database, file, nameof(AllocationConfig.Database), issues);
        var mode = config.Mode;
        if (!Enum.IsDefined(mode))
        {
            issues.Add(new ConfigIssue(file, nameof(AllocationConfig.Mode), "must be Menu, NativeBuy or Both; using Menu"));
            mode = AllocationMode.Menu;
        }
        var reminder = config.HowToIntervalMinutes;
        if (reminder is < 0 or > MaxReminderMinutes)
        {
            issues.Add(new ConfigIssue(file, nameof(AllocationConfig.HowToIntervalMinutes), $"must be between 0 and {MaxReminderMinutes}; using default"));
            reminder = defaults.HowToIntervalMinutes;
        }
        return new ValidationResult<AllocationConfig>(config with { Database = database, Mode = mode, HowToIntervalMinutes = reminder }, issues);
    }
}
