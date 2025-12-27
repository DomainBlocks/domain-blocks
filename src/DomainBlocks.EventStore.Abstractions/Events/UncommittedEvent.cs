namespace DomainBlocks.EventStore.Abstractions.Events;

public static class UncommittedEvent
{
    public static UncommittedEvent<TValue> Create<TValue>(TValue value) where TValue : notnull
    {
        return new UncommittedEvent<TValue>(UncommittedEventHeader.Empty, value);
    }

    public static UncommittedEvent<TValue> Create<TValue>(UncommittedEventHeader header, TValue value)
        where TValue : notnull
    {
        return new UncommittedEvent<TValue>(header, value);
    }
}

public sealed class UncommittedEvent<TValue>(UncommittedEventHeader header, TValue value) where TValue : notnull
{
    public UncommittedEventHeader Header { get; } = header;
    public TValue Value { get; } = value;
}