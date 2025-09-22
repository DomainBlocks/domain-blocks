namespace DomainBlocks.EventStore.Abstractions;

public static class UncommittedEvent
{
    public static UncommittedEvent<TPayload> Create<TPayload>(TPayload payload) where TPayload : notnull
    {
        return new UncommittedEvent<TPayload>(UncommittedEventHeader.Empty, payload);
    }

    public static UncommittedEvent<TPayload> Create<TPayload>(UncommittedEventHeader header, TPayload payload)
        where TPayload : notnull
    {
        return new UncommittedEvent<TPayload>(header, payload);
    }
}

public sealed class UncommittedEvent<TPayload>(UncommittedEventHeader header, TPayload payload) where TPayload : notnull
{
    public UncommittedEventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}