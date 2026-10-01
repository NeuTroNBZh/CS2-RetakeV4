using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class RoundStateMachineTests
{
    [Theory]
    [InlineData(RoundPhase.Warmup, RoundSignal.RoundStarted, RoundPhase.Warmup, 0)]
    [InlineData(RoundPhase.Warmup, RoundSignal.WarmupEnded, RoundPhase.PostRound, 0)]
    [InlineData(RoundPhase.PostRound, RoundSignal.RoundStarted, RoundPhase.Preparing, 1)]
    [InlineData(RoundPhase.Preparing, RoundSignal.PreparationCompleted, RoundPhase.FreezeTime, 0)]
    [InlineData(RoundPhase.FreezeTime, RoundSignal.FreezeEnded, RoundPhase.Live, 0)]
    [InlineData(RoundPhase.Preparing, RoundSignal.FreezeEnded, RoundPhase.Live, 0)]
    [InlineData(RoundPhase.Live, RoundSignal.RoundEnded, RoundPhase.PostRound, 0)]
    [InlineData(RoundPhase.FreezeTime, RoundSignal.RoundEnded, RoundPhase.PostRound, 0)]
    [InlineData(RoundPhase.Live, RoundSignal.RoundStarted, RoundPhase.Preparing, 1)]
    [InlineData(RoundPhase.Live, RoundSignal.WarmupStarted, RoundPhase.Warmup, 0)]
    public void Apply_Transitions(RoundPhase from, RoundSignal signal, RoundPhase expectedPhase, int expectedRoundNumber)
    {
        var result = RoundStateMachine.Apply(new RoundState(from, 0), signal);
        Assert.Equal(new RoundState(expectedPhase, expectedRoundNumber), result);
    }

    [Theory]
    [InlineData(RoundPhase.Warmup, RoundSignal.FreezeEnded)]
    [InlineData(RoundPhase.Warmup, RoundSignal.RoundEnded)]
    [InlineData(RoundPhase.Warmup, RoundSignal.PreparationCompleted)]
    [InlineData(RoundPhase.Live, RoundSignal.WarmupEnded)]
    [InlineData(RoundPhase.PostRound, RoundSignal.RoundEnded)]
    [InlineData(RoundPhase.Live, RoundSignal.PreparationCompleted)]
    [InlineData(RoundPhase.Live, RoundSignal.FreezeEnded)]
    public void Apply_IgnoresIrrelevantSignals(RoundPhase from, RoundSignal signal)
    {
        var state = new RoundState(from, 4);
        Assert.Equal(state, RoundStateMachine.Apply(state, signal));
    }

    [Fact]
    public void RoundStarted_IncrementsRoundNumber()
    {
        var state = new RoundState(RoundPhase.PostRound, 7);
        Assert.Equal(8, RoundStateMachine.Apply(state, RoundSignal.RoundStarted).RoundNumber);
    }

    [Fact]
    public void Initial_IsWarmupRoundZero()
    {
        Assert.Equal(new RoundState(RoundPhase.Warmup, 0), RoundState.Initial);
    }

    [Fact]
    public void UnknownSignal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundStateMachine.Apply(RoundState.Initial, (RoundSignal)99));
    }
}
