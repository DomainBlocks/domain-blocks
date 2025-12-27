namespace DomainBlocks.EventStore.Abstractions.Events;

public static class CommittedEvent
{
    public static CommittedEvent<TPayload> Create<TPayload>(CommittedEventHeader header, TPayload payload)
        where TPayload : notnull
    {
        return new CommittedEvent<TPayload>(header, payload);
    }
}

public sealed class CommittedEvent<TPayload>(CommittedEventHeader header, TPayload payload) : IReadEvent<TPayload>
    where TPayload : notnull
{
    public CommittedEventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}