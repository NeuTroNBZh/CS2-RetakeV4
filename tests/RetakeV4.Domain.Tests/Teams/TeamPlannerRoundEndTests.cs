using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Teams;

public class TeamPlannerRoundEndTests
{
    private static readonly TeamRules Rules = new(MaxPlayers: 9, TBalanceRatio: 0.499, ScrambleAfterTWins: 5, SwitchTeamsOnCtWin: true);
    private static readonly IRandom Random = new SystemRandom(new Random(11));

    private static PlayerId[] Ids(params int[] slots) => slots.Select(s => new PlayerId(s)).ToArray();

    private static TeamState State(int[] ct, int[] t, params QueuedPlayer[] queue) =>
        TeamState.Empty with { Ct = Ids(ct), T = Ids(t), Queue = queue, NextTicket = queue.Length };

    [Fact]
    public void CtWin_WinnersAttack_LosersDefend()
    {
        var state = State(new[] { 1, 2, 3, 4, 5 }, new[] { 6, 7, 8, 9 });
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.CT, false, Rules, Random);
        Assert.Equal(4, plan.State.T.Count);
        Assert.Equal(5, plan.State.Ct.Count);
        Assert.All(plan.State.T, p => Assert.Contains(p, state.Ct));
        Assert.All(Ids(6, 7, 8, 9), p => Assert.Contains(p, plan.State.Ct));
        Assert.All(plan.Moves, m => Assert.Equal(MoveReason.SwitchedAfterCtWin, m.Reason));
        Assert.Equal(8, plan.Moves.Count);
    }

    [Fact]
    public void TWin_KeepsTeams_AndIncrementsStreak()
    {
        var state = State(new[] { 1, 2, 3 }, new[] { 4, 5 }) with { TWinStreak = 2 };
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules, Random);
        Assert.Empty(plan.Moves);
        Assert.Equal(3, plan.State.TWinStreak);
        Assert.False(plan.Scrambled);
    }

    [Fact]
    public void QueuedPlayers_EnterAsCt_AndTeamsAreRebalanced()
    {
        var state = State(new[] { 1, 2, 3, 4 }, new[] { 5, 6, 7 }, new QueuedPlayer(new PlayerId(8), 0, 0), new QueuedPlayer(new PlayerId(9), 0, 1));
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules, Random);
        Assert.Equal(4, plan.State.T.Count);
        Assert.Equal(5, plan.State.Ct.Count);
        Assert.Contains(new PlayerId(8), plan.State.Ct);
        Assert.Contains(new PlayerId(9), plan.State.Ct);
        Assert.Empty(plan.State.Queue);
        Assert.Contains(plan.Moves, m => m.Player == new PlayerId(8) && m.Reason == MoveReason.EnteredFromQueue);
        var promoted = Assert.Single(plan.Moves, m => m.Reason == MoveReason.Balanced);
        Assert.Contains(promoted.Player, state.Ct);
        Assert.Equal(TeamSide.T, promoted.To);
    }

    [Fact]
    public void FullServer_KeepsQueue()
    {
        var state = State(new[] { 1, 2, 3, 4, 5 }, new[] { 6, 7, 8, 9 }, new QueuedPlayer(new PlayerId(10), 0, 0));
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules, Random);
        Assert.Equal(new PlayerId(10), Assert.Single(plan.State.Queue).Player);
        Assert.Equal(9, plan.State.PlayingCount);
    }

    [Fact]
    public void Queue_AdmitsHigherPriorityFirst()
    {
        var state = State(new[] { 1, 2, 3, 4 }, new[] { 5, 6, 7, 8 },
            new QueuedPlayer(new PlayerId(10), 0, 0), new QueuedPlayer(new PlayerId(11), 1, 1));
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules, Random);
        Assert.Contains(new PlayerId(11), plan.State.Ct.Concat(plan.State.T));
        Assert.Equal(new PlayerId(10), Assert.Single(plan.State.Queue).Player);
    }

    [Fact]
    public void TWinStreak_TriggersScramble_AndResetsStreak()
    {
        var state = State(new[] { 1, 2, 3 }, new[] { 4, 5 }) with { TWinStreak = 4 };
        var plan = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules, Random);
        Assert.True(plan.Scrambled);
        Assert.Equal(0, plan.State.TWinStreak);
        Assert.Equal(2, plan.State.T.Count);
        Assert.All(plan.Moves, m => Assert.Equal(MoveReason.Scrambled, m.Reason));
    }

    [Fact]
    public void RequestedScramble_IsApplied()
    {
        var plan = TeamPlanner.PlanRoundEnd(State(new[] { 1, 2 }, new[] { 3 }), RoundWinner.None, true, Rules, Random);
        Assert.True(plan.Scrambled);
    }

    [Fact]
    public void CtWin_ResetsStreak()
    {
        var state = State(new[] { 1, 2 }, new[] { 3 }) with { TWinStreak = 3 };
        Assert.Equal(0, TeamPlanner.PlanRoundEnd(state, RoundWinner.CT, false, Rules, Random).State.TWinStreak);
    }

    [Fact]
    public void CtWin_WithSwitchDisabled_KeepsTeams()
    {
        var plan = TeamPlanner.PlanRoundEnd(State(new[] { 1, 2 }, new[] { 3 }), RoundWinner.CT, false, Rules with { SwitchTeamsOnCtWin = false }, Random);
        Assert.Empty(plan.Moves);
    }

    [Fact]
    public void ScrambleThresholdZero_NeverScramblesAutomatically()
    {
        var state = State(new[] { 1, 2 }, new[] { 3 }) with { TWinStreak = 50 };
        Assert.False(TeamPlanner.PlanRoundEnd(state, RoundWinner.T, false, Rules with { ScrambleAfterTWins = 0 }, Random).Scrambled);
    }

    [Fact]
    public void EmptyServer_ProducesEmptyPlan()
    {
        var plan = TeamPlanner.PlanRoundEnd(TeamState.Empty, RoundWinner.None, false, Rules, Random);
        Assert.Empty(plan.Moves);
        Assert.Equal(0, plan.State.PlayingCount);
    }

    [Fact]
    public void QueuePosition_FollowsPriorityThenTicket()
    {
        var state = State(new[] { 1 }, new[] { 2 },
            new QueuedPlayer(new PlayerId(10), 0, 0), new QueuedPlayer(new PlayerId(11), 2, 1), new QueuedPlayer(new PlayerId(12), 0, 2));
        Assert.Equal(1, state.QueuePosition(new PlayerId(11)));
        Assert.Equal(2, state.QueuePosition(new PlayerId(10)));
        Assert.Equal(3, state.QueuePosition(new PlayerId(12)));
        Assert.Null(state.QueuePosition(new PlayerId(1)));
    }
}
