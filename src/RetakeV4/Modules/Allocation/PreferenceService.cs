using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Persistence;

namespace RetakeV4.Modules.Allocation;

// Game-thread state: every mutation of _book happens on the game thread (directly or via onGameThread).
public sealed class PreferenceService : IAsyncDisposable
{
    private readonly IPreferenceRepository _store;
    private readonly ILogger _logger;
    private readonly PreferenceWriteQueue _writes;
    private PreferenceBook _book = PreferenceBook.Empty;

    public PreferenceService(IPreferenceRepository store, ILogger logger)
    {
        _store = store;
        _logger = logger;
        _writes = new PreferenceWriteQueue(store, logger);
    }

    public void PlayerConnected(ulong steamId, Action<Action> onGameThread)
    {
        var (book, token) = _book.StartSession(steamId);
        _book = book;
        _ = Task.Run(async () =>
        {
            try
            {
                var stored = await _store.LoadAsync(steamId, CancellationToken.None).ConfigureAwait(false);
                onGameThread(() => _book = _book.WithPlayer(steamId, token, stored));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not apply loaded preferences for {SteamId}", steamId);
            }
        });
    }

    public void PlayerDisconnected(ulong steamId) => _book = _book.WithoutPlayer(steamId);

    public LoadoutPreference? RequestFor(ulong steamId, TeamSide team, string roundType) =>
        _book.RequestFor(steamId, team, roundType);

    public bool ToggleAwp(ulong steamId)
    {
        var (book, changes, optIn) = _book.ToggleAwp(steamId);
        _book = book;
        foreach (var change in changes)
        {
            _writes.Enqueue(change);
        }
        return optIn;
    }

    public async Task<int> ImportV3Async(string databaseFile, CancellationToken ct)
    {
        var rows = await V3SqliteReader.ReadAsync(databaseFile, ct).ConfigureAwait(false);
        var preferences = V3PreferenceImport.Convert(rows);
        await _store.ImportAsync(preferences, ct).ConfigureAwait(false);
        _logger.LogInformation("Imported {Count} V3 preference(s) from {File}", preferences.Count, databaseFile);
        return preferences.Count;
    }

    public ValueTask DisposeAsync() => _writes.DisposeAsync();
}
