using System.Text.RegularExpressions;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Allocation;

public sealed partial class AllocationConfigValidator : IConfigValidator<AllocationConfig>
{
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
        return new ValidationResult<AllocationConfig>(config with { Database = database }, issues);
    }

    private static DatabaseConfig Missing(DatabaseConfig defaults, string file, List<ConfigIssue> issues)
    {
        issues.Add(new ConfigIssue(file, nameof(AllocationConfig.Database), "missing; using defaults"));
        return defaults;
    }
}
