using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class NoOpPreferenceRepository : IPreferenceRepository
{
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredPreference>>(Array.Empty<StoredPreference>());

    public Task UpsertAsync(StoredPreference preference, CancellationToken ct) => Task.CompletedTask;

    public Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct) => Task.CompletedTask;

    public Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct) =>
        Task.FromResult<PlayerSnapshot?>(new PlayerSnapshot(Array.Empty<StoredPreference>(), PreferenceSync.NoRows));

    public Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<ulong, string>?>(new Dictionary<ulong, string>());

    public Task PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct) => Task.CompletedTask;
}
