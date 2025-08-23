using DomainBlocks.Persistence.Abstractions.Events;

namespace DomainBlocks.Persistence.Events;

public interface IEventWriteTransform
{
    Type FromType { get; }
    object Apply(object @event, NewEventHeader header);
}