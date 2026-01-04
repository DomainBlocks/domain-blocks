using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Read;

public abstract class ReadEventTransform<TEventBase, TSourceEvent> : IReadEventTransform<TEventBase>
    where TEventBase : class
    where TSourceEvent : TEventBase
{
    public Type SourceEventType => typeof(TSourceEvent);

    protected abstract IEnumerable<TEventBase> Apply(
        TSourceEvent @event,
        IReadOnlyDictionary<string, string> metadata,
        ReadEventContext context);

    IEnumerable<TEventBase> IReadEventTransform<TEventBase>.Apply(ReadEvent<TEventBase> @event)
    {
        return Apply((TSourceEvent)@event.Event, @event.Metadata, @event.Context);
    }
}