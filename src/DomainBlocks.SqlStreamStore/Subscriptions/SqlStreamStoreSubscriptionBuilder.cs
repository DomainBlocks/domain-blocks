using DomainBlocks.Core.Subscriptions.Builders;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using DomainBlocks.SqlStreamStore.Serialization;

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

    public EventStreamConsumerBuilder<StreamMessage, long> FromAllEventsStream()
    {
        var subscribersBuilder = new EventStreamConsumerBuilder<StreamMessage, long>(_coreBuilder);

        ((IEventStreamSubscriptionBuilderInfrastructure)_coreBuilder).WithSubscriptionFactory(() =>
        {
            var streamStore = _streamStoreOptions.GetOrCreateStreamStore();
            var eventAdapter = new SqlStreamStoreEventAdapter(_streamStoreOptions.GetEventDataSerializer());

            var consumers = ((IEventStreamConsumerBuilderInfrastructure<StreamMessage, long>)subscribersBuilder)
                .Build(eventAdapter);

            return new SqlStreamStoreAllEventsStreamSubscription(consumers, streamStore);
        });

        return subscribersBuilder;
    }

    public EventStreamConsumerBuilder<StreamMessage, int> FromEventStream(string streamName)
    {
        throw new NotImplementedException();
    }
}