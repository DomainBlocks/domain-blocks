using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Core.Subscriptions.Builders;

public interface IEventStreamConsumerBuilder<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    IEventStreamConsumer<TEvent, TPosition> Build(IReadEventAdapter<TEvent> readEventAdapter);
}