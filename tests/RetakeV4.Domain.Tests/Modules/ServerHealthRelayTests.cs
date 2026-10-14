using RetakeV4.Domain.Events;
using RetakeV4.Domain.Modules;

namespace RetakeV4.Domain.Tests.Modules;

public class ServerHealthRelayTests
{
    [Fact]
    public void Reports_ArePublished()
    {
        var bus = new EventBus(_ => { });
        var relay = new ServerHealthRelay(bus);
        var failures = new List<ModuleFailureReported>();
        var slow = new List<SlowHandlerReported>();
        bus.Subscribe<ModuleFailureReported>("test", failures.Add);
        bus.Subscribe<SlowHandlerReported>("test", slow.Add);
        relay.Failure(new GuardFailure("Spawns", "bus:X", new InvalidOperationException("boom"), true));
        relay.Slow(new SlowRun("Hud", "tick", TimeSpan.FromMilliseconds(51.6)));
        Assert.Equal(new ModuleFailureReported("Spawns", "bus:X", true), Assert.Single(failures));
        Assert.Equal(new SlowHandlerReported("Hud", "tick", 52), Assert.Single(slow));
    }

    // The Stats handler of a report runs inside the guard: when it throws, the guard reports again.
    [Fact]
    public void AFailingSubscriber_DoesNotLoop()
    {
        var bus = new EventBus(_ => { });
        var relay = new ServerHealthRelay(bus);
        var guard = new ModuleGuard(1000, relay.Failure);
        var received = 0;
        bus.Subscribe<ModuleFailureReported>("Stats", _ => guard.Run("Stats", "bus:ModuleFailureReported", () =>
        {
            received++;
            throw new InvalidOperationException("stats down");
        }));
        guard.Run("Spawns", "tick", () => throw new InvalidOperationException("boom"));
        Assert.Equal(1, received);
        // The gate is released after the report: the next one goes through.
        guard.Run("Spawns", "tick", () => throw new InvalidOperationException("boom"));
        Assert.Equal(2, received);
    }

    [Fact]
    public void ASlowSubscriber_DoesNotLoop()
    {
        var bus = new EventBus(_ => { });
        var relay = new ServerHealthRelay(bus);
        var now = TimeSpan.Zero;
        var guard = new ModuleGuard(10, _ => { }, new SlowRunReporter(TimeSpan.FromMilliseconds(50), () => now += TimeSpan.FromMilliseconds(100), relay.Slow));
        var received = 0;
        bus.Subscribe<SlowHandlerReported>("Stats", _ => guard.Run("Stats", "bus:SlowHandlerReported", () => received++));
        guard.Run("Hud", "tick", () => { });
        Assert.Equal(1, received);
    }
}
