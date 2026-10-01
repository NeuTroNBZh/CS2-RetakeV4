using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public class MySqlPreferenceRepositoryTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RETAKEV4_MYSQL_TEST");

    [Fact]
    public async Task RoundTrip_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var repository = new MySqlPreferenceRepository(ConnectionString);
        await repository.InitializeAsync(CancellationToken.None);
        var steamId = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        var preference = new StoredPreference(new PreferenceKey(steamId, TeamSide.CT, "FullBuy"), new LoadoutPreference("weapon_m4a1", "weapon_deagle", true));
        await repository.UpsertAsync(preference, CancellationToken.None);
        await repository.UpsertAsync(preference with { Preference = preference.Preference with { Primary = "weapon_aug" } }, CancellationToken.None);
        var loaded = Assert.Single(await repository.LoadAsync(steamId, CancellationToken.None));
        Assert.Equal("weapon_aug", loaded.Preference.Primary);
        Assert.True(loaded.Preference.AwpOptIn);
    }

    [Fact]
    public void Constructor_RejectsBlankConnectionString() =>
        Assert.Throws<ArgumentException>(() => new MySqlPreferenceRepository(" "));
}
