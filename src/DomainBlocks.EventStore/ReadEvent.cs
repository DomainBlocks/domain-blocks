using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore;

public static class ReadEvent
{
    public static ReadEvent<TEventBase> Create<TEventBase>(
        TEventBase @event,
        IReadOnlyDictionary<string, string> metadata,
        ReadEventContext context) where TEventBase : class
    {
        return new ReadEvent<TEventBase>(@event, metadata, context);
    }
}

public readonly struct ReadEvent<TEventBase>(
    TEventBase @event,
    IReadOnlyDictionary<string, string> metadata,
    ReadEventContext context)
    where TEventBase : class
{
    public TEventBase Event { get; } = @event;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;
    public ReadEventContext Context { get; } = context;
}