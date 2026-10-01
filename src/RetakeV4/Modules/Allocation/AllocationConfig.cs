using RetakeV4.Configuration;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

public enum DatabaseType
{
    Sqlite,
    MySql,
    None,
}

public sealed record DatabaseConfig
{
    public DatabaseType Type { get; init; } = DatabaseType.Sqlite;

    public string SqliteFile { get; init; } = "data/retakev4.db";

    public string MySqlConnectionString { get; init; } = string.Empty;

    // Identifies this server's row in retake_catalog, read by the web panel.
    public string ServerKey { get; init; } = "default";
}

public sealed record AllocationConfig : ModuleConfig
{
    public AllocationConfig() => Version = 4;

    public DatabaseConfig Database { get; init; } = new();

    public bool AutoOpenMenu { get; init; } = true;

    public AllocationMode Mode { get; init; } = AllocationMode.Menu;

    public int HowToIntervalMinutes { get; init; } = 5;
}
