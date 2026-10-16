namespace DomainBlocks.EventStore;

public interface IReadEvent<out TPayload, TStreamId, TStreamPos, TLogPos>
    where TPayload : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    TPayload Payload { get; }

    IReadOnlyDictionary<string, string> Metadata { get; }

    ReadEventContext<TStreamId, TStreamPos, TLogPos> Context { get; }
}