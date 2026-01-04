namespace DomainBlocks.EventStore.Abstractions;

public static class DecodedEvent
{
    public static DecodedEvent<TEventBase> Create<TEventBase>(
        TEventBase @event,
        IReadOnlyDictionary<string, string> metadata)
        where TEventBase : class
    {
        return new DecodedEvent<TEventBase>(@event, metadata);
    }
}

public readonly struct DecodedEvent<TEventBase>(
    TEventBase @event,
    IReadOnlyDictionary<string, string> metadata)
    where TEventBase : class
{
    public TEventBase Event { get; } = @event;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;

    public void Deconstruct(out TEventBase @event, out IReadOnlyDictionary<string, string> metadata)
    {
        @event = Event;
        metadata = Metadata;
    }
}