namespace DomainBlocks.EventStore.Abstractions;

public static class EncodedAppendEvent
{
    public static EncodedAppendEvent<TEventData, TMetadata> Create<TEventData, TMetadata>(
        string eventName,
        TEventData eventData,
        TMetadata? metadata = default)
        where TEventData : notnull
        where TMetadata : notnull
    {
        return new EncodedAppendEvent<TEventData, TMetadata>(eventName, eventData, metadata);
    }
}

public readonly struct EncodedAppendEvent<TEventData, TMetadata>(
    string eventName,
    TEventData eventData,
    TMetadata? metadata = default)
    where TEventData : notnull
    where TMetadata : notnull
{
    public readonly string EventName = eventName;
    public readonly TEventData EventData = eventData;
    public readonly TMetadata? Metadata = metadata;

    public void Deconstruct(out string eventName, out TEventData eventData, out TMetadata? metadata)
    {
        eventName = EventName;
        eventData = EventData;
        metadata = Metadata;
    }
}