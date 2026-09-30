namespace RetakeV4.Domain.Rounds;

public static class RoundStateMachine
{
    public static RoundState Apply(RoundState state, RoundSignal signal) => signal switch
    {
        RoundSignal.WarmupStarted => state with { Phase = RoundPhase.Warmup },
        RoundSignal.WarmupEnded => state.Phase == RoundPhase.Warmup ? state with { Phase = RoundPhase.PostRound } : state,
        RoundSignal.RoundStarted => state.Phase == RoundPhase.Warmup
            ? state
            : new RoundState(RoundPhase.Preparing, state.RoundNumber + 1),
        RoundSignal.PreparationCompleted => state.Phase == RoundPhase.Preparing ? state with { Phase = RoundPhase.FreezeTime } : state,
        RoundSignal.FreezeEnded => state.Phase is RoundPhase.Preparing or RoundPhase.FreezeTime
            ? state with { Phase = RoundPhase.Live }
            : state,
        RoundSignal.RoundEnded => state.Phase is RoundPhase.Preparing or RoundPhase.FreezeTime or RoundPhase.Live
            ? state with { Phase = RoundPhase.PostRound }
            : state,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "Unknown round signal"),
    };
}
