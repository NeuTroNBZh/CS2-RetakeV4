using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace RetakeV4.Persistence;

public static partial class SqlMigrator
{
    private const int LockTimeoutSeconds = 60;

    [GeneratedRegex(@"^[a-z_]{1,64}\z")]
    private static partial Regex SafeTableName();

    public static Task<int> ApplyAsync(DbConnection connection, IReadOnlyList<string> migrations, CancellationToken ct, string versionTable = "schema_version")
    {
        ArgumentNullException.ThrowIfNull(migrations);
        return ApplyAsync(connection, migrations.Select(sql => new SqlMigration(sql)).ToList(), ct, versionTable);
    }

    public static async Task<int> ApplyAsync(DbConnection connection, IReadOnlyList<SqlMigration> migrations, CancellationToken ct, string versionTable = "schema_version")
    {
        ArgumentNullException.ThrowIfNull(migrations);
        if (!SafeTableName().IsMatch(versionTable))
        {
            throw new ArgumentException("Version table must be lowercase letters and underscores", nameof(versionTable));
        }
        // MySQL commits DDL implicitly, so the transaction below cannot keep two servers that start together from
        // writing the same version row: a named lock serializes migrators of the same database.
        var locked = connection is MySqlConnection && await AcquireLockAsync(connection, versionTable, ct).ConfigureAwait(false);
        try
        {
            return await ApplyLockedAsync(connection, migrations, versionTable, ct).ConfigureAwait(false);
        }
        finally
        {
            if (locked)
            {
                await ReleaseLockAsync(connection, versionTable).ConfigureAwait(false);
            }
        }
    }

    private static async Task<int> ApplyLockedAsync(DbConnection connection, IReadOnlyList<SqlMigration> migrations, string versionTable, CancellationToken ct)
    {
        await ExecuteAsync(connection, null, $"CREATE TABLE IF NOT EXISTS {versionTable} (version INTEGER NOT NULL PRIMARY KEY)", ct).ConfigureAwait(false);
        var current = await CurrentVersionAsync(connection, versionTable, ct).ConfigureAwait(false);
        for (var index = current; index < migrations.Count; index++)
        {
            var migration = migrations[index];
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            if (!await AlreadyAppliedAsync(connection, transaction, migration, ct).ConfigureAwait(false))
            {
                await ExecuteAsync(connection, transaction, migration.Sql, ct).ConfigureAwait(false);
            }
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

    // Only an AddColumn step can tell that it already ran: its column exists.
    private static async Task<bool> AlreadyAppliedAsync(DbConnection connection, DbTransaction transaction, SqlMigration migration, CancellationToken ct)
    {
        if (migration.Table is not { } table || migration.Column is not { } column)
        {
            return false;
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = connection is MySqlConnection
            ? "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column"
            : "SELECT COUNT(*) FROM pragma_table_info(@table) WHERE name = @column";
        AddParameter(command, "@table", table);
        AddParameter(command, "@column", column);
        var count = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(count, CultureInfo.InvariantCulture) > 0;
    }

    // Named locks are server-wide: the name is derived from the database and the version table (max 64 characters).
    private const string LockName = "LEFT(SHA2(CONCAT('retakev4/', DATABASE(), '/', @table), 256), 60)";

    private static async Task<bool> AcquireLockAsync(DbConnection connection, string versionTable, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT GET_LOCK({LockName}, @timeout)";
        AddParameter(command, "@table", versionTable);
        AddParameter(command, "@timeout", LockTimeoutSeconds);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null or DBNull || Convert.ToInt32(result, CultureInfo.InvariantCulture) != 1)
        {
            throw new TimeoutException($"Could not take the migration lock for {versionTable} within {LockTimeoutSeconds} s");
        }
        return true;
    }

    private static async Task ReleaseLockAsync(DbConnection connection, string versionTable)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT RELEASE_LOCK({LockName})";
        AddParameter(command, "@table", versionTable);
        await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
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
