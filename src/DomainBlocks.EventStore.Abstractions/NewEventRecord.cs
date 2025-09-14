namespace DomainBlocks.EventStore.Abstractions;

public static class NewEventRecord
{
    public static NewEventRecord<TPayload> Create<TPayload>(NewEventHeader header, TPayload payload)
    {
        return new NewEventRecord<TPayload>(header, payload);
    }
}

public readonly struct NewEventRecord<TPayload>(NewEventHeader header, TPayload payload)
{
    public NewEventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}