using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Transforms;

public interface IReadEventTransform<TEventBase> where TEventBase : class
{
    Type SourceEventType { get; }
    IEnumerable<TEventBase> Apply(ReadEvent<TEventBase> sourceEvent);
}