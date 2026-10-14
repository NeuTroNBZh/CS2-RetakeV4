namespace RetakeV4.Domain.Stats;

// In-memory ELO of connected players. The stored value is loaded asynchronously: rounds closed while a load is pending
// are kept as a pending delta and added to the stored value when it arrives. Each load carries a token, so that a result
// started before a disconnect (or a reconnect) never applies to the current connection.
public sealed class EloCache
{
    private readonly int _start;
    private readonly int _floor;
    private readonly Dictionary<ulong, int> _elo = new();
    private readonly HashSet<ulong> _loaded = new();
    private readonly Dictionary<ulong, long> _loading = new();
    private readonly Dictionary<ulong, int> _pendingDelta = new();
    private long _nextToken;

    public EloCache(int start, int floor)
    {
        _start = start;
        _floor = floor;
    }

    public int Current(ulong player) => _elo.GetValueOrDefault(player, _start);

    public bool IsLoaded(ulong player) => _loaded.Contains(player);

    // Null when the player is already loaded or a load is in flight.
    public long? BeginLoad(ulong player)
    {
        if (_loaded.Contains(player) || _loading.ContainsKey(player))
        {
            return null;
        }
        var token = ++_nextToken;
        _loading[player] = token;
        return token;
    }

    // False (and nothing changes) when the load is stale: the player left, or a newer load replaced it.
    public bool CompleteLoad(ulong player, long token, IReadOnlyDictionary<ulong, int> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (!IsCurrent(player, token))
        {
            return false;
        }
        _loading.Remove(player);
        if (stored.TryGetValue(player, out var value))
        {
            _elo[player] = Math.Max(_floor, value + _pendingDelta.GetValueOrDefault(player));
        }
        _loaded.Add(player);
        _pendingDelta.Remove(player);
        return true;
    }

    // A failed load can be retried later; a stale failure leaves the current load alone.
    public void CancelLoad(ulong player, long token)
    {
        if (IsCurrent(player, token))
        {
            _loading.Remove(player);
        }
    }

    // Only players still connected are updated: a leaver's delta is in the database when they come back.
    public void ApplyRound(IEnumerable<PlayerRoundStats> players, Func<ulong, bool> stillConnected)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(stillConnected);
        foreach (var player in players)
        {
            if (!stillConnected(player.SteamId))
            {
                continue;
            }
            _elo[player.SteamId] = player.EloAfter;
            if (!_loaded.Contains(player.SteamId))
            {
                _pendingDelta[player.SteamId] = _pendingDelta.GetValueOrDefault(player.SteamId) + player.EloDelta;
            }
        }
    }

    public void Forget(ulong player)
    {
        _elo.Remove(player);
        _loaded.Remove(player);
        _loading.Remove(player);
        _pendingDelta.Remove(player);
    }

    public void Clear()
    {
        _elo.Clear();
        _loaded.Clear();
        _loading.Clear();
        _pendingDelta.Clear();
    }

    private bool IsCurrent(ulong player, long token) => _loading.TryGetValue(player, out var current) && current == token;
}
