namespace DomainBlocks.EventStore.Abstractions;

public static class EncodedEvent
{
    public static EncodedEvent<TEventData, TMetadata> Create<TEventData, TMetadata>(
        Guid eventId,
        string eventName,
        TEventData eventData,
        TMetadata? metadata)
        where TEventData : notnull
    {
        return new EncodedEvent<TEventData, TMetadata>(eventId, eventName, eventData, metadata);
    }
}

public readonly struct EncodedEvent<TEventData, TMetadata>(
    Guid eventId,
    string eventName,
    TEventData eventData,
    TMetadata? metadata)
    where TEventData : notnull
{
    public Guid EventId { get; } = eventId;
    public string EventName { get; } = eventName;
    public TEventData EventData { get; } = eventData;
    public TMetadata? Metadata { get; } = metadata;

    public void Deconstruct(out Guid eventId, out string eventName, out TEventData eventData, out TMetadata? metadata)
    {
        eventId = EventId;
        eventName = EventName;
        eventData = EventData;
        metadata = Metadata;
    }
}