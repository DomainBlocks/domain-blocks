namespace DomainBlocks.EventStore.Abstractions;

public static class ReadEvent
{
    public static ReadEvent<TPayload, TStreamId, TStreamPos, TLogPos> Create<TPayload, TStreamId, TStreamPos, TLogPos>(
        TPayload payload,
        ReadEventContext<TStreamId, TStreamPos, TLogPos> context)
        where TPayload : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return new ReadEvent<TPayload, TStreamId, TStreamPos, TLogPos>(payload, context);
    }
}

public readonly struct ReadEvent<TPayload, TStreamId, TStreamPos, TLogPos>(
    TPayload payload,
    ReadEventContext<TStreamId, TStreamPos, TLogPos> context)
    where TPayload : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    public TPayload Payload { get; } = payload;
    public ReadEventContext<TStreamId, TStreamPos, TLogPos> Context { get; } = context;
}