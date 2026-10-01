using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public class PreferenceWriteQueueTests
{
    private static StoredPreference Preference(ulong steamId, string weapon) =>
        new(new PreferenceKey(steamId, TeamSide.T, "Mid"), new LoadoutPreference(weapon, null, false));

    [Fact]
    public async Task EnqueuedWrites_ReachTheStoreInOrder()
    {
        var repository = new FakePreferenceRepository();
        var queue = new PreferenceWriteQueue(repository, new ListLogger());
        queue.Enqueue(Preference(1, "weapon_mac10"));
        queue.Enqueue(Preference(1, "weapon_galilar"));
        await queue.DisposeAsync();
        Assert.Equal(new[] { "weapon_mac10", "weapon_galilar" }, repository.Upserts.Select(u => u.Preference.Primary));
    }

    [Fact]
    public async Task FailingWrite_IsLogged_AndDoesNotStopTheQueue()
    {
        // The failure is tied to the first write, not to timing: the queue may run both writes at any moment.
        var repository = new FakePreferenceRepository { FailUpsertFor = p => p.Key.SteamId == 1 };
        var logger = new ListLogger();
        var queue = new PreferenceWriteQueue(repository, logger);
        queue.Enqueue(Preference(1, "weapon_mac10"));
        queue.Enqueue(Preference(2, "weapon_galilar"));
        await queue.DisposeAsync();
        Assert.Equal("weapon_galilar", Assert.Single(repository.Upserts).Preference.Primary);
        Assert.NotEmpty(logger.Entries);
    }
}
