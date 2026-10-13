using System.Text.RegularExpressions;
using RetakeV4.Persistence;

namespace RetakeV4.Configuration;

// Shared by every module that owns a Database block (Allocation, Stats).
public static partial class DatabaseConfigValidator
{
    [GeneratedRegex(@"^[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)*\.db\z")]
    private static partial Regex SafeDatabaseFile();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,64}\z")]
    private static partial Regex SafeServerKey();

    public static DatabaseConfig Validate(DatabaseConfig? config, DatabaseConfig defaults, string file, string key, List<ConfigIssue> issues)
    {
        if (config is null)
        {
            issues.Add(new ConfigIssue(file, key, "missing; using defaults"));
            return defaults;
        }
        var database = config;
        if (!SafeDatabaseFile().IsMatch(database.SqliteFile ?? string.Empty))
        {
            issues.Add(new ConfigIssue(file, $"{key}.SqliteFile", "must be a relative .db path (letters, digits, _ - /); using default"));
            database = database with { SqliteFile = defaults.SqliteFile };
        }
        if (database.Type == DatabaseType.MySql && string.IsNullOrWhiteSpace(database.MySqlConnectionString))
        {
            issues.Add(new ConfigIssue(file, $"{key}.MySqlConnectionString", "MySql selected without a connection string; using Sqlite"));
            database = database with { Type = DatabaseType.Sqlite };
        }
        if (!SafeServerKey().IsMatch(database.ServerKey ?? string.Empty))
        {
            issues.Add(new ConfigIssue(file, $"{key}.ServerKey", "must be 1 to 64 letters, digits, _ or -; using default"));
            database = database with { ServerKey = defaults.ServerKey };
        }
        return database;
    }
}
