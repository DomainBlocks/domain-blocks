namespace DomainBlocks.EventStore.Abstractions.Events;

public static class SerializedAppendEvent
{
    public static SerializedAppendEvent<TSerialized> Create<TSerialized>(
        string eventName,
        TSerialized eventData,
        TSerialized? metadata = default)
        where TSerialized : notnull
    {
        return new SerializedAppendEvent<TSerialized>(eventName, eventData, metadata);
    }
}

public readonly struct SerializedAppendEvent<TSerialized>(
    string eventName,
    TSerialized eventData,
    TSerialized? metadata = default)
    where TSerialized : notnull
{
    public string EventName { get; } = eventName;
    public TSerialized EventData { get; } = eventData;
    public TSerialized? Metadata { get; } = metadata;
}