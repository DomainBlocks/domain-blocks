namespace DomainBlocks.EventStore;

public static class ReadEventContext
{
    public static ReadEventContext<TStreamId, TStreamPos, TLogPos> Create<TStreamId, TStreamPos, TLogPos>(
        TStreamId streamId,
        string eventName,
        DateTimeOffset createdAt,
        TStreamPos streamPosition,
        TLogPos logPosition)
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return new ReadEventContext<TStreamId, TStreamPos, TLogPos>(
            streamId,
            eventName,
            createdAt,
            streamPosition,
            logPosition);
    }
}

public readonly struct ReadEventContext<TStreamId, TStreamPos, TLogPos>(
    TStreamId streamId,
    string eventName,
    DateTimeOffset createdAt,
    TStreamPos streamPosition,
    TLogPos logPosition)
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    public TStreamId StreamId { get; } = streamId;
    public string EventName { get; } = eventName;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public TStreamPos StreamPosition { get; } = streamPosition;
    public TLogPos LogPosition { get; } = logPosition;
}