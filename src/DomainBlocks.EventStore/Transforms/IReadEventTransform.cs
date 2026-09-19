namespace DomainBlocks.EventStore.Transforms;

public interface IReadEventTransform<TEvent> where TEvent : notnull
{
    Type SourceEventType { get; }

    IEnumerable<TEvent> Apply<TStreamId, TStreamPos, TLogPos>(
        TEvent @event,
        in ReadEventContext<TStreamId, TStreamPos, TLogPos> context)
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull;
}