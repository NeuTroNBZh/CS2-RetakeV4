using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Modules.Allocation;

// Game-thread state: every mutation of _book and _sync happens on the game thread (directly or via _onGameThread).
public sealed class PreferenceService : IAsyncDisposable
{
    private readonly IPreferenceRepository _store;
    private readonly ILogger _logger;
    private readonly Action<Action> _onGameThread;
    private readonly PreferenceWriteQueue _writes;
    private PreferenceBook _book = PreferenceBook.Empty;
    private PreferenceSync _sync = PreferenceSync.Empty;

    public PreferenceService(IPreferenceRepository store, ILogger logger, Action<Action> onGameThread)
    {
        _store = store;
        _logger = logger;
        _onGameThread = onGameThread;
        _writes = new PreferenceWriteQueue(store, logger);
    }

    public void PlayerConnected(ulong steamId)
    {
        var (book, token) = _book.StartSession(steamId);
        _book = book;
        InBackground("load", steamId, async () =>
        {
            var snapshot = await _store.SnapshotAsync(steamId, CancellationToken.None).ConfigureAwait(false);
            if (snapshot is null)
            {
                return;
            }
            _onGameThread(() =>
            {
                _book = _book.WithPlayer(steamId, token, snapshot.Preferences);
                if (_book.Sessions.GetValueOrDefault(steamId) == token)
                {
                    _sync = _sync.Stamped(steamId, snapshot.Stamp);
                }
            });
        });
    }

    public void PlayerDisconnected(ulong steamId)
    {
        _book = _book.WithoutPlayer(steamId);
        _sync = _sync.Forget(steamId);
    }

    public LoadoutPreference? RequestFor(ulong steamId, TeamSide team, string roundType) =>
        _book.RequestFor(steamId, team, roundType);

    public bool IsAwpVolunteer(ulong steamId) => _book.IsAwpVolunteer(steamId);

    public void SetWeapon(ulong steamId, TeamSide team, string roundType, WeaponSlot slot, string weapon)
    {
        var (book, change) = _book.SetWeapon(steamId, team, roundType, slot, weapon);
        _book = book;
        Save(change);
    }

    public bool ToggleAwp(ulong steamId)
    {
        var (book, changes, optIn) = _book.ToggleAwp(steamId);
        _book = book;
        foreach (var change in changes)
        {
            Save(change);
        }
        return optIn;
    }

    // Preferences edited on the web panel apply from the next round, without reconnecting.
    public void CheckForExternalChanges()
    {
        var connected = _book.Sessions.Keys.ToList();
        if (connected.Count == 0)
        {
            return;
        }
        InBackground("check", 0, async () =>
        {
            var current = await _store.StampsAsync(connected, CancellationToken.None).ConfigureAwait(false);
            if (current is not null)
            {
                _onGameThread(() => ReloadChanged(connected, current));
            }
        });
    }

    public void PublishCatalog(string serverKey, string json) =>
        InBackground("publish catalog", 0, () =>
            _store.PublishCatalogAsync(new PublishedCatalog(serverKey, CatalogExport.FormatVersion, json), CancellationToken.None));

    public async Task<int> ImportV3Async(string databaseFile, CancellationToken ct)
    {
        var rows = await V3SqliteReader.ReadAsync(databaseFile, ct).ConfigureAwait(false);
        var preferences = V3PreferenceImport.Convert(rows);
        await _store.ImportAsync(preferences, ct).ConfigureAwait(false);
        _logger.LogInformation("Imported {Count} V3 preference(s) from {File}", preferences.Count, databaseFile);
        return preferences.Count;
    }

    public ValueTask DisposeAsync() => _writes.DisposeAsync();

    private void Save(StoredPreference change)
    {
        var steamId = change.Key.SteamId;
        _sync = _sync.Edited(steamId);
        _writes.Enqueue(change, () => _onGameThread(() => _sync = _sync.Flushed(steamId)));
    }

    private void ReloadChanged(IReadOnlyCollection<ulong> connected, IReadOnlyDictionary<ulong, string> current)
    {
        foreach (var steamId in _sync.Changed(connected.Where(_book.Sessions.ContainsKey).ToList(), current))
        {
            var edit = _sync.EditOf(steamId);
            InBackground("reload", steamId, async () =>
            {
                var snapshot = await _store.SnapshotAsync(steamId, CancellationToken.None).ConfigureAwait(false);
                if (snapshot is not null)
                {
                    _onGameThread(() => ApplyReload(steamId, edit, snapshot));
                }
            });
        }
    }

    private void ApplyReload(ulong steamId, long edit, PlayerSnapshot snapshot)
    {
        if (!_book.Sessions.ContainsKey(steamId) || !_sync.CanApply(steamId, edit))
        {
            return;
        }
        _book = _book.Replace(steamId, snapshot.Preferences);
        _sync = _sync.Stamped(steamId, snapshot.Stamp);
    }

    private void InBackground(string operation, ulong steamId, Func<Task> work) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Preference {Operation} failed for {SteamId}", operation, steamId);
            }
        });
}
