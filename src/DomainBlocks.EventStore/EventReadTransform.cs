using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public abstract class EventReadTransform<TFrom> : IEventReadTransform
{
    public Type FromType => typeof(TFrom);

    protected abstract IEnumerable<object> Apply(TFrom @event, CommittedEventHeader header);

    IEnumerable<object> IEventReadTransform.Apply(object @event, CommittedEventHeader header) =>
        Apply((TFrom)@event, header);
}