using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1.Subscriptions;

public static class ReadOnlyEventStoreExtensions
{
    public static EventStreamSubscriber CreateSubscriber(
        this IReadOnlyEventStore eventStore, IEventStreamConsumer consumer, EventMapper eventMapper)
    {
        return new EventStreamSubscriber(
            pos => eventStore.SubscribeToAll(pos == null ? null : new GlobalPosition(pos.Value.Value)),
            new[] { consumer },
            eventMapper);
    }

    public static EventStreamSubscriber CreateSubscriber(
        this IReadOnlyEventStore eventStore,
        string streamName,
        IEventStreamConsumer consumer,
        EventMapper eventMapper)
    {
        return new EventStreamSubscriber(
            pos => eventStore.SubscribeToStream(streamName, pos == null ? null : new StreamPosition(pos.Value.Value)),
            new[] { consumer },
            eventMapper);
    }
}