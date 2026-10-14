using System.Text.RegularExpressions;

namespace RetakeV4.Persistence;

// One schema step. An AddColumn step is skipped when its column already exists: MySQL commits DDL implicitly, so the
// ALTER may have run without its version row (crash, or a second server past the migration lock) and is replayed.
public sealed partial record SqlMigration(string Sql)
{
    [GeneratedRegex(@"^[a-z_][a-z0-9_]{0,63}\z")]
    private static partial Regex SafeIdentifier();

    public string? Table { get; private init; }

    public string? Column { get; private init; }

    public static SqlMigration AddColumn(string table, string column, string definition)
    {
        if (!SafeIdentifier().IsMatch(table ?? string.Empty) || !SafeIdentifier().IsMatch(column ?? string.Empty))
        {
            throw new ArgumentException("Table and column must be lowercase identifiers");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(definition);
        return new SqlMigration($"ALTER TABLE {table} ADD COLUMN {column} {definition}") { Table = table, Column = column };
    }
}
