namespace DomainBlocks.Persistence.Abstractions.Events;

public readonly struct EventRecord<TPayload>(EventHeader header, TPayload payload)
{
    public EventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}