using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore;

public interface IEventReadTransform
{
    Type FromType { get; }
    IEnumerable<object> Apply(object @event, CommittedEventHeader header);
}