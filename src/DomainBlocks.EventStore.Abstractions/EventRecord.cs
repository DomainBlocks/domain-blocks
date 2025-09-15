namespace DomainBlocks.EventStore.Abstractions;

public readonly struct EventRecord<TPayload>(EventHeader header, TPayload payload)
{
    public EventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}