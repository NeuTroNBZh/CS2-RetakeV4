using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public class ResilientPreferenceStoreTests
{
    private readonly FakePreferenceRepository _inner = new();
    private readonly ListLogger _logger = new();
    private DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private ResilientPreferenceStore Store() => new(_inner, _logger, () => _now);

    private static StoredPreference Preference(ulong steamId) =>
        new(new PreferenceKey(steamId, TeamSide.CT, "FullBuy"), new LoadoutPreference("weapon_m4a1", null, false));

    [Fact]
    public async Task HealthyRepository_IsUsedDirectly()
    {
        _inner.Stored.Add(Preference(1));
        var store = Store();
        Assert.Single(await store.LoadAsync(1, CancellationToken.None));
        Assert.True(store.IsAvailable);
    }

    [Fact]
    public async Task Failure_ReturnsEmpty_LogsOnce_AndSkipsCallsDuringBackoff()
    {
        _inner.Fail = true;
        var store = Store();
        Assert.Empty(await store.LoadAsync(1, CancellationToken.None));
        Assert.Empty(await store.LoadAsync(1, CancellationToken.None));
        await store.UpsertAsync(Preference(1), CancellationToken.None);
        Assert.Equal(1, _inner.LoadCalls);
        Assert.Single(_logger.Entries);
        Assert.False(store.IsAvailable);
    }

    [Fact]
    public async Task AfterBackoff_RepositoryIsRetried_AndSuccessRecovers()
    {
        _inner.Fail = true;
        var store = Store();
        await store.LoadAsync(1, CancellationToken.None);
        _inner.Fail = false;
        _now = _now.AddSeconds(6);
        await store.LoadAsync(1, CancellationToken.None);
        Assert.Equal(2, _inner.LoadCalls);
        Assert.True(store.IsAvailable);
    }

    [Fact]
    public async Task ConsecutiveFailures_DoubleTheBackoff()
    {
        _inner.Fail = true;
        var store = Store();
        await store.LoadAsync(1, CancellationToken.None);
        _now = _now.AddSeconds(6);
        await store.LoadAsync(1, CancellationToken.None);
        _now = _now.AddSeconds(6);
        await store.LoadAsync(1, CancellationToken.None);
        Assert.Equal(2, _inner.LoadCalls);
        _now = _now.AddSeconds(5);
        await store.LoadAsync(1, CancellationToken.None);
        Assert.Equal(3, _inner.LoadCalls);
    }

    [Fact]
    public async Task Import_PropagatesFailures()
    {
        _inner.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().ImportAsync(new[] { Preference(1) }, CancellationToken.None));
    }

    [Fact]
    public async Task NoOpRepository_StoresNothing()
    {
        var repository = new NoOpPreferenceRepository();
        await repository.UpsertAsync(Preference(1), CancellationToken.None);
        await repository.ImportAsync(new[] { Preference(1) }, CancellationToken.None);
        Assert.Empty(await repository.LoadAsync(1, CancellationToken.None));
    }
}
