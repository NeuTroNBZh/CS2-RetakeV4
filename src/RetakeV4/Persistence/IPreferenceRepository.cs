using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public interface IPreferenceRepository
{
    Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct);

    Task UpsertAsync(StoredPreference preference, CancellationToken ct);

    Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct);
}
