namespace DomainBlocks.EventStore;

public static class ReadEvent
{
    public static ReadEvent<TPayload, TStreamId, TStreamPos, TLogPos> Create<TPayload, TStreamId, TStreamPos, TLogPos>(
        TPayload payload,
        IReadOnlyDictionary<string, string> metadata,
        ReadEventContext<TStreamId, TStreamPos, TLogPos> context)
        where TPayload : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return new ReadEvent<TPayload, TStreamId, TStreamPos, TLogPos>(payload, metadata, context);
    }
}

public readonly struct ReadEvent<TPayload, TStreamId, TStreamPos, TLogPos>(
    TPayload payload,
    IReadOnlyDictionary<string, string> metadata,
    ReadEventContext<TStreamId, TStreamPos, TLogPos> context) :
    IReadEvent<TPayload, TStreamId, TStreamPos, TLogPos>
    where TPayload : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    public TPayload Payload { get; } = payload;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;
    public ReadEventContext<TStreamId, TStreamPos, TLogPos> Context { get; } = context;
}