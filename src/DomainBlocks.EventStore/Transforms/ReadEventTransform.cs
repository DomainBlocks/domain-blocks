using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Transforms;

public abstract class ReadEventTransform<TEventBase, TStreamId, TStreamPos, TLogPos, TSourceEvent> :
    IReadEventTransform<TEventBase, TStreamId, TStreamPos, TLogPos>
    where TEventBase : class
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
    where TSourceEvent : TEventBase
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