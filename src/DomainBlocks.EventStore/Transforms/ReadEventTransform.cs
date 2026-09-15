namespace DomainBlocks.EventStore.Transforms;

/// <summary>
/// Base class for a transform of one source event type that does not depend on the store's position types.
/// Implement <see cref="IReadEventTransform{TEvent}"/> directly to receive the full
/// <see cref="ReadEventContext{TStreamId, TStreamPos, TLogPos}"/>.
/// </summary>
public abstract class ReadEventTransform<TEventBase, TSourceEvent> : IReadEventTransform<TEventBase>
    where TEventBase : notnull
    where TSourceEvent : TEventBase
{
    public Type SourceEventType => typeof(TSourceEvent);

    protected abstract IEnumerable<TEventBase> Apply(TSourceEvent @event, ReadEventInfo info);

    IEnumerable<TEventBase> IReadEventTransform<TEventBase>.Apply<TStreamId, TStreamPos, TLogPos>(
        TEventBase @event,
        in ReadEventContext<TStreamId, TStreamPos, TLogPos> context)
    {
        return Apply((TSourceEvent)@event, new ReadEventInfo(context.Metadata, context.CreatedAt));
    }
}