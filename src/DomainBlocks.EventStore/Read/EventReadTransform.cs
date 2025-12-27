using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Read;

public abstract class EventReadTransform<TEventBase, TSourceEvent> : IEventReadTransform<TEventBase>
    where TEventBase : class
    where TSourceEvent : TEventBase
{
    public Type SourceEventType => typeof(TSourceEvent);

    protected abstract IEnumerable<TEventBase> Apply(TSourceEvent sourceEvent, ReadEventHeader header);

    IEnumerable<TEventBase> IEventReadTransform<TEventBase>.Apply(TEventBase sourceEvent, ReadEventHeader header)
    {
        return Apply((TSourceEvent)sourceEvent, header);
    }
}