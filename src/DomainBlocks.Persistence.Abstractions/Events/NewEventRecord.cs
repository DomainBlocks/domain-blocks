namespace DomainBlocks.Persistence.Abstractions.Events;

public readonly struct NewEventRecord<TPayload>(NewEventHeader header, TPayload payload)
{
    public NewEventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}