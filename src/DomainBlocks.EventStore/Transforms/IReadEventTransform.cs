using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Transforms;

public interface IReadEventTransform<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    Type SourceEventType { get; }

    IEnumerable<TEvent> Apply(ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> sourceEvent);
}