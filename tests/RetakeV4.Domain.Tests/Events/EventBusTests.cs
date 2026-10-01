using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Tests.Events;

public class EventBusTests
{
    private record Ping(int Value);
    private record DerivedPing(int Value) : Ping(Value);

    private readonly List<BusError> _errors = new();
    private readonly EventBus _bus;

    public EventBusTests() => _bus = new EventBus(_errors.Add);

    [Fact]
    public void Publish_InvokesSubscribersInSubscriptionOrder()
    {
        var calls = new List<string>();
        _bus.Subscribe<Ping>("a", _ => calls.Add("a"));
        _bus.Subscribe<Ping>("b", _ => calls.Add("b"));
        _bus.Publish(new Ping(1));
        Assert.Equal(new[] { "a", "b" }, calls);
    }

    [Fact]
    public void ThrowingSubscriber_IsIsolatedAndReported()
    {
        var received = 0;
        _bus.Subscribe<Ping>("broken", _ => throw new InvalidOperationException("boom"));
        _bus.Subscribe<Ping>("healthy", _ => received++);
        _bus.Publish(new Ping(1));
        Assert.Equal(1, received);
        var error = Assert.Single(_errors);
        Assert.Equal("broken", error.Subscriber);
        Assert.Equal(typeof(Ping), error.EventType);
    }

    [Fact]
    public void DisposedSubscription_NoLongerReceives()
    {
        var received = 0;
        var subscription = _bus.Subscribe<Ping>("a", _ => received++);
        subscription.Dispose();
        subscription.Dispose();
        _bus.Publish(new Ping(1));
        Assert.Equal(0, received);
    }

    [Fact]
    public void SubscriptionAddedDuringPublish_DoesNotReceiveCurrentEvent()
    {
        var lateCalls = 0;
        var added = false;
        _bus.Subscribe<Ping>("adder", _ =>
        {
            if (!added)
            {
                added = true;
                _bus.Subscribe<Ping>("late", _ => lateCalls++);
            }
        });
        _bus.Publish(new Ping(1));
        Assert.Equal(0, lateCalls);
        _bus.Publish(new Ping(2));
        Assert.Equal(1, lateCalls);
    }

    [Fact]
    public void Dispatch_IsByExactType()
    {
        var baseCalls = 0;
        _bus.Subscribe<Ping>("base", _ => baseCalls++);
        _bus.Publish(new DerivedPing(1));
        Assert.Equal(0, baseCalls);
    }

    [Fact]
    public void NestedPublish_IsDeliveredSynchronously()
    {
        var order = new List<string>();
        _bus.Subscribe<Ping>("outer", p =>
        {
            order.Add($"outer{p.Value}");
            if (p.Value == 1)
            {
                _bus.Publish(new Ping(2));
            }
        });
        _bus.Publish(new Ping(1));
        Assert.Equal(new[] { "outer1", "outer2" }, order);
    }

    [Fact]
    public void Subscribe_RejectsBlankSubscriberName()
    {
        Assert.Throws<ArgumentException>(() => _bus.Subscribe<Ping>(" ", _ => { }));
    }

    [Fact]
    public void Publish_FromAHandler_ReachesOtherSubscribers()
    {
        var bus = new EventBus(_ => { });
        var menu = new Menu("m", HudText.Raw("t"), Array.Empty<MenuItem>());
        var refreshed = new List<HudMenuOpen>();
        bus.Subscribe<HudMenuSelected>("allocation", e => bus.Publish(new HudMenuOpen(e.Player, menu, RefreshOnly: true)));
        bus.Subscribe<HudMenuOpen>("hud", refreshed.Add);
        bus.Publish(new HudMenuSelected(new PlayerId(4), "m", "awp"));
        var open = Assert.Single(refreshed);
        Assert.Equal(new PlayerId(4), open.Player);
        Assert.True(open.RefreshOnly);
    }
}
