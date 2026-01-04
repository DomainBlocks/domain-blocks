namespace DomainBlocks.EventStore.Abstractions;

public static class ReadEvent
{
    public static ReadEvent<TEvent> Create<TEvent>(
        TEvent @event,
        IReadOnlyDictionary<string, string> metadata,
        ReadEventContext context)
        where TEvent : notnull
    {
        return new ReadEvent<TEvent>(@event, metadata, context);
    }
}

public readonly struct ReadEvent<TEvent>(
    TEvent @event,
    IReadOnlyDictionary<string, string> metadata,
    ReadEventContext context)
    where TEvent : notnull
{
    public TEvent Event { get; } = @event;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;
    public ReadEventContext Context { get; } = context;
}