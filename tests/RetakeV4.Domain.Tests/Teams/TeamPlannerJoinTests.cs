using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Teams;

public class TeamPlannerJoinTests
{
    private static readonly TeamRules Rules = new(MaxPlayers: 4, TBalanceRatio: 0.499, ScrambleAfterTWins: 5, SwitchTeamsOnCtWin: true);

    private static PlayerId P(int slot) => new(slot);

    private static TeamState State(int[] ct, int[] t) =>
        TeamState.Empty with { Ct = ct.Select(P).ToArray(), T = t.Select(P).ToArray() };

    [Fact]
    public void Warmup_JoinsRequestedSideImmediately()
    {
        var result = TeamPlanner.RequestJoin(State(new[] { 1 }, new[] { 2 }), P(3), 0, isWarmup: true, TeamSide.T, Rules);
        Assert.Equal(JoinOutcome.JoinedNow, result.Outcome);
        Assert.Equal(TeamSide.T, result.Side);
        Assert.Contains(P(3), result.State.T);
        Assert.False(result.RestartRound);
    }

    [Fact]
    public void Warmup_WhenFull_Queues()
    {
        var result = TeamPlanner.RequestJoin(State(new[] { 1, 2 }, new[] { 3, 4 }), P(5), 0, isWarmup: true, TeamSide.CT, Rules);
        Assert.Equal(JoinOutcome.Queued, result.Outcome);
        Assert.Equal(1, result.QueuePosition);
    }

    [Fact]
    public void Live_WithEnoughPlayers_Queues()
    {
        var result = TeamPlanner.RequestJoin(State(new[] { 1 }, new[] { 2 }), P(3), 1, isWarmup: false, TeamSide.CT, Rules);
        Assert.Equal(JoinOutcome.Queued, result.Outcome);
        Assert.Equal(1, result.State.NextTicket);
        Assert.Equal(1, Assert.Single(result.State.Queue).Priority);
    }

    [Fact]
    public void Live_WithFewerThanTwoPlayers_JoinsMissingSideAndRestarts()
    {
        var result = TeamPlanner.RequestJoin(State(new[] { 1 }, Array.Empty<int>()), P(2), 0, isWarmup: false, TeamSide.CT, Rules);
        Assert.Equal(JoinOutcome.JoinedNow, result.Outcome);
        Assert.Equal(TeamSide.T, result.Side);
        Assert.True(result.RestartRound);
    }

    [Fact]
    public void AlreadyPlaying_IsReported()
    {
        var result = TeamPlanner.RequestJoin(State(new[] { 1 }, new[] { 2 }), P(2), 0, isWarmup: false, TeamSide.CT, Rules);
        Assert.Equal(JoinOutcome.AlreadyPlaying, result.Outcome);
        Assert.Equal(TeamSide.T, result.Side);
    }

    [Fact]
    public void AlreadyQueued_IsIdempotent()
    {
        var queued = TeamPlanner.RequestJoin(State(new[] { 1 }, new[] { 2 }), P(3), 0, false, TeamSide.CT, Rules).State;
        var again = TeamPlanner.RequestJoin(queued, P(3), 0, false, TeamSide.T, Rules);
        Assert.Equal(JoinOutcome.AlreadyQueued, again.Outcome);
        Assert.Single(again.State.Queue);
    }

    [Fact]
    public void Leave_RemovesPlayingPlayer()
    {
        var state = TeamPlanner.Leave(State(new[] { 1, 2 }, new[] { 3 }), P(2));
        Assert.DoesNotContain(P(2), state.Ct);
        Assert.Equal(2, state.PlayingCount);
    }

    [Fact]
    public void Leave_RemovesQueuedPlayer_AndShiftsPositions()
    {
        var state = State(new[] { 1 }, new[] { 2 });
        state = TeamPlanner.RequestJoin(state, P(3), 0, false, TeamSide.CT, Rules).State;
        state = TeamPlanner.RequestJoin(state, P(4), 0, false, TeamSide.CT, Rules).State;
        state = TeamPlanner.Leave(state, P(3));
        Assert.Equal(1, state.QueuePosition(P(4)));
    }

    [Fact]
    public void Leave_UnknownPlayer_ChangesNothing()
    {
        var state = State(new[] { 1 }, new[] { 2 });
        Assert.Equal(2, TeamPlanner.Leave(state, P(9)).PlayingCount);
    }

    [Fact]
    public void Adopt_AddsUnknownHumansOnTheirCurrentSide()
    {
        var state = TeamPlanner.Adopt(TeamState.Empty, new[] { (P(1), TeamSide.CT), (P(2), TeamSide.T) }, Rules);
        Assert.Equal(new[] { P(1) }, state.Ct);
        Assert.Equal(new[] { P(2) }, state.T);
    }

    [Fact]
    public void Adopt_QueuesBeyondMaxPlayers_AndIgnoresKnownPlayers()
    {
        var start = State(new[] { 1, 2 }, new[] { 3 });
        var state = TeamPlanner.Adopt(start, new[] { (P(1), TeamSide.T), (P(4), TeamSide.T), (P(5), TeamSide.CT) }, Rules);
        Assert.Equal(4, state.PlayingCount);
        Assert.Contains(P(1), state.Ct);
        Assert.Equal(P(5), Assert.Single(state.Queue).Player);
    }

    [Fact]
    public void Reconcile_FixesWrongSides_AndSpectatesIntruders()
    {
        var state = State(new[] { 1 }, new[] { 2 });
        var actual = new Dictionary<PlayerId, TeamSide?> { [P(1)] = TeamSide.T, [P(2)] = TeamSide.T, [P(3)] = TeamSide.CT, [P(4)] = null };
        var result = TeamPlanner.Reconcile(state, actual, _ => 0);
        var fix = Assert.Single(result.Fixes);
        Assert.Equal(new TeamMove(P(1), TeamSide.CT, MoveReason.Balanced), fix);
        Assert.Equal(new[] { P(3) }, result.ToSpectator);
    }

    [Fact]
    public void Reconcile_IgnoresPlayersMissingFromActual()
    {
        var result = TeamPlanner.Reconcile(State(new[] { 1 }, new[] { 2 }), new Dictionary<PlayerId, TeamSide?>(), _ => 0);
        Assert.Empty(result.Fixes);
        Assert.Empty(result.ToSpectator);
        Assert.Equal(2, result.State.PlayingCount);
    }

    [Fact]
    public void Reconcile_SpectatingExpectedPlayer_LeavesInsteadOfBeingFixed()
    {
        var actual = new Dictionary<PlayerId, TeamSide?> { [P(1)] = null, [P(2)] = TeamSide.T };
        var result = TeamPlanner.Reconcile(State(new[] { 1 }, new[] { 2 }), actual, _ => 0);
        Assert.Empty(result.Fixes);
        Assert.Null(result.State.SideOf(P(1)));
        Assert.False(result.State.IsQueued(P(1)));
    }

    [Fact]
    public void Reconcile_QueuesIntruders_WithTheirPriority()
    {
        var actual = new Dictionary<PlayerId, TeamSide?> { [P(1)] = TeamSide.CT, [P(2)] = TeamSide.T, [P(3)] = TeamSide.CT };
        var result = TeamPlanner.Reconcile(State(new[] { 1 }, new[] { 2 }), actual, p => p == P(3) ? 2 : 0);
        var queued = Assert.Single(result.State.Queue);
        Assert.Equal(P(3), queued.Player);
        Assert.Equal(2, queued.Priority);
        Assert.Equal(new[] { P(3) }, result.ToSpectator);
    }

    [Fact]
    public void Reconcile_AlreadyQueuedIntruder_IsNotQueuedTwice()
    {
        var state = TeamPlanner.RequestJoin(State(new[] { 1 }, new[] { 2 }), P(3), 0, false, TeamSide.CT, Rules).State;
        var actual = new Dictionary<PlayerId, TeamSide?> { [P(1)] = TeamSide.CT, [P(2)] = TeamSide.T, [P(3)] = TeamSide.T };
        var result = TeamPlanner.Reconcile(state, actual, _ => 0);
        Assert.Single(result.State.Queue);
        Assert.Equal(new[] { P(3) }, result.ToSpectator);
    }
}
