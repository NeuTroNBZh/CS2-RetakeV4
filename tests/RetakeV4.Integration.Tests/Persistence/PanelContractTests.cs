using System.Text.Json;
using Microsoft.Data.Sqlite;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

// Rows written the way the web panel writes them must load as the plugin expects.
public sealed class PanelContractTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private static JsonElement Contract() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "player_loadout.json"))).RootElement;

    [Fact]
    public void TeamValues_AndAwpKey_MatchThePlugin()
    {
        var contract = Contract();
        Assert.Equal((int)TeamSide.T, contract.GetProperty("teams").GetProperty("T").GetInt32());
        Assert.Equal((int)TeamSide.CT, contract.GetProperty("teams").GetProperty("CT").GetInt32());
        Assert.Equal(PreferenceKey.AnyRoundType, contract.GetProperty("anyRoundType").GetString());
    }

    [Fact]
    public async Task RowsWrittenByThePanel_LoadAsPreferences()
    {
        var file = Path.Combine(_dir.Path, "retakev4.db");
        var repository = new SqlitePreferenceRepository(file);
        await repository.InitializeAsync(CancellationToken.None);
        await using (var connection = new SqliteConnection($"Data Source={file}"))
        {
            await connection.OpenAsync();
            foreach (var row in Contract().GetProperty("rows").EnumerateArray())
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO player_loadout (steam_id, team, round_type, primary_weapon, secondary_weapon, awp_opt_in, updated_at)
                    VALUES (@steam_id, @team, @round_type, @primary, @secondary, @awp, @updated_at)
                    """;
                insert.Parameters.AddWithValue("@steam_id", unchecked((long)ulong.Parse(row.GetProperty("steam_id").GetString()!)));
                insert.Parameters.AddWithValue("@team", row.GetProperty("team").GetInt32());
                insert.Parameters.AddWithValue("@round_type", row.GetProperty("round_type").GetString());
                insert.Parameters.AddWithValue("@primary", (object?)row.GetProperty("primary_weapon").GetString() ?? DBNull.Value);
                insert.Parameters.AddWithValue("@secondary", (object?)row.GetProperty("secondary_weapon").GetString() ?? DBNull.Value);
                insert.Parameters.AddWithValue("@awp", row.GetProperty("awp_opt_in").GetInt32());
                insert.Parameters.AddWithValue("@updated_at", row.GetProperty("updated_at").GetString());
                await insert.ExecuteNonQueryAsync();
            }
        }
        var snapshot = await repository.SnapshotAsync(76561198000000001UL, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("2026-10-01 12:00:00.000002", snapshot.Stamp);
        Assert.Contains(new StoredPreference(new PreferenceKey(76561198000000001UL, TeamSide.CT, "FullBuy"), new LoadoutPreference("weapon_m4a1_silencer", null, false)), snapshot.Preferences);
        Assert.Contains(new StoredPreference(new PreferenceKey(76561198000000001UL, TeamSide.T, PreferenceKey.AnyRoundType), new LoadoutPreference(null, null, true)), snapshot.Preferences);
    }
}
