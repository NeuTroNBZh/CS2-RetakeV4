using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public static class QueueChanges
{
    public static IReadOnlyList<(PlayerId Player, int Position)> NewlyQueued(TeamState before, TeamState after) =>
        after.OrderedQueue()
            .Select((queued, index) => (queued.Player, Position: index + 1))
            .Where(entry => !before.IsQueued(entry.Player))
            .ToList();
}
