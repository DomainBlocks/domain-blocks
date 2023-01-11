using DomainBlocks.Core.Subscriptions.Builders;
using DomainBlocks.SqlStreamStore.Serialization;
using SqlStreamStore.Streams;

namespace DomainBlocks.SqlStreamStore.Subscriptions;

public class SqlStreamStoreSubscriptionBuilder
{
    private readonly EventStreamSubscriptionBuilder _coreBuilder;
    private readonly SqlStreamStoreOptions _streamStoreOptions;

    public SqlStreamStoreSubscriptionBuilder(
        EventStreamSubscriptionBuilder coreBuilder,
        SqlStreamStoreOptions streamStoreOptions)
    {
        _coreBuilder = coreBuilder;
        _streamStoreOptions = streamStoreOptions;
    }

    public EventStreamSubscriberBuilder<StreamMessage, long> FromAllEventsStream()
    {
        var subscribersBuilder = new EventStreamSubscriberBuilder<StreamMessage, long>(_coreBuilder);

        ((IEventStreamSubscriptionBuilderInfrastructure)_coreBuilder).WithSubscriptionFactory(() =>
        {
            var streamStore = _streamStoreOptions.GetOrCreateStreamStore();
            var eventAdapter = new SqlStreamStoreEventAdapter(_streamStoreOptions.GetEventDataSerializer());

            var subscriber = ((IEventStreamSubscriberBuilderInfrastructure<StreamMessage, long>)subscribersBuilder)
                .Build(eventAdapter);

            return new SqlStreamStoreAllEventsStreamSubscription(subscriber, streamStore);
        });

        return subscribersBuilder;
    }

    public EventStreamSubscriberBuilder<StreamMessage, int> FromEventStream(string streamName)
    {
        throw new NotImplementedException();
    }
}