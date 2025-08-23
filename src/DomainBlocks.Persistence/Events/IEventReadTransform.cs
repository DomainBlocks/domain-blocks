using DomainBlocks.Persistence.Abstractions.Events;

namespace DomainBlocks.Persistence.Events;

public interface IEventReadTransform
{
    Type FromType { get; }
    IEnumerable<object> Apply(object @event, EventHeader header);
}