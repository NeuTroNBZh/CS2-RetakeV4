using RetakeV4.Modules.Allocation;
using RetakeV4.Modules.Stats;
using RetakeV4.Persistence;

namespace RetakeV4.Configuration;

// The web panel reads preferences and stats from one database and one server key: both modules must agree.
public static class DatabaseConsistencyCheck
{
    public static IReadOnlyList<string> Check(AllocationConfig allocation, StatsConfig stats)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(stats);
        var a = allocation.Database;
        var s = stats.Database;
        if (!stats.Enabled || a.Type == DatabaseType.None || s.Type == DatabaseType.None)
        {
            return Array.Empty<string>();
        }
        var same = a.Type == s.Type
            && a.ServerKey == s.ServerKey
            && (a.Type == DatabaseType.MySql ? a.MySqlConnectionString == s.MySqlConnectionString : a.SqliteFile == s.SqliteFile);
        return same
            ? Array.Empty<string>()
            : new[] { "stats.json: Database differs from allocation.json (type, connection, file or ServerKey); the web panel expects both in the same database" };
    }
}
