using MySqlConnector;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class MySqlPreferenceRepository : IPreferenceRepository
{
    private static readonly string[] Migrations =
    {
        """
        CREATE TABLE IF NOT EXISTS player_loadout (
            steam_id BIGINT UNSIGNED NOT NULL,
            team TINYINT NOT NULL,
            round_type VARCHAR(64) NOT NULL,
            primary_weapon VARCHAR(64) NULL,
            secondary_weapon VARCHAR(64) NULL,
            awp_opt_in TINYINT(1) NOT NULL DEFAULT 0,
            updated_at DATETIME(6) NOT NULL,
            PRIMARY KEY (steam_id, team, round_type))
        """,
    };

    private const string UpsertSql = """
        INSERT INTO player_loadout (steam_id, team, round_type, primary_weapon, secondary_weapon, awp_opt_in, updated_at)
        VALUES (@steam_id, @team, @round_type, @primary, @secondary, @awp, @updated_at)
        ON DUPLICATE KEY UPDATE
            primary_weapon = VALUES(primary_weapon),
            secondary_weapon = VALUES(secondary_weapon),
            awp_opt_in = VALUES(awp_opt_in),
            updated_at = VALUES(updated_at)
        """;

    private readonly string _connectionString;

    public MySqlPreferenceRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await SqlMigrator.ApplyAsync(connection, Migrations, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT team, round_type, primary_weapon, secondary_weapon, awp_opt_in FROM player_loadout WHERE steam_id = @steam_id";
        command.Parameters.AddWithValue("@steam_id", steamId);
        var result = new List<StoredPreference>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var team = reader.GetInt32(0) == (int)TeamSide.T ? TeamSide.T : TeamSide.CT;
            var preference = new LoadoutPreference(
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4));
            result.Add(new StoredPreference(new PreferenceKey(steamId, team, reader.GetString(1)), preference));
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
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var preference in preferences)
        {
            await using var command = CreateUpsert(connection, preference);
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static MySqlCommand CreateUpsert(MySqlConnection connection, StoredPreference preference)
    {
        var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        command.Parameters.AddWithValue("@steam_id", preference.Key.SteamId);
        command.Parameters.AddWithValue("@team", (int)preference.Key.Team);
        command.Parameters.AddWithValue("@round_type", preference.Key.RoundType);
        command.Parameters.AddWithValue("@primary", (object?)preference.Preference.Primary ?? DBNull.Value);
        command.Parameters.AddWithValue("@secondary", (object?)preference.Preference.Secondary ?? DBNull.Value);
        command.Parameters.AddWithValue("@awp", preference.Preference.AwpOptIn);
        command.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
        return command;
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
