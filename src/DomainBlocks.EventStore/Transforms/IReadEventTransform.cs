namespace DomainBlocks.EventStore.Transforms;

public interface IReadEventTransform<TEvent> where TEvent : notnull
{
    Type SourceEventType { get; }

    IEnumerable<TEvent> Apply<TStreamId, TStreamPos, TLogPos>(
        in ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event)
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull;
}