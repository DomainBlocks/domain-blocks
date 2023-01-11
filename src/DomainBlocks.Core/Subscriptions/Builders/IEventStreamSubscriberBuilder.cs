using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Core.Subscriptions.Builders;

public interface IEventStreamSubscriberBuilder<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    IEventStreamSubscriber<TEvent, TPosition> Build(IReadEventAdapter<TEvent> readEventAdapter);
}