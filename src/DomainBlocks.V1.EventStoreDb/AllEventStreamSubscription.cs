using System.Runtime.CompilerServices;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Subscriptions;
using DomainBlocks.V1.Abstractions.Subscriptions.Messages;
using DomainBlocks.V1.EventStoreDb.Extensions;
using EventStore.Client;

namespace DomainBlocks.V1.EventStoreDb;

public class AllEventStreamSubscription : IEventStreamSubscription
{
    private readonly EventStoreClient.StreamSubscriptionResult _subscription;

    public AllEventStreamSubscription(EventStoreClient eventStoreClient, GlobalPosition? afterPosition)
    {
        var positionAsLong = afterPosition?.ToUInt64();

        Position? eventStoreDbPosition =
            positionAsLong == null ? null : new Position(positionAsLong.Value, positionAsLong.Value);

        var fromAll = eventStoreDbPosition == null ? FromAll.Start : FromAll.After(eventStoreDbPosition.Value);

        _subscription = eventStoreClient.SubscribeToAll(
            fromAll,
            filterOptions: new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents()));
    }

    public void Dispose() => _subscription.Dispose();

    public async IAsyncEnumerable<ISubscriptionMessage> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _subscription.Messages.WithCancellation(cancellationToken))
        {
            switch (message)
            {
                case StreamMessage.Event e:
                    var eventRecord = e.ResolvedEvent.ToStoredEventRecord();
                    var position = new SubscriptionPosition(eventRecord.GlobalPosition.ToUInt64());
                    yield return new EventReceived(eventRecord, position);
                    break;
            }
        }
    }
}