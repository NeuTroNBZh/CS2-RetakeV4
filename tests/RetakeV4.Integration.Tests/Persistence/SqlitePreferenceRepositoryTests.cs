using Microsoft.Data.Sqlite;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public sealed class SqlitePreferenceRepositoryTests : IDisposable
{
    private const ulong Alice = 76561198000000001UL;
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private async Task<SqlitePreferenceRepository> Repository()
    {
        var repository = new SqlitePreferenceRepository(Path.Combine(_dir.Path, "data", "retakev4.db"));
        await repository.InitializeAsync(CancellationToken.None);
        return repository;
    }

    private static StoredPreference Preference(ulong steamId, TeamSide team, string roundType, string? primary, bool awp = false) =>
        new(new PreferenceKey(steamId, team, roundType), new LoadoutPreference(primary, "weapon_deagle", awp));

    [Fact]
    public async Task Upsert_ThenLoad_RoundTrips()
    {
        var repository = await Repository();
        await repository.UpsertAsync(Preference(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1_silencer"), CancellationToken.None);
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, PreferenceKey.AnyRoundType, null, awp: true), CancellationToken.None);
        var loaded = await repository.LoadAsync(Alice, CancellationToken.None);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(Preference(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1_silencer"), loaded);
        Assert.Contains(Preference(Alice, TeamSide.T, PreferenceKey.AnyRoundType, null, awp: true), loaded);
    }

    [Fact]
    public async Task Upsert_ReplacesTheSameKey()
    {
        var repository = await Repository();
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, "Mid", "weapon_mac10"), CancellationToken.None);
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, "Mid", "weapon_galilar"), CancellationToken.None);
        Assert.Equal("weapon_galilar", Assert.Single(await repository.LoadAsync(Alice, CancellationToken.None)).Preference.Primary);
    }

    [Fact]
    public async Task Load_OnlyReturnsTheRequestedPlayer()
    {
        var repository = await Repository();
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, "Mid", "weapon_mac10"), CancellationToken.None);
        Assert.Empty(await repository.LoadAsync(Alice + 1, CancellationToken.None));
    }

    [Fact]
    public async Task Import_StoresEverything_AndOverwrites()
    {
        var repository = await Repository();
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, "Mid", "weapon_mac10"), CancellationToken.None);
        await repository.ImportAsync(new[] { Preference(Alice, TeamSide.T, "Mid", "weapon_sg556"), Preference(Alice, TeamSide.CT, "Mid", "weapon_mp9") }, CancellationToken.None);
        var loaded = await repository.LoadAsync(Alice, CancellationToken.None);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, p => p.Preference.Primary == "weapon_sg556");
    }

    [Fact]
    public async Task Initialize_IsIdempotent()
    {
        await Repository();
        var again = await Repository();
        Assert.Empty(await again.LoadAsync(Alice, CancellationToken.None));
    }

    [Fact]
    public async Task LargeSteamId_RoundTrips()
    {
        var repository = await Repository();
        const ulong huge = ulong.MaxValue - 1;
        await repository.UpsertAsync(Preference(huge, TeamSide.CT, "Pistol", null), CancellationToken.None);
        Assert.Equal(huge, Assert.Single(await repository.LoadAsync(huge, CancellationToken.None)).Key.SteamId);
    }

    private async Task SetStamp(ulong steamId, string stamp)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_dir.Path, "data", "retakev4.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE player_loadout SET updated_at = @stamp WHERE steam_id = @steam_id";
        command.Parameters.AddWithValue("@stamp", stamp);
        command.Parameters.AddWithValue("@steam_id", unchecked((long)steamId));
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Stamps_ReturnTheLatestUpdate_OnlyForPlayersWithRows()
    {
        var repository = await Repository();
        await repository.UpsertAsync(Preference(Alice, TeamSide.T, "Mid", "weapon_mac10"), CancellationToken.None);
        await SetStamp(Alice, "2026-10-01T12:00:00.0000000+00:00");
        var stamps = await repository.StampsAsync(new[] { Alice, Alice + 1 }, CancellationToken.None);
        Assert.Equal("2026-10-01T12:00:00.0000000+00:00", Assert.Single(stamps!).Value);
        Assert.True(stamps!.ContainsKey(Alice));
    }

    [Fact]
    public async Task Stamps_OfNobody_AreEmpty() =>
        Assert.Empty((await (await Repository()).StampsAsync(Array.Empty<ulong>(), CancellationToken.None))!);

    [Fact]
    public async Task Stamps_HandleLargeSteamIds()
    {
        var repository = await Repository();
        const ulong huge = ulong.MaxValue - 1;
        await repository.UpsertAsync(Preference(huge, TeamSide.CT, "Pistol", null), CancellationToken.None);
        Assert.True((await repository.StampsAsync(new[] { huge }, CancellationToken.None))!.ContainsKey(huge));
        Assert.Equal(huge, Assert.Single((await repository.SnapshotAsync(huge, CancellationToken.None))!.Preferences).Key.SteamId);
    }

    [Fact]
    public async Task Snapshot_OfAPlayerWithoutRows_HasNoRowsStamp()
    {
        var snapshot = await (await Repository()).SnapshotAsync(Alice, CancellationToken.None);
        Assert.Empty(snapshot!.Preferences);
        Assert.Equal(PreferenceSync.NoRows, snapshot.Stamp);
    }

    [Fact]
    public async Task PublishCatalog_StoresOneRowPerServer()
    {
        var repository = await Repository();
        await repository.PublishCatalogAsync(new PublishedCatalog("default", 1, """{"roundTypes":[]}"""), CancellationToken.None);
        await repository.PublishCatalogAsync(new PublishedCatalog("default", 1, """{"roundTypes":[{"name":"Pistol"}]}"""), CancellationToken.None);
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_dir.Path, "data", "retakev4.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MAX(catalog), MAX(format_version) FROM retake_catalog WHERE server_key = 'default'";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Contains("Pistol", reader.GetString(1), StringComparison.Ordinal);
        Assert.Equal(1, reader.GetInt32(2));
    }

    [Fact]
    public async Task ExistingV1Database_GetsTheCatalogTable()
    {
        var file = Path.Combine(_dir.Path, "data", "retakev4.db");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await using (var connection = new SqliteConnection($"Data Source={file}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE player_loadout (steam_id INTEGER NOT NULL, team INTEGER NOT NULL, round_type TEXT NOT NULL,
                    primary_weapon TEXT NULL, secondary_weapon TEXT NULL, awp_opt_in INTEGER NOT NULL DEFAULT 0,
                    updated_at TEXT NOT NULL, PRIMARY KEY (steam_id, team, round_type));
                CREATE TABLE schema_version (version INTEGER NOT NULL);
                INSERT INTO schema_version (version) VALUES (1);
                INSERT INTO player_loadout VALUES (1, 0, 'Mid', 'weapon_mac10', NULL, 0, 'x');
                """;
            await command.ExecuteNonQueryAsync();
        }
        var repository = await Repository();
        await repository.PublishCatalogAsync(new PublishedCatalog("default", 1, "{}"), CancellationToken.None);
        Assert.Single(await repository.LoadAsync(1, CancellationToken.None));
    }
}
