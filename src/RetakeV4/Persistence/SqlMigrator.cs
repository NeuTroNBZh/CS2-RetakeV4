using System.Data.Common;
using System.Text.RegularExpressions;

namespace RetakeV4.Persistence;

public static partial class SqlMigrator
{
    [GeneratedRegex(@"^[a-z_]{1,64}\z")]
    private static partial Regex SafeTableName();

    public static async Task<int> ApplyAsync(DbConnection connection, IReadOnlyList<string> migrations, CancellationToken ct, string versionTable = "schema_version")
    {
        if (!SafeTableName().IsMatch(versionTable))
        {
            throw new ArgumentException("Version table must be lowercase letters and underscores", nameof(versionTable));
        }
        await ExecuteAsync(connection, null, $"CREATE TABLE IF NOT EXISTS {versionTable} (version INTEGER NOT NULL PRIMARY KEY)", ct).ConfigureAwait(false);
        var current = await CurrentVersionAsync(connection, versionTable, ct).ConfigureAwait(false);
        for (var index = current; index < migrations.Count; index++)
        {
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, migrations[index], ct).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, $"DELETE FROM {versionTable}", ct).ConfigureAwait(false);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {versionTable} (version) VALUES (@version)";
            var parameter = insert.CreateParameter();
            parameter.ParameterName = "@version";
            parameter.Value = index + 1;
            insert.Parameters.Add(parameter);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        return migrations.Count;
    }

    private static async Task<int> CurrentVersionAsync(DbConnection connection, string versionTable, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COALESCE(MAX(version), 0) FROM {versionTable}";
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(value);
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
