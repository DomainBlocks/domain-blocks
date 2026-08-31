namespace DomainBlocks.EventStore.Abstractions;

public static class ReadEventContext
{
    public static ReadEventContext<TStreamId, TStreamPos, TLogPos> Create<TStreamId, TStreamPos, TLogPos>(
        TStreamId streamId,
        IReadOnlyDictionary<string, string> metadata,
        DateTimeOffset createdAt,
        TStreamPos streamPosition,
        TLogPos logPosition)
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return new ReadEventContext<TStreamId, TStreamPos, TLogPos>(
            streamId,
            metadata,
            createdAt,
            streamPosition,
            logPosition);
    }
}

public readonly struct ReadEventContext<TStreamId, TStreamPos, TLogPos>(
    TStreamId streamId,
    IReadOnlyDictionary<string, string> metadata,
    DateTimeOffset createdAt,
    TStreamPos streamPosition,
    TLogPos logPosition)
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    public TStreamId StreamId { get; } = streamId;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public TStreamPos StreamPosition { get; } = streamPosition;
    public TLogPos LogPosition { get; } = logPosition;
}