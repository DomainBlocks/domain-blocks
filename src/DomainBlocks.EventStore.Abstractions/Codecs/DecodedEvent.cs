namespace DomainBlocks.EventStore.Abstractions.Codecs;

public static class DecodedEvent
{
    public static DecodedEvent<TEvent> Create<TEvent>(TEvent @event, IReadOnlyDictionary<string, string> metadata)
        where TEvent : notnull
    {
        return new DecodedEvent<TEvent>(@event, metadata);
    }
}

public readonly struct DecodedEvent<TEvent>(TEvent @event, IReadOnlyDictionary<string, string> metadata)
    where TEvent : notnull
{
    public TEvent Event { get; } = @event;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;

    public void Deconstruct(out TEvent @event, out IReadOnlyDictionary<string, string> metadata)
    {
        @event = Event;
        metadata = Metadata;
    }
}