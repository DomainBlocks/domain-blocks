using DomainBlocks.Core.Subscriptions.Builders;
using DomainBlocks.EventStore.Serialization;
using EventStore.Client;

namespace DomainBlocks.EventStore.Subscriptions;

public sealed class EventStoreSubscriptionBuilder
{
    private readonly EventStreamSubscriptionBuilder _coreBuilder;
    private readonly EventStoreOptions _eventStoreOptions;

    public EventStoreSubscriptionBuilder(
        EventStreamSubscriptionBuilder coreBuilder,
        EventStoreOptions eventStoreOptions)
    {
        _coreBuilder = coreBuilder;
        _eventStoreOptions = eventStoreOptions;
    }

    public EventStreamSubscriberBuilder<ResolvedEvent, Position> FromAllEventsStream()
    {
        var subscribersBuilder = new EventStreamSubscriberBuilder<ResolvedEvent, Position>(_coreBuilder);

        ((IEventStreamSubscriptionBuilderInfrastructure)_coreBuilder).WithSubscriptionFactory(() =>
        {
            var eventStoreClient = _eventStoreOptions.GetOrCreateEventStoreClient();
            var eventAdapter = new EventStoreEventAdapter(_eventStoreOptions.GetEventDataSerializer());

            var subscriber = ((IEventStreamSubscriberBuilderInfrastructure<ResolvedEvent, Position>)subscribersBuilder)
                .Build(eventAdapter);

            return new EventStoreAllEventsStreamSubscription(subscriber, eventStoreClient);
        });

        return subscribersBuilder;
    }

    public EventStreamSubscriberBuilder<ResolvedEvent, StreamPosition> FromEventStream(string streamName)
    {
        throw new NotImplementedException();
    }
}