using System.Collections.Immutable;
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public sealed record TeamCount(int Alive, int Playing);

// A team down to exactly one living player (a clutch) is reported once per round, and only if it had at least two players.
public sealed record LastAliveTracker(ImmutableHashSet<TeamSide> Reported)
{
    public static LastAliveTracker Empty { get; } = new(ImmutableHashSet<TeamSide>.Empty);

    public (LastAliveTracker Tracker, IReadOnlyList<TeamSide> NewlyLast) Update(TeamCount t, TeamCount ct)
    {
        var newly = new[] { (Side: TeamSide.T, Count: t), (Side: TeamSide.CT, Count: ct) }
            .Where(c => c.Count.Alive == 1 && c.Count.Playing >= 2 && !Reported.Contains(c.Side))
            .Select(c => c.Side)
            .ToList();
        return (newly.Count == 0 ? this : new LastAliveTracker(Reported.Union(newly)), newly);
    }
}
