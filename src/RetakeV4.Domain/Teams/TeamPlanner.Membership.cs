using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public enum JoinOutcome
{
    JoinedNow,
    Queued,
    AlreadyQueued,
    AlreadyPlaying,
}

public sealed record JoinResult(TeamState State, JoinOutcome Outcome, TeamSide? Side, int? QueuePosition, bool RestartRound);

public sealed record ReconcileResult(TeamState State, IReadOnlyList<TeamMove> Fixes, IReadOnlyList<PlayerId> ToSpectator);

public static partial class TeamPlanner
{
    private const int MinimumPlayersForARound = 2;

    public static JoinResult RequestJoin(TeamState state, PlayerId player, int priority, bool isWarmup, TeamSide requested, TeamRules rules)
    {
        if (state.SideOf(player) is { } side)
        {
            return new JoinResult(state, JoinOutcome.AlreadyPlaying, side, null, false);
        }
        if (state.IsQueued(player))
        {
            return new JoinResult(state, JoinOutcome.AlreadyQueued, null, state.QueuePosition(player), false);
        }
        var hasRoom = state.PlayingCount < rules.MaxPlayers;
        if (hasRoom && isWarmup)
        {
            return new JoinResult(AddTo(state, player, requested), JoinOutcome.JoinedNow, requested, null, false);
        }
        if (hasRoom && state.PlayingCount < MinimumPlayersForARound)
        {
            var needed = SideNeeded(state, rules);
            return new JoinResult(AddTo(state, player, needed), JoinOutcome.JoinedNow, needed, null, true);
        }
        var queued = Enqueue(state, player, priority);
        return new JoinResult(queued, JoinOutcome.Queued, null, queued.QueuePosition(player), false);
    }

    public static TeamState Leave(TeamState state, PlayerId player) => state with
    {
        Ct = state.Ct.Where(p => p != player).ToList(),
        T = state.T.Where(p => p != player).ToList(),
        Queue = state.Queue.Where(q => q.Player != player).ToList(),
    };

    public static TeamState Adopt(TeamState state, IReadOnlyList<(PlayerId Player, TeamSide Side)> onTeams, TeamRules rules)
    {
        var result = state;
        foreach (var (player, side) in onTeams)
        {
            if (result.SideOf(player) is not null || result.IsQueued(player))
            {
                continue;
            }
            result = result.PlayingCount < rules.MaxPlayers ? AddTo(result, player, side) : Enqueue(result, player, 0);
        }
        return result;
    }

    public static ReconcileResult Reconcile(TeamState state, IReadOnlyDictionary<PlayerId, TeamSide?> actual, Func<PlayerId, int> priorityOf)
    {
        var expected = state.Ct.Select(p => (Player: p, Side: TeamSide.CT)).Concat(state.T.Select(p => (Player: p, Side: TeamSide.T))).ToList();
        var result = expected
            .Where(e => actual.TryGetValue(e.Player, out var side) && side is null)
            .Aggregate(state, (current, e) => Leave(current, e.Player));
        var fixes = expected
            .Where(e => actual.TryGetValue(e.Player, out var side) && side is not null && side != e.Side)
            .Select(e => new TeamMove(e.Player, e.Side, MoveReason.Balanced))
            .ToList();
        var intruders = actual
            .Where(a => a.Value is not null && state.SideOf(a.Key) is null)
            .Select(a => a.Key)
            .ToList();
        result = intruders
            .Where(p => !result.IsQueued(p))
            .Aggregate(result, (current, p) => Enqueue(current, p, priorityOf(p)));
        return new ReconcileResult(result, fixes, intruders);
    }

    private static TeamSide SideNeeded(TeamState state, TeamRules rules)
    {
        var (_, t) = TeamRatio.Compute(state.PlayingCount + 1, rules.TBalanceRatio);
        return state.T.Count < t ? TeamSide.T : TeamSide.CT;
    }

    private static TeamState AddTo(TeamState state, PlayerId player, TeamSide side) => side == TeamSide.T
        ? state with { T = state.T.Append(player).ToList() }
        : state with { Ct = state.Ct.Append(player).ToList() };

    private static TeamState Enqueue(TeamState state, PlayerId player, int priority) => state with
    {
        Queue = state.Queue.Append(new QueuedPlayer(player, priority, state.NextTicket)).ToList(),
        NextTicket = state.NextTicket + 1,
    };
}
