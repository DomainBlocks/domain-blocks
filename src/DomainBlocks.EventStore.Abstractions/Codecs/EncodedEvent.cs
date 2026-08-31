namespace DomainBlocks.EventStore.Abstractions.Codecs;

public static class EncodedEvent
{
    public static EncodedEvent<TEventData, TMetadata> Create<TEventData, TMetadata>(
        string eventName,
        TEventData eventData,
        TMetadata? metadata)
        where TEventData : notnull
    {
        return new EncodedEvent<TEventData, TMetadata>(eventName, eventData, metadata);
    }
}

public readonly struct EncodedEvent<TEventData, TMetadata>(string eventName, TEventData eventData, TMetadata? metadata)
    where TEventData : notnull
{
    public string EventName { get; } = eventName;
    public TEventData EventData { get; } = eventData;
    public TMetadata? Metadata { get; } = metadata;

    public void Deconstruct(out string eventName, out TEventData eventData, out TMetadata? metadata)
    {
        eventName = EventName;
        eventData = EventData;
        metadata = Metadata;
    }
}