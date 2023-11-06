using DomainBlocks.Core.Subscriptions;
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

    public EventStreamConsumersBuilder<StreamMessage, long> FromAllEventsStream()
    {
        var consumersBuilder = new EventStreamConsumersBuilder<StreamMessage, long>(_coreBuilder);

        ((IEventStreamSubscriptionBuilderInfrastructure)_coreBuilder).WithSubscriptionFactory(() =>
        {
            var streamStore = _streamStoreOptions.GetOrCreateStreamStore();
            var eventStream = new SqlStreamStoreAllEventsStream(streamStore);
            var eventAdapter = new SqlStreamStoreEventAdapter(_streamStoreOptions.GetEventDataSerializer());

            var consumers = ((IEventStreamConsumerBuilderInfrastructure<StreamMessage, long>)consumersBuilder)
                .Build(eventAdapter);

            return new EventStreamSubscription<StreamMessage, long>(
                eventStream,
                consumers,
                new SqlStreamStorePositionComparer());
        });

        return consumersBuilder;
    }

    public EventStreamConsumersBuilder<StreamMessage, int> FromEventStream(string streamName)
    {
        throw new NotImplementedException();
    }
}