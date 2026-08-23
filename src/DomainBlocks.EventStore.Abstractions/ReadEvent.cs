namespace DomainBlocks.EventStore.Abstractions;

public static class ReadEvent
{
    public static ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> Create<TEvent, TStreamId, TStreamPos, TLogPos>(
        TEvent @event,
        TStreamId streamId,
        IReadOnlyDictionary<string, string> metadata,
        DateTimeOffset createdAt,
        TStreamPos streamPosition,
        TLogPos logPosition)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        var inner = new ReadEvent<TEvent, TStreamId>(@event, streamId, metadata, createdAt);
        return new ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>(inner, streamPosition, logPosition);
    }
}

public readonly struct ReadEvent<TEvent, TStreamId>(
    TEvent @event,
    TStreamId streamId,
    IReadOnlyDictionary<string, string> metadata,
    DateTimeOffset createdAt)
    where TEvent : notnull
    where TStreamId : notnull
{
    public TEvent Event { get; } = @event;
    public TStreamId StreamId { get; } = streamId;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;
    public DateTimeOffset CreatedAt { get; } = createdAt;
}

public readonly struct ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>(
    ReadEvent<TEvent, TStreamId> inner,
    TStreamPos streamPosition,
    TLogPos logPosition)
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private readonly ReadEvent<TEvent, TStreamId> _inner = inner;

    public TEvent Event { get; } = inner.Event;
    public TStreamId StreamId { get; } = inner.StreamId;
    public IReadOnlyDictionary<string, string> Metadata { get; } = inner.Metadata;
    public DateTimeOffset CreatedAt { get; } = inner.CreatedAt;
    public TStreamPos StreamPosition { get; } = streamPosition;
    public TLogPos LogPosition { get; } = logPosition;

    public static implicit operator ReadEvent<TEvent, TStreamId>(
        ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> e) =>
        e._inner;
}