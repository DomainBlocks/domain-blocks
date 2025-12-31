namespace DomainBlocks.EventStore.Abstractions.Events;

public static class AppendEvent
{
    public static AppendEvent<TEventData, TMetadata> Create<TEventData, TMetadata>(
        string eventName,
        TEventData eventData,
        TMetadata? metadata = default)
        where TEventData : notnull
        where TMetadata : notnull
    {
        return new AppendEvent<TEventData, TMetadata>(eventName, eventData, metadata);
    }
}

public readonly struct AppendEvent<TEventData, TMetadata>(
    string eventName,
    TEventData eventData,
    TMetadata? metadata = default)
    where TEventData : notnull
    where TMetadata : notnull
{
    public string EventName { get; } = eventName;
    public TEventData EventData { get; } = eventData;
    public TMetadata? Metadata { get; } = metadata;
}