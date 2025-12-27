using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Read;

public abstract class EventReadTransform<TSourceEvent> : IEventReadTransform
{
    public Type SourceEventType => typeof(TSourceEvent);

    protected abstract IEnumerable<object> Apply(TSourceEvent sourceEvent, CommittedEventHeader header);

    IEnumerable<object> IEventReadTransform.Apply(object sourceEvent, CommittedEventHeader header) =>
        Apply((TSourceEvent)sourceEvent, header);
}