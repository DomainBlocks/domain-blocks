using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Read;

public sealed class TransformedEvent<TValue>(CommittedEventHeader header, TValue value) : IReadEvent<TValue>
    where TValue : notnull
{
    public CommittedEventHeader Header { get; } = header;
    public TValue Value { get; } = value;
}