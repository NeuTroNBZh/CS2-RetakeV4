using System.Data.Common;

namespace RetakeV4.Persistence;

public static class SqlMigrator
{
    public static async Task<int> ApplyAsync(DbConnection connection, IReadOnlyList<string> migrations, CancellationToken ct)
    {
        await ExecuteAsync(connection, null, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)", ct).ConfigureAwait(false);
        var current = await CurrentVersionAsync(connection, ct).ConfigureAwait(false);
        for (var index = current; index < migrations.Count; index++)
        {
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, migrations[index], ct).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "DELETE FROM schema_version", ct).ConfigureAwait(false);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO schema_version (version) VALUES (@version)";
            var parameter = insert.CreateParameter();
            parameter.ParameterName = "@version";
            parameter.Value = index + 1;
            insert.Parameters.Add(parameter);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        return migrations.Count;
    }

    private static async Task<int> CurrentVersionAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
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
