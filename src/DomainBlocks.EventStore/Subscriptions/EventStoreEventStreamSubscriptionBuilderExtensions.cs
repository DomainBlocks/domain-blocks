using DomainBlocks.Core.Subscriptions.Builders;
using EventStore.Client;

namespace DomainBlocks.EventStore.Subscriptions;

public static class EventStoreEventStreamSubscriptionBuilderExtensions
{
    public static EventStoreSubscriptionBuilder UseEventStore(
        this EventStreamSubscriptionBuilder builder,
        string connectionString)
    {
        var eventStoreOptions = new EventStoreOptions().WithEventStoreClientFactory(() =>
        {
            var settings = EventStoreClientSettings.Create(connectionString);
            return new EventStoreClient(settings);
        });

        return new EventStoreSubscriptionBuilder(builder, eventStoreOptions);
    }

    public static EventStoreSubscriptionBuilder UseEventStore(
        this EventStreamSubscriptionBuilder builder,
        Action<EventStoreOptionsBuilder> builderAction)
    {
        var eventStoreOptionsBuilder = new EventStoreOptionsBuilder();
        builderAction(eventStoreOptionsBuilder);
        var eventStoreOptions = eventStoreOptionsBuilder.Options;
        return new EventStoreSubscriptionBuilder(builder, eventStoreOptions);
    }
}