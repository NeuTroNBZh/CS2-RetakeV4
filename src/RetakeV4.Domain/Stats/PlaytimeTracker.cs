using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;

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

    // Part of the play time spent queued with a priority above 0 (VIP); included in SecondsQueue.
    public long SecondsVip { get; init; }
}

public static class PresenceRules
{
    // A queued player is in the queue whatever their team; they are VIP when their queue priority is above 0.
    public static (Presence Presence, bool Vip) Classify(TeamState teams, PlayerId player, TeamSide? side)
    {
        ArgumentNullException.ThrowIfNull(teams);
        if (teams.Queue.FirstOrDefault(q => q.Player == player) is { } queued)
        {
            return (Presence.Queue, queued.Priority > 0);
        }
        var presence = side switch
        {
            TeamSide.CT => Presence.Ct,
            TeamSide.T => Presence.T,
            _ => Presence.Spectator,
        };
        return (presence, false);
    }
}

// Time is a monotonic clock in seconds supplied by the caller (it must not reset on map change).
public sealed class PlaytimeTracker
{
    private sealed class Entry(Presence presence, bool vip, double since)
    {
        public Presence Presence { get; set; } = presence;

        public bool Vip { get; set; } = vip;

        public double Since { get; set; } = since;

        public double[] Seconds { get; } = new double[4];

        public double VipSeconds { get; set; }
    }

    private readonly Dictionary<ulong, Entry> _players = new();

    public bool IsTracked(ulong player) => _players.ContainsKey(player);

    public void Set(ulong player, Presence presence, double nowSeconds, bool vip = false)
    {
        if (_players.TryGetValue(player, out var entry))
        {
            Accumulate(entry, nowSeconds);
            entry.Presence = presence;
            entry.Vip = vip;
            return;
        }
        _players[player] = new Entry(presence, vip, nowSeconds);
    }

    public Playtime? Close(ulong player, double nowSeconds)
    {
        if (!_players.Remove(player, out var entry))
        {
            return null;
        }
        Accumulate(entry, nowSeconds);
        return new Playtime(Round(entry.Seconds[0]), Round(entry.Seconds[1]), Round(entry.Seconds[2]), Round(entry.Seconds[3]))
        {
            SecondsVip = Round(entry.VipSeconds),
        };
    }

    public IReadOnlyList<(ulong Player, Playtime Playtime)> CloseAll(double nowSeconds) =>
        _players.Keys.ToList().Select(player => (player, Close(player, nowSeconds)!)).ToList();

    private static void Accumulate(Entry entry, double nowSeconds)
    {
        var elapsed = Math.Max(0, nowSeconds - entry.Since);
        entry.Seconds[(int)entry.Presence] += elapsed;
        if (entry.Vip)
        {
            entry.VipSeconds += elapsed;
        }
        entry.Since = nowSeconds;
    }

    private static long Round(double seconds) => (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
}
