namespace DomainBlocks.EventStore.Abstractions;

public static class EventRecord
{
    public static EventRecord<TPayload> Create<TPayload>(EventHeader header, TPayload payload)
    {
        return new EventRecord<TPayload>(header, payload);
    }
}

public sealed class EventRecord<TPayload>(EventHeader header, TPayload payload)
{
    public EventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}