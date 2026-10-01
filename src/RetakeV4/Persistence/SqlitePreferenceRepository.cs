using Microsoft.Data.Sqlite;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class SqlitePreferenceRepository : IPreferenceRepository
{
    private static readonly string[] Migrations =
    {
        """
        CREATE TABLE IF NOT EXISTS player_loadout (
            steam_id INTEGER NOT NULL,
            team INTEGER NOT NULL,
            round_type TEXT NOT NULL,
            primary_weapon TEXT NULL,
            secondary_weapon TEXT NULL,
            awp_opt_in INTEGER NOT NULL DEFAULT 0,
            updated_at TEXT NOT NULL,
            PRIMARY KEY (steam_id, team, round_type))
        """,
        """
        CREATE TABLE IF NOT EXISTS retake_catalog (
            server_key TEXT NOT NULL PRIMARY KEY,
            format_version INTEGER NOT NULL,
            catalog TEXT NOT NULL,
            updated_at TEXT NOT NULL)
        """,
    };

    private const string UpsertSql = """
        INSERT INTO player_loadout (steam_id, team, round_type, primary_weapon, secondary_weapon, awp_opt_in, updated_at)
        VALUES (@steam_id, @team, @round_type, @primary, @secondary, @awp, @updated_at)
        ON CONFLICT (steam_id, team, round_type) DO UPDATE SET
            primary_weapon = excluded.primary_weapon,
            secondary_weapon = excluded.secondary_weapon,
            awp_opt_in = excluded.awp_opt_in,
            updated_at = excluded.updated_at
        """;

    private const string PublishCatalogSql = """
        INSERT INTO retake_catalog (server_key, format_version, catalog, updated_at)
        VALUES (@server_key, @format_version, @catalog, @updated_at)
        ON CONFLICT (server_key) DO UPDATE SET
            format_version = excluded.format_version,
            catalog = excluded.catalog,
            updated_at = excluded.updated_at
        """;

    private readonly string _databaseFile;
    private readonly string _connectionString;

    public SqlitePreferenceRepository(string databaseFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        _databaseFile = databaseFile;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databaseFile }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_databaseFile))!);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await SqlMigrator.ApplyAsync(connection, Migrations, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await ReadRowsAsync(connection, steamId, ct).ConfigureAwait(false);
    }

    public async Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var rows = await ReadRowsAsync(connection, steamId, ct).ConfigureAwait(false);
        var stamps = await ReadStampsAsync(connection, new[] { steamId }, ct).ConfigureAwait(false);
        return new PlayerSnapshot(rows, stamps.GetValueOrDefault(steamId, PreferenceSync.NoRows));
    }

    public async Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct)
    {
        if (steamIds.Count == 0)
        {
            return new Dictionary<ulong, string>();
        }
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await ReadStampsAsync(connection, steamIds, ct).ConfigureAwait(false);
    }

    public async Task PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = PublishCatalogSql;
        command.Parameters.AddWithValue("@server_key", catalog.ServerKey);
        command.Parameters.AddWithValue("@format_version", catalog.FormatVersion);
        command.Parameters.AddWithValue("@catalog", catalog.Json);
        command.Parameters.AddWithValue("@updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<StoredPreference>> ReadRowsAsync(SqliteConnection connection, ulong steamId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT team, round_type, primary_weapon, secondary_weapon, awp_opt_in FROM player_loadout WHERE steam_id = @steam_id";
        command.Parameters.AddWithValue("@steam_id", unchecked((long)steamId));
        var result = new List<StoredPreference>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var team = reader.GetInt32(0) == (int)TeamSide.T ? TeamSide.T : TeamSide.CT;
            var preference = new LoadoutPreference(
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4) != 0);
            result.Add(new StoredPreference(new PreferenceKey(steamId, team, reader.GetString(1)), preference));
        }
        return result;
    }

    private static async Task<Dictionary<ulong, string>> ReadStampsAsync(SqliteConnection connection, IReadOnlyCollection<ulong> steamIds, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        var names = new List<string>();
        foreach (var steamId in steamIds)
        {
            var name = $"@s{names.Count}";
            command.Parameters.AddWithValue(name, unchecked((long)steamId));
            names.Add(name);
        }
        command.CommandText = $"SELECT steam_id, MAX(updated_at) FROM player_loadout WHERE steam_id IN ({string.Join(", ", names)}) GROUP BY steam_id";
        var result = new Dictionary<ulong, string>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result[unchecked((ulong)reader.GetInt64(0))] = reader.GetString(1);
        }
        return result;
    }

    public async Task UpsertAsync(StoredPreference preference, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = CreateUpsert(connection, preference);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var preference in preferences)
        {
            await using var command = CreateUpsert(connection, preference);
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static SqliteCommand CreateUpsert(SqliteConnection connection, StoredPreference preference)
    {
        var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        command.Parameters.AddWithValue("@steam_id", unchecked((long)preference.Key.SteamId));
        command.Parameters.AddWithValue("@team", (int)preference.Key.Team);
        command.Parameters.AddWithValue("@round_type", preference.Key.RoundType);
        command.Parameters.AddWithValue("@primary", (object?)preference.Preference.Primary ?? DBNull.Value);
        command.Parameters.AddWithValue("@secondary", (object?)preference.Preference.Secondary ?? DBNull.Value);
        command.Parameters.AddWithValue("@awp", preference.Preference.AwpOptIn ? 1 : 0);
        command.Parameters.AddWithValue("@updated_at", DateTimeOffset.UtcNow.ToString("O"));
        return command;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
