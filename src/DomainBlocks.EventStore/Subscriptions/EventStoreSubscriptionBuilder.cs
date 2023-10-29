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

    public EventStreamConsumersBuilder<ResolvedEvent, Position> FromAllEventsStream()
    {
        var consumersBuilder = new EventStreamConsumersBuilder<ResolvedEvent, Position>(_coreBuilder);

        ((IEventStreamSubscriptionBuilderInfrastructure)_coreBuilder).WithSubscriptionFactory(() =>
        {
            var eventStoreClient = _eventStoreOptions.GetOrCreateEventStoreClient();
            var subscribable = new EventStoreAllEventsStreamSubscribable(eventStoreClient);
            var eventAdapter = new EventStoreEventAdapter(_eventStoreOptions.GetEventDataSerializer());

            var consumers = ((IEventStreamConsumerBuilderInfrastructure<ResolvedEvent, Position>)consumersBuilder)
                .Build(eventAdapter);

            return new EventStreamSubscription<ResolvedEvent, Position>(subscribable, consumers);
        });

        return consumersBuilder;
    }

    public EventStreamConsumersBuilder<ResolvedEvent, StreamPosition> FromEventStream(string streamName)
    {
        throw new NotImplementedException();
    }
}