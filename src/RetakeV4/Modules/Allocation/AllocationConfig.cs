using RetakeV4.Configuration;

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
}

public sealed record AllocationConfig : ModuleConfig
{
    public AllocationConfig() => Version = 2;

    public DatabaseConfig Database { get; init; } = new();
}
