using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Read;

public interface IEventReadTransform<TEventBase> where TEventBase : class
{
    Type SourceEventType { get; }
    IEnumerable<TEventBase> Apply(TEventBase sourceEvent, ReadEventHeader header);
}