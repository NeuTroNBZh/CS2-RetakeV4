using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public sealed class FakePreferenceRepository : IPreferenceRepository
{
    public bool Fail { get; set; }

    public int LoadCalls { get; private set; }

    public List<StoredPreference> Upserts { get; } = new();

    public List<StoredPreference> Stored { get; } = new();

    public Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        LoadCalls++;
        ThrowIfFailing();
        return Task.FromResult<IReadOnlyList<StoredPreference>>(Stored.Where(s => s.Key.SteamId == steamId).ToList());
    }

    public Task UpsertAsync(StoredPreference preference, CancellationToken ct)
    {
        ThrowIfFailing();
        Upserts.Add(preference);
        return Task.CompletedTask;
    }

    public Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct)
    {
        ThrowIfFailing();
        Stored.AddRange(preferences);
        return Task.CompletedTask;
    }

    private void ThrowIfFailing()
    {
        if (Fail)
        {
            throw new InvalidOperationException("database down");
        }
    }
}
