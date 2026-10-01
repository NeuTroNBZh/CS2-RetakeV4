using RetakeV4.Domain.Events;

namespace RetakeV4.Domain.Rounds;

public sealed class RoundTracker
{
    private readonly IEventBus _bus;
    private readonly PreparationPipeline _pipeline;

    public RoundTracker(IEventBus bus, PreparationPipeline pipeline, RoundState initial)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(initial);
        _bus = bus;
        _pipeline = pipeline;
        State = initial;
    }

    public RoundState State { get; private set; }

    public static RoundState InitialStateFor(bool isWarmup) =>
        isWarmup ? RoundState.Initial : new RoundState(RoundPhase.PostRound, 0);

    public void Reset(RoundState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
    }

    public void Handle(RoundSignal signal)
    {
        var next = RoundStateMachine.Apply(State, signal);
        if (next == State)
        {
            return;
        }
        Transition(next);
        if (next.Phase == RoundPhase.Preparing)
        {
            Prepare(next.RoundNumber);
        }
    }

    private void Prepare(int roundNumber)
    {
        var context = _pipeline.Execute(new PreparationContext(roundNumber));
        _bus.Publish(new RoundPrepared(context));
        Transition(RoundStateMachine.Apply(State, RoundSignal.PreparationCompleted));
    }

    private void Transition(RoundState next)
    {
        var previous = State;
        State = next;
        _bus.Publish(new RoundPhaseChanged(previous.Phase, next.Phase, next.RoundNumber));
    }
}
