namespace DomainBlocks.EventStore.Abstractions;

// UncommittedEventRecord

public static class NewEventRecord
{
    public static NewEventRecord<TPayload> Create<TPayload>(TPayload payload)
    {
        return new NewEventRecord<TPayload>(NewEventHeader.Empty, payload);
    }

    public static NewEventRecord<TPayload> Create<TPayload>(NewEventHeader header, TPayload payload)
    {
        return new NewEventRecord<TPayload>(header, payload);
    }
}

public sealed class NewEventRecord<TPayload>(NewEventHeader header, TPayload payload)
{
    public NewEventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}