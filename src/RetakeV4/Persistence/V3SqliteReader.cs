using Microsoft.Data.Sqlite;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public static class V3SqliteReader
{
    private static readonly string[] WeaponTables = { "FullBuyPrimary", "FullBuySecondary", "MidPrimary", "MidSecondary", "Pistol" };
    private const string AwpTable = "FullBuyAWPChance";

    public static async Task<IReadOnlyList<V3PreferenceRow>> ReadAsync(string databaseFile, CancellationToken ct)
    {
        if (!File.Exists(databaseFile))
        {
            throw new FileNotFoundException("V3 database not found", databaseFile);
        }
        var builder = new SqliteConnectionStringBuilder { DataSource = databaseFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(ct).ConfigureAwait(false);
        var existing = await ExistingTablesAsync(connection, ct).ConfigureAwait(false);
        var rows = new List<V3PreferenceRow>();
        foreach (var table in WeaponTables.Where(existing.Contains))
        {
            rows.AddRange(await ReadTableAsync(connection, table, "WeaponString", ct).ConfigureAwait(false));
        }
        if (existing.Contains(AwpTable))
        {
            rows.AddRange(await ReadTableAsync(connection, AwpTable, "AWPChance", ct).ConfigureAwait(false));
        }
        return rows;
    }

    private static async Task<HashSet<string>> ExistingTablesAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    // Table and column names come from the fixed lists above, never from user input.
    private static async Task<List<V3PreferenceRow>> ReadTableAsync(SqliteConnection connection, string table, string valueColumn, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT UserId, Team, {valueColumn} FROM {table}";
        var rows = new List<V3PreferenceRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var userId = unchecked((ulong)reader.GetInt64(0));
            var team = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            rows.Add(valueColumn == "AWPChance"
                ? new V3PreferenceRow(table, userId, team, null, reader.IsDBNull(2) ? null : reader.GetInt32(2))
                : new V3PreferenceRow(table, userId, team, reader.IsDBNull(2) ? null : reader.GetString(2), null));
        }
        return rows;
    }
}
