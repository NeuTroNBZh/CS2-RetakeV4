using System.Collections.Immutable;

namespace RetakeV4.Domain.MapVote;

public enum RtvRefusal
{
    Disabled,
    Warmup,
    NotEnoughPlayers,
    TooEarly,
    NoMaps,
}

public sealed record RtvTracker(ImmutableHashSet<int> Wanting)
{
    public static RtvTracker Empty { get; } = new(ImmutableHashSet<int>.Empty);

    public RtvTracker Want(int slot) => new(Wanting.Add(slot));

    public RtvTracker Left(int slot) => new(Wanting.Remove(slot));

    public static int Needed(int humans, int percentage) => Math.Max(1, (int)Math.Ceiling(humans * percentage / 100.0));

    public bool IsReached(int humans, int percentage) => Wanting.Count >= Needed(humans, percentage);

    // NoMaps: with fewer than two maps to vote on, accepting !rtv would announce a map change that cannot happen.
    public static RtvRefusal? Check(bool enabled, bool warmup, int humans, int minPlayers, int roundsPlayed, int minRounds, int mapsAvailable) =>
        !enabled ? RtvRefusal.Disabled
        : mapsAvailable < MapPool.MinimumMaps ? RtvRefusal.NoMaps
        : warmup ? RtvRefusal.Warmup
        : humans < minPlayers ? RtvRefusal.NotEnoughPlayers
        : roundsPlayed < minRounds ? RtvRefusal.TooEarly
        : null;
}
