using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Read;

public interface IEventReadTransform
{
    Type SourceEventType { get; }
    IEnumerable<object> Apply(object sourceEvent, CommittedEventHeader header);
}