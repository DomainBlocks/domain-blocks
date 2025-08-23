using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public abstract class EventWriteTransform<TFrom> : IEventWriteTransform
{
    public Type FromType => typeof(TFrom);

    protected abstract object Apply(TFrom @event, NewEventHeader header);

    object IEventWriteTransform.Apply(object @event, NewEventHeader header) => Apply((TFrom)@event, header);
}