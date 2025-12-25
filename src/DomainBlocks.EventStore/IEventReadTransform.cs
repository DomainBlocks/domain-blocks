using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore;

public interface IEventReadTransform
{
    Type SourceType { get; }
    IEnumerable<object> Apply(object @event, CommittedEventHeader header);
}