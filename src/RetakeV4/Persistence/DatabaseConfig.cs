namespace RetakeV4.Persistence;

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

    // Identifies this server's row in retake_catalog and stats_servers, read by the web panel.
    public string ServerKey { get; init; } = "default";
}
