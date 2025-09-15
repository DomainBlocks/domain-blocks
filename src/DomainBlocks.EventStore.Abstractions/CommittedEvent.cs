namespace DomainBlocks.EventStore.Abstractions;

public static class CommittedEvent
{
    public static CommittedEvent<TPayload> Create<TPayload>(CommittedEventHeader header, TPayload payload)
    {
        return new CommittedEvent<TPayload>(header, payload);
    }
}

public sealed class CommittedEvent<TPayload>(CommittedEventHeader header, TPayload payload)
{
    public CommittedEventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}