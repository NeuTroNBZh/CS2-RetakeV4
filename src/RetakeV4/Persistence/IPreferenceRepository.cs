using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public interface IPreferenceRepository
{
    Task InitializeAsync(CancellationToken ct);

    Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct);

    // Null only when the store is unavailable: a player without rows gets an empty snapshot stamped PreferenceSync.NoRows.
    Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct);

    // Players without rows are absent. Null only when the store is unavailable.
    Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct);

    Task UpsertAsync(StoredPreference preference, CancellationToken ct);

    Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct);

    // False when the store is unavailable: the caller retries later.
    Task<bool> PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct);
}
