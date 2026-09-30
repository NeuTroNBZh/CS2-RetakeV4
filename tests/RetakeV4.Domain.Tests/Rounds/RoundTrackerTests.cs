using RetakeV4.Domain.Events;
using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class RoundTrackerTests
{
    private readonly EventBus _bus = new(_ => { });
    private readonly List<object> _events = new();
    private readonly PreparationPipeline _pipeline = new(new ModuleGuard(5, _ => { }));

    public RoundTrackerTests()
    {
        _bus.Subscribe<RoundPhaseChanged>("test", _events.Add);
        _bus.Subscribe<RoundPrepared>("test", _events.Add);
    }

    [Fact]
    public void RoundStart_PreparesThenEntersFreezeTime()
    {
        var tracker = new RoundTracker(_bus, _pipeline, new RoundState(RoundPhase.PostRound, 3));
        tracker.Handle(RoundSignal.RoundStarted);
        Assert.Equal(new RoundState(RoundPhase.FreezeTime, 4), tracker.State);
        Assert.Equal(new object[]
        {
            new RoundPhaseChanged(RoundPhase.PostRound, RoundPhase.Preparing, 4),
            new RoundPrepared(new PreparationContext(4)),
            new RoundPhaseChanged(RoundPhase.Preparing, RoundPhase.FreezeTime, 4),
        }, _events);
    }

    [Fact]
    public void IgnoredSignal_PublishesNothing()
    {
        var tracker = new RoundTracker(_bus, _pipeline, RoundState.Initial);
        tracker.Handle(RoundSignal.FreezeEnded);
        Assert.Empty(_events);
        Assert.Equal(RoundState.Initial, tracker.State);
    }

    [Fact]
    public void RoundStartedDuringLive_StartsNewPreparation()
    {
        var tracker = new RoundTracker(_bus, _pipeline, new RoundState(RoundPhase.Live, 5));
        tracker.Handle(RoundSignal.RoundStarted);
        Assert.Equal(new RoundState(RoundPhase.FreezeTime, 6), tracker.State);
        Assert.Single(_events.OfType<RoundPrepared>());
    }

    [Fact]
    public void FullRound_EndsInPostRound()
    {
        var tracker = new RoundTracker(_bus, _pipeline, new RoundState(RoundPhase.PostRound, 0));
        tracker.Handle(RoundSignal.RoundStarted);
        tracker.Handle(RoundSignal.FreezeEnded);
        tracker.Handle(RoundSignal.RoundEnded);
        Assert.Equal(new RoundState(RoundPhase.PostRound, 1), tracker.State);
    }

    [Fact]
    public void Reset_ReplacesStateWithoutPublishing()
    {
        var tracker = new RoundTracker(_bus, _pipeline, new RoundState(RoundPhase.Live, 9));
        tracker.Reset(RoundState.Initial);
        Assert.Equal(RoundState.Initial, tracker.State);
        Assert.Empty(_events);
    }

    [Fact]
    public void Prepare_PassesRoundsPlayedToThePipeline()
    {
        PreparationContext? seen = null;
        _pipeline.Register("Probe", new DelegatePreparationStep("probe", 1, c =>
        {
            seen = c;
            return c;
        }));
        var tracker = new RoundTracker(_bus, _pipeline, new RoundState(RoundPhase.PostRound, 0), () => 7);
        tracker.Handle(RoundSignal.RoundStarted);
        Assert.Equal(7, seen?.RoundsPlayed);
    }

    [Fact]
    public void InitialStateFor_Warmup_IsWarmup() =>
        Assert.Equal(RoundState.Initial, RoundTracker.InitialStateFor(isWarmup: true));

    [Fact]
    public void InitialStateFor_NotWarmup_IsPostRound() =>
        Assert.Equal(new RoundState(RoundPhase.PostRound, 0), RoundTracker.InitialStateFor(isWarmup: false));
}
