namespace RetakeV4.Domain.Events;

public interface IEventBus
{
    IDisposable Subscribe<TEvent>(string subscriber, Action<TEvent> handler) where TEvent : notnull;

    void Publish<TEvent>(TEvent evt) where TEvent : notnull;
}
