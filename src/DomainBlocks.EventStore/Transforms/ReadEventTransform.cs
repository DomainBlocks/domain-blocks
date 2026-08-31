using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Transforms;

public abstract class ReadEventTransform<TEventBase, TSourceEvent, TStreamId, TStreamPos, TLogPos> :
    IReadEventTransform<TEventBase, TStreamId, TStreamPos, TLogPos>
    where TEventBase : class
    where TSourceEvent : TEventBase
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull

{
    public Type SourceEventType => typeof(TSourceEvent);

    protected abstract IEnumerable<TEventBase> Apply(
        TSourceEvent @event,
        ReadEventContext<TStreamId, TStreamPos, TLogPos> context);

    IEnumerable<TEventBase> IReadEventTransform<TEventBase, TStreamId, TStreamPos, TLogPos>.Apply(
        ReadEvent<TEventBase, TStreamId, TStreamPos, TLogPos> @event)
    {
        return Apply((TSourceEvent)@event.Payload, @event.Context);
    }
}