namespace DomainBlocks.EventStore.Abstractions.Events;

public static class ReadEvent
{
    public static ReadEvent<TEventData, TMetadata> Create<TEventData, TMetadata>(
        string eventName,
        TEventData eventData,
        TMetadata? metadata,
        ReadEventContext context)
        where TEventData : notnull
        where TMetadata : notnull
    {
        return new ReadEvent<TEventData, TMetadata>(eventName, eventData, metadata, context);
    }
}

public readonly struct ReadEvent<TEventData, TMetadata>(
    string eventName,
    TEventData eventData,
    TMetadata? metadata,
    ReadEventContext context)
    where TEventData : notnull
    where TMetadata : notnull
{
    public string EventName { get; } = eventName;
    public TEventData EventData { get; } = eventData;
    public TMetadata? Metadata { get; } = metadata;
    public ReadEventContext Context { get; } = context;
}