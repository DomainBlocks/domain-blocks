using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Read;

public abstract class ReadEventTransform<TEventBase, TSourceEvent> : IReadEventTransform<TEventBase>
    where TEventBase : class
    where TSourceEvent : TEventBase
{
    public Type SourceEventType => typeof(TSourceEvent);

    protected abstract IEnumerable<TEventBase> Apply(TSourceEvent sourceEvent, ReadEventHeader sourceHeader);

    IEnumerable<TEventBase> IReadEventTransform<TEventBase>.Apply(ReadEvent<TEventBase> sourceEvent)
    {
        return Apply((TSourceEvent)sourceEvent.Value, sourceEvent.Header);
    }
}