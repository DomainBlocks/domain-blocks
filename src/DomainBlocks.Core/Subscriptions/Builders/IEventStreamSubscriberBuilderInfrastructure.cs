using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Core.Subscriptions.Builders;

public interface IEventStreamSubscriberBuilderInfrastructure<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    void AddSubscriberBuilder(IEventStreamSubscriberBuilder<TEvent, TPosition> builder);

    IEventStreamSubscriber<TEvent, TPosition> Build(IReadEventAdapter<TEvent> readEventAdapter);
}