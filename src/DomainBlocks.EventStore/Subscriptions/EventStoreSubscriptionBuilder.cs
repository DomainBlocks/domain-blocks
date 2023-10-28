using DomainBlocks.Core.Subscriptions;
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

    public EventStreamConsumerBuilder<ResolvedEvent, Position> FromAllEventsStream()
    {
        var subscribersBuilder = new EventStreamConsumerBuilder<ResolvedEvent, Position>(_coreBuilder);

        ((IEventStreamSubscriptionBuilderInfrastructure)_coreBuilder).WithSubscriptionFactory(() =>
        {
            var eventStoreClient = _eventStoreOptions.GetOrCreateEventStoreClient();
            var subscriber = new EventStoreAllEventsStreamSubscriber(eventStoreClient);
            var eventAdapter = new EventStoreEventAdapter(_eventStoreOptions.GetEventDataSerializer());

            var consumers = ((IEventStreamConsumerBuilderInfrastructure<ResolvedEvent, Position>)subscribersBuilder)
                .Build(eventAdapter);

            return new EventStreamSubscription<ResolvedEvent, Position>(subscriber, consumers);
        });

        return subscribersBuilder;
    }

    public EventStreamConsumerBuilder<ResolvedEvent, StreamPosition> FromEventStream(string streamName)
    {
        throw new NotImplementedException();
    }
}