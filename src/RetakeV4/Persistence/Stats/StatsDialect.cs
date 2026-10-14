using System.Globalization;

namespace RetakeV4.Persistence.Stats;

// Everything that differs between MySQL 8 and SQLite for the stats schema and queries.
public sealed class StatsDialect
{
    private StatsDialect()
    {
    }

    public static StatsDialect MySql { get; } = new()
    {
        IsMySql = true,
        IdPrimaryKey = "BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY",
        SteamId = "BIGINT UNSIGNED",
        BigInt = "BIGINT",
        Int = "INT",
        Bool = "TINYINT(1)",
        Real = "DOUBLE",
        DateTime = "DATETIME(3)",
        TableOptions = " ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin",
        LastInsertId = "SELECT LAST_INSERT_ID()",
        Greatest = "GREATEST",
    };

    public static StatsDialect Sqlite { get; } = new()
    {
        IsMySql = false,
        IdPrimaryKey = "INTEGER PRIMARY KEY AUTOINCREMENT",
        SteamId = "INTEGER",
        BigInt = "INTEGER",
        Int = "INTEGER",
        Bool = "INTEGER",
        Real = "REAL",
        DateTime = "TEXT",
        TableOptions = string.Empty,
        LastInsertId = "SELECT last_insert_rowid()",
        Greatest = "MAX",
    };

    public bool IsMySql { get; private init; }

    public string IdPrimaryKey { get; private init; } = string.Empty;

    public string SteamId { get; private init; } = string.Empty;

    public string BigInt { get; private init; } = string.Empty;

    public string Int { get; private init; } = string.Empty;

    public string Bool { get; private init; } = string.Empty;

    public string Real { get; private init; } = string.Empty;

    public string DateTime { get; private init; } = string.Empty;

    public string TableOptions { get; private init; } = string.Empty;

    public string LastInsertId { get; private init; } = string.Empty;

    public string Greatest { get; private init; } = string.Empty;

    public string CreateView => IsMySql ? "CREATE OR REPLACE VIEW" : "CREATE VIEW IF NOT EXISTS";

    public string FloatCast(string expression) => IsMySql ? $"CAST({expression} AS DOUBLE)" : $"CAST({expression} AS REAL)";

    // Integer aggregates of the derived views: MySQL's SUM returns DECIMAL, which most drivers hand over as a string.
    public string IntCast(string expression) => IsMySql ? $"CAST({expression} AS SIGNED)" : $"CAST({expression} AS INTEGER)";

    // UTC calendar day (yyyy-MM-dd text) and hour (0-23 integer) of a stored date: dates are stored in UTC on both
    // backends (DATETIME(3) on MySQL, sortable "yyyy-MM-dd HH:mm:ss.fff" text on SQLite).
    public string UtcDay(string expression) => IsMySql ? $"DATE_FORMAT({expression}, '%Y-%m-%d')" : $"SUBSTR({expression}, 1, 10)";

    public string UtcHour(string expression) => IsMySql ? $"HOUR({expression})" : $"CAST(SUBSTR({expression}, 12, 2) AS INTEGER)";

    public string Text(int length) => IsMySql ? $"VARCHAR({length.ToString(CultureInfo.InvariantCulture)})" : "TEXT";

    // Replaced columns take the new value; added columns are summed with the stored one.
    public string OnConflict(IReadOnlyList<string> keys, IReadOnlyList<string> replace, IReadOnlyList<string> add)
    {
        if (IsMySql)
        {
            return "ON DUPLICATE KEY UPDATE " + string.Join(", ",
                replace.Select(c => $"{c} = VALUES({c})").Concat(add.Select(c => $"{c} = {c} + VALUES({c})")));
        }
        return $"ON CONFLICT ({string.Join(", ", keys)}) DO UPDATE SET " + string.Join(", ",
            replace.Select(c => $"{c} = excluded.{c}").Concat(add.Select(c => $"{c} = {c} + excluded.{c}")));
    }

    // Upsert that only moves a timestamp forward (GREATEST on MySQL / MariaDB, scalar MAX on SQLite).
    public string OnConflictKeepLatest(IReadOnlyList<string> keys, string column, string parameter) => IsMySql
        ? $"ON DUPLICATE KEY UPDATE {column} = {Greatest}({column}, {parameter})"
        : $"ON CONFLICT ({string.Join(", ", keys)}) DO UPDATE SET {column} = {Greatest}({column}, {parameter})";

    // MySQL refuses LIMIT inside an IN subquery; SQLite has no DELETE ... LIMIT by default.
    public string DeleteBatch(string table, int batchSize)
    {
        var limit = batchSize.ToString(CultureInfo.InvariantCulture);
        return IsMySql
            ? $"DELETE FROM {table} WHERE round_id <= @max_round LIMIT {limit}"
            : $"DELETE FROM {table} WHERE id IN (SELECT id FROM {table} WHERE round_id <= @max_round LIMIT {limit})";
    }

    // Time-based purge (presence snapshots, server events), same batching rules as DeleteBatch.
    public string DeleteBatchBefore(string table, int batchSize)
    {
        var limit = batchSize.ToString(CultureInfo.InvariantCulture);
        return IsMySql
            ? $"DELETE FROM {table} WHERE at < @cutoff LIMIT {limit}"
            : $"DELETE FROM {table} WHERE id IN (SELECT id FROM {table} WHERE at < @cutoff LIMIT {limit})";
    }

    public object SteamParam(ulong steamId) => IsMySql ? steamId : unchecked((long)steamId);

    public ulong ReadSteam(object value) => IsMySql
        ? Convert.ToUInt64(value, CultureInfo.InvariantCulture)
        : unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture));

    // SQLite stores sortable text so that range comparisons (purge) work.
    public object DateParam(DateTimeOffset at) => IsMySql
        ? at.UtcDateTime
        : at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
