using System.Text.RegularExpressions;
using RetakeV4.Configuration;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

public sealed partial class AllocationConfigValidator : IConfigValidator<AllocationConfig>
{
    private const int MaxReminderMinutes = 120;

    [GeneratedRegex(@"^[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)*\.db\z")]
    private static partial Regex SafeDatabaseFile();

    public ValidationResult<AllocationConfig> Validate(AllocationConfig config, AllocationConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var database = config.Database ?? Missing(defaults.Database, file, issues);
        if (!SafeDatabaseFile().IsMatch(database.SqliteFile ?? string.Empty))
        {
            issues.Add(new ConfigIssue(file, "Database.SqliteFile", "must be a relative .db path (letters, digits, _ - /); using default"));
            database = database with { SqliteFile = defaults.Database.SqliteFile };
        }
        if (database.Type == DatabaseType.MySql && string.IsNullOrWhiteSpace(database.MySqlConnectionString))
        {
            issues.Add(new ConfigIssue(file, "Database.MySqlConnectionString", "MySql selected without a connection string; using Sqlite"));
            database = database with { Type = DatabaseType.Sqlite };
        }
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

    private static DatabaseConfig Missing(DatabaseConfig defaults, string file, List<ConfigIssue> issues)
    {
        issues.Add(new ConfigIssue(file, nameof(AllocationConfig.Database), "missing; using defaults"));
        return defaults;
    }
}
