using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Read;

public sealed class TransformedEvent<TPayload>(CommittedEventHeader header, TPayload payload) : IReadEvent<TPayload>
    where TPayload : notnull
{
    public CommittedEventHeader Header { get; } = header;
    public TPayload Payload { get; } = payload;
}