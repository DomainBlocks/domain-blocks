using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Transforms;

public interface IReadEventTransform<TEventBase, TStreamId, TStreamPos, TLogPos>
    where TEventBase : class
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    Type SourceEventType { get; }

    IEnumerable<TEventBase> Apply(ReadEvent<TEventBase, TStreamId, TStreamPos, TLogPos> sourceEvent);
}