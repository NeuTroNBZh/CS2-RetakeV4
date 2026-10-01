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
}
