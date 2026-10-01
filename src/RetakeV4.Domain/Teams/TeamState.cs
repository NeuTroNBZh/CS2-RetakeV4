using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public sealed record TeamRules(int MaxPlayers, double TBalanceRatio, int ScrambleAfterTWins, bool SwitchTeamsOnCtWin);

public sealed record QueuedPlayer(PlayerId Player, int Priority, long Ticket);

public sealed record TeamState(
    IReadOnlyList<PlayerId> Ct,
    IReadOnlyList<PlayerId> T,
    IReadOnlyList<QueuedPlayer> Queue,
    int TWinStreak,
    long NextTicket)
{
    public static TeamState Empty { get; } =
        new(Array.Empty<PlayerId>(), Array.Empty<PlayerId>(), Array.Empty<QueuedPlayer>(), 0, 0);

    public int PlayingCount => Ct.Count + T.Count;

    public TeamSide? SideOf(PlayerId player) =>
        Ct.Contains(player) ? TeamSide.CT : T.Contains(player) ? TeamSide.T : null;

    public bool IsQueued(PlayerId player) => Queue.Any(q => q.Player == player);

    public IReadOnlyList<QueuedPlayer> OrderedQueue() =>
        Queue.OrderByDescending(q => q.Priority).ThenBy(q => q.Ticket).ToList();

    public int? QueuePosition(PlayerId player)
    {
        var index = OrderedQueue().Select(q => q.Player).ToList().IndexOf(player);
        return index < 0 ? null : index + 1;
    }
}
