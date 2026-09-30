using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class NoOpPreferenceRepository : IPreferenceRepository
{
    public Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredPreference>>(Array.Empty<StoredPreference>());

    public Task UpsertAsync(StoredPreference preference, CancellationToken ct) => Task.CompletedTask;

    public Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct) => Task.CompletedTask;
}
