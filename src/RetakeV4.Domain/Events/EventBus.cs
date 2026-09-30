using System.Collections.Immutable;

namespace RetakeV4.Domain.Events;

// Game-thread only: CounterStrikeSharp dispatches every event on the main thread.
public sealed class EventBus : IEventBus
{
    private readonly Action<BusError> _onError;
    private ImmutableDictionary<Type, ImmutableList<Subscription>> _subscriptions =
        ImmutableDictionary<Type, ImmutableList<Subscription>>.Empty;

    public EventBus(Action<BusError> onError)
    {
        ArgumentNullException.ThrowIfNull(onError);
        _onError = onError;
    }

    public IDisposable Subscribe<TEvent>(string subscriber, Action<TEvent> handler) where TEvent : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriber);
        ArgumentNullException.ThrowIfNull(handler);
        var key = typeof(TEvent);
        var subscription = new Subscription(subscriber, evt => handler((TEvent)evt));
        var current = _subscriptions.GetValueOrDefault(key, ImmutableList<Subscription>.Empty);
        _subscriptions = _subscriptions.SetItem(key, current.Add(subscription));
        return new Unsubscriber(() => Remove(key, subscription));
    }

    public void Publish<TEvent>(TEvent evt) where TEvent : notnull
    {
        if (!_subscriptions.TryGetValue(typeof(TEvent), out var subscriptions))
        {
            return;
        }
        foreach (var subscription in subscriptions)
        {
            Invoke(subscription, evt, typeof(TEvent));
        }
    }

    private void Invoke(Subscription subscription, object evt, Type eventType)
    {
        try
        {
            subscription.Handler(evt);
        }
        catch (Exception ex)
        {
            _onError(new BusError(subscription.Subscriber, eventType, ex));
        }
    }

    private void Remove(Type key, Subscription subscription)
    {
        if (_subscriptions.TryGetValue(key, out var subscriptions))
        {
            _subscriptions = _subscriptions.SetItem(key, subscriptions.Remove(subscription));
        }
    }

    private sealed class Subscription(string subscriber, Action<object> handler)
    {
        public string Subscriber { get; } = subscriber;
        public Action<object> Handler { get; } = handler;
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
