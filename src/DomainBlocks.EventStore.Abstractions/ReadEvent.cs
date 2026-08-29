namespace DomainBlocks.EventStore.Abstractions;

public static class ReadEvent
{
    public static ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> Create<TEvent, TStreamId, TStreamPos, TLogPos>(
        TEvent @event,
        ReadEventContext<TStreamId, TStreamPos, TLogPos> context)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return new ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>(@event, context);
    }
}

public readonly struct ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>(
    TEvent @event,
    ReadEventContext<TStreamId, TStreamPos, TLogPos> context)
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    public TEvent Event { get; } = @event;
    public ReadEventContext<TStreamId, TStreamPos, TLogPos> Context { get; } = context;
}