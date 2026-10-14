namespace RetakeV4.Domain.Stats;

public enum Presence
{
    Ct,
    T,
    Spectator,
    Queue,
}

public sealed record Playtime(long SecondsCt, long SecondsT, long SecondsSpectator, long SecondsQueue)
{
    public long Total => SecondsCt + SecondsT + SecondsSpectator + SecondsQueue;
}

// Time is a monotonic clock in seconds supplied by the caller (it must not reset on map change).
public sealed class PlaytimeTracker
{
    private sealed class Entry(Presence presence, double since)
    {
        public Presence Presence { get; set; } = presence;

        public double Since { get; set; } = since;

        public double[] Seconds { get; } = new double[4];
    }

    private readonly Dictionary<ulong, Entry> _players = new();
    private readonly HashSet<ulong> _left = new();

    public bool IsTracked(ulong player) => _players.ContainsKey(player);

    // The game can still list a player whose session closed on disconnect (same frame, or until the slot is reused):
    // only a new connection opens a session for that player again.
    public bool HasLeft(ulong player) => _left.Contains(player);

    public void Set(ulong player, Presence presence, double nowSeconds)
    {
        if (_players.TryGetValue(player, out var entry))
        {
            Accumulate(entry, nowSeconds);
            entry.Presence = presence;
            return;
        }
        _players[player] = new Entry(presence, nowSeconds);
        _left.Remove(player);
    }

    // A player who disconnected: the session ends and the player is marked as left.
    public Playtime? Close(ulong player, double nowSeconds)
    {
        _left.Add(player);
        return End(player, nowSeconds);
    }

    // Unload: every session ends, nobody is marked as left (a hot reload adopts the players still on the server).
    public IReadOnlyList<(ulong Player, Playtime Playtime)> CloseAll(double nowSeconds) =>
        _players.Keys.ToList().Select(player => (player, End(player, nowSeconds)!)).ToList();

    private Playtime? End(ulong player, double nowSeconds)
    {
        if (!_players.Remove(player, out var entry))
        {
            return null;
        }
        Accumulate(entry, nowSeconds);
        return new Playtime(Round(entry.Seconds[0]), Round(entry.Seconds[1]), Round(entry.Seconds[2]), Round(entry.Seconds[3]));
    }

    private static void Accumulate(Entry entry, double nowSeconds)
    {
        entry.Seconds[(int)entry.Presence] += Math.Max(0, nowSeconds - entry.Since);
        entry.Since = nowSeconds;
    }

    private static long Round(double seconds) => (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
}
