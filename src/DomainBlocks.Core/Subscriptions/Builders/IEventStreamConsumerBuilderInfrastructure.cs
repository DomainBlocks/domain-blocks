using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Core.Subscriptions.Builders;

public interface IEventStreamConsumerBuilderInfrastructure<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    void AddConsumerBuilder(IEventStreamConsumerBuilder<TEvent, TPosition> builder);

    IEnumerable<IEventStreamConsumer<TEvent, TPosition>> Build(IReadEventAdapter<TEvent> readEventAdapter);
}