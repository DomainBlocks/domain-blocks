using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public interface IEventWriteTransform
{
    Type FromType { get; }
    object Apply(object @event, NewEventHeader header);
}