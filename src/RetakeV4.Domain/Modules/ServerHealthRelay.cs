using RetakeV4.Domain.Events;

namespace RetakeV4.Domain.Modules;

// Republishes ModuleGuard reports on the bus. A subscriber that fails or runs slowly while handling a report makes the
// guard report again: reports raised during a publication are dropped, so this never loops. Game thread only.
public sealed class ServerHealthRelay
{
    private readonly IEventBus _bus;
    private bool _publishing;

    public ServerHealthRelay(IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
    }

    public void Failure(GuardFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Publish(new ModuleFailureReported(failure.Module, failure.Stage, failure.ModuleDisabled));
    }

    public void Slow(SlowRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        Publish(new SlowHandlerReported(run.Module, run.Stage, (int)Math.Round(run.Elapsed.TotalMilliseconds)));
    }

    private void Publish<T>(T report) where T : notnull
    {
        if (_publishing)
        {
            return;
        }
        _publishing = true;
        try
        {
            _bus.Publish(report);
        }
        finally
        {
            _publishing = false;
        }
    }
}
