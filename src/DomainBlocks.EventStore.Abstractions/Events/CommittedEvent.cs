namespace DomainBlocks.EventStore.Abstractions.Events;

public static class CommittedEvent
{
    public static CommittedEvent<TValue> Create<TValue>(CommittedEventHeader header, TValue value)
        where TValue : notnull
    {
        return new CommittedEvent<TValue>(header, value);
    }
}

public sealed class CommittedEvent<TValue>(CommittedEventHeader header, TValue value) : IReadEvent<TValue>
    where TValue : notnull
{
    public CommittedEventHeader Header { get; } = header;
    public TValue Value { get; } = value;
}