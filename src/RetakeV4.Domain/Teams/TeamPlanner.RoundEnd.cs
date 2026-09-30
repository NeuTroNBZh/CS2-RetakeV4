using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public static partial class TeamPlanner
{
    public static TeamPlan PlanRoundEnd(TeamState state, RoundWinner winner, bool scrambleRequested, TeamRules rules, IRandom random)
    {
        var effectiveWinner = winner == RoundWinner.CT && !rules.SwitchTeamsOnCtWin ? RoundWinner.None : winner;
        var streak = winner switch
        {
            RoundWinner.T => state.TWinStreak + 1,
            RoundWinner.CT => 0,
            _ => state.TWinStreak,
        };
        var scramble = scrambleRequested || (rules.ScrambleAfterTWins > 0 && streak >= rules.ScrambleAfterTWins);
        var (admitted, remainingQueue) = Admit(state, rules.MaxPlayers);
        var everyone = state.Ct.Concat(state.T).Concat(admitted).ToList();
        var (_, tCount) = TeamRatio.Compute(everyone.Count, rules.TBalanceRatio);
        var newT = scramble
            ? random.Shuffle(everyone).Take(tCount).ToList()
            : ChooseTerrorists(state, admitted, effectiveWinner, tCount, random);
        var newCt = everyone.Where(p => !newT.Contains(p)).ToList();
        var moves = BuildMoves(state, newT, newCt, scramble, effectiveWinner);
        var next = new TeamState(newCt, newT, remainingQueue, scramble ? 0 : streak, state.NextTicket);
        return new TeamPlan(next, moves, scramble);
    }

    private static (List<PlayerId> Admitted, List<QueuedPlayer> Remaining) Admit(TeamState state, int maxPlayers)
    {
        var capacity = Math.Max(0, maxPlayers - state.PlayingCount);
        var ordered = state.OrderedQueue();
        return (ordered.Take(capacity).Select(q => q.Player).ToList(), ordered.Skip(capacity).ToList());
    }

    private static List<PlayerId> ChooseTerrorists(TeamState state, IReadOnlyList<PlayerId> admitted, RoundWinner winner, int tCount, IRandom random)
    {
        var (primary, secondary) = winner == RoundWinner.CT ? (state.Ct, state.T) : (state.T, state.Ct);
        return random.Shuffle(primary)
            .Concat(random.Shuffle(secondary))
            .Concat(random.Shuffle(admitted))
            .Take(tCount)
            .ToList();
    }

    private static List<TeamMove> BuildMoves(TeamState state, List<PlayerId> newT, List<PlayerId> newCt, bool scramble, RoundWinner winner)
    {
        var targets = newT.Select(p => (Player: p, Side: TeamSide.T)).Concat(newCt.Select(p => (Player: p, Side: TeamSide.CT)));
        var moves = new List<TeamMove>();
        foreach (var (player, side) in targets)
        {
            var previous = state.SideOf(player);
            if (previous == side)
            {
                continue;
            }
            var reason = previous is null ? MoveReason.EnteredFromQueue
                : scramble ? MoveReason.Scrambled
                : winner == RoundWinner.CT ? MoveReason.SwitchedAfterCtWin
                : MoveReason.Balanced;
            moves.Add(new TeamMove(player, side, reason));
        }
        return moves;
    }
}
