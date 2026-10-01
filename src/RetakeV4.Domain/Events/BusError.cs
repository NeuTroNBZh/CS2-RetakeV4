namespace RetakeV4.Domain.Events;

public sealed record BusError(string Subscriber, Type EventType, Exception Exception);
