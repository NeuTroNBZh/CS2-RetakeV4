using System.Collections.Concurrent;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public sealed class FakePreferenceRepository : IPreferenceRepository
{
    public bool Fail { get; set; }

    public Func<StoredPreference, bool>? FailUpsertFor { get; set; }

    public bool FailInitialize { get; set; }

    public int InitializeCalls { get; private set; }

    public bool Initialized { get; private set; }

    public int LoadCalls { get; private set; }

    public List<StoredPreference> Upserts { get; } = new();

    public List<StoredPreference> Stored { get; } = new();

    public Task InitializeAsync(CancellationToken ct)
    {
        InitializeCalls++;
        if (FailInitialize)
        {
            throw new InvalidOperationException("no such table");
        }
        Initialized = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        LoadCalls++;
        ThrowIfFailing();
        return Task.FromResult<IReadOnlyList<StoredPreference>>(Stored.Where(s => s.Key.SteamId == steamId).ToList());
    }

    public Task UpsertAsync(StoredPreference preference, CancellationToken ct)
    {
        ThrowIfFailing();
        if (FailUpsertFor?.Invoke(preference) == true)
        {
            throw new InvalidOperationException("write refused");
        }
        Upserts.Add(preference);
        return Task.CompletedTask;
    }

    public Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct)
    {
        ThrowIfFailing();
        Stored.AddRange(preferences);
        return Task.CompletedTask;
    }

    public Dictionary<ulong, string> StampValues { get; } = new();

    public int SnapshotCalls { get; private set; }

    public ConcurrentQueue<PublishedCatalog> Published { get; } = new();

    public Task<PlayerSnapshot?> SnapshotAsync(ulong steamId, CancellationToken ct)
    {
        SnapshotCalls++;
        ThrowIfFailing();
        var rows = Stored.Where(s => s.Key.SteamId == steamId).ToList();
        return Task.FromResult<PlayerSnapshot?>(new PlayerSnapshot(rows, StampValues.GetValueOrDefault(steamId, PreferenceSync.NoRows)));
    }

    public Task<IReadOnlyDictionary<ulong, string>?> StampsAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct)
    {
        ThrowIfFailing();
        IReadOnlyDictionary<ulong, string> stamps = StampValues.Where(s => steamIds.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value);
        return Task.FromResult<IReadOnlyDictionary<ulong, string>?>(stamps);
    }

    public Task<bool> PublishCatalogAsync(PublishedCatalog catalog, CancellationToken ct)
    {
        ThrowIfFailing();
        Published.Enqueue(catalog);
        return Task.FromResult(true);
    }

    private void ThrowIfFailing()
    {
        if (Fail)
        {
            throw new InvalidOperationException("database down");
        }
    }
}
