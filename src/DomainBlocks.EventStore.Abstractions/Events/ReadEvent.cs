namespace DomainBlocks.EventStore.Abstractions.Events;

public static class ReadEvent
{
    public static ReadEvent<TValue> Create<TValue>(ReadEventHeader header, TValue value, bool isTransformed = false)
        where TValue : notnull
    {
        return new ReadEvent<TValue>(header, value, isTransformed);
    }
}

public sealed class ReadEvent<TValue>(ReadEventHeader header, TValue value, bool isTransformed = false)
    where TValue : notnull
{
    public ReadEventHeader Header { get; } = header;
    public TValue Value { get; } = value;
    public bool IsTransformed { get; } = isTransformed;
}