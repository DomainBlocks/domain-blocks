using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public abstract class EventReadTransform<TFrom> : IEventReadTransform
{
    public Type FromType => typeof(TFrom);

    protected abstract IEnumerable<object> Apply(TFrom @event, EventHeader header);

    IEnumerable<object> IEventReadTransform.Apply(object @event, EventHeader header) => Apply((TFrom)@event, header);
}