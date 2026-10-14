namespace RetakeV4.Domain.Stats;

// Network activity of one session. Ping is null when no sample was taken.
public sealed record SessionActivity(int? PingAvg, int? PingMax)
{
    public static SessionActivity None { get; } = new(null, null);
}

// Per-session ping samples, on the game thread. A session starts with Start and ends with Close.
public sealed class SessionActivityTracker
{
    public const int MaxPing = 999;

    private sealed class Entry
    {
        public long PingSum { get; set; }

        public int PingSamples { get; set; }

        public int PingMax { get; set; }
    }

    private readonly Dictionary<ulong, Entry> _players = new();

    public void Start(ulong player) => _players[player] = new Entry();

    public void AddPing(ulong player, int pingMs)
    {
        if (pingMs < 0 || !_players.TryGetValue(player, out var entry))
        {
            return;
        }
        var ping = Math.Min(pingMs, MaxPing);
        entry.PingSum += ping;
        entry.PingSamples++;
        entry.PingMax = Math.Max(entry.PingMax, ping);
    }

    public SessionActivity Close(ulong player)
    {
        if (!_players.Remove(player, out var entry) || entry.PingSamples == 0)
        {
            return SessionActivity.None;
        }
        var average = (int)Math.Round((double)entry.PingSum / entry.PingSamples, MidpointRounding.AwayFromZero);
        return new SessionActivity(average, entry.PingMax);
    }
}
