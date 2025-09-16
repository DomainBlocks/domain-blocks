namespace DomainBlocks.EventStore.Abstractions;

public static class UncommittedEvent
{
    public static UncommittedEvent<TPayload> Create<TPayload>(TPayload payload)
    {
        return new UncommittedEvent<TPayload>(UncommittedEventHeader.Empty, payload);
    }

    public static UncommittedEvent<TPayload> Create<TPayload>(UncommittedEventHeader header, TPayload payload)
    {
        return new UncommittedEvent<TPayload>(header, payload);
    }
}

public sealed class UncommittedEvent<TPayload>(UncommittedEventHeader header, TPayload payload)
{
    public UncommittedEventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}