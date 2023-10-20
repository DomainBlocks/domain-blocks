using DomainBlocks.Core.Subscriptions;
using EventStore.Client;
using EventStoreSubscriptionDroppedReason = EventStore.Client.SubscriptionDroppedReason;
using SubscriptionDroppedReason = DomainBlocks.Core.Subscriptions.SubscriptionDroppedReason;

namespace DomainBlocks.EventStore.Subscriptions;

public sealed class EventStoreAllEventsStreamSubscription : EventStreamSubscriptionBase<ResolvedEvent, Position>
{
    private readonly EventStoreClient _eventStoreClient;
    private readonly UserCredentials _userCredentials;

    public EventStoreAllEventsStreamSubscription(
        IEnumerable<IEventStreamSubscriber<ResolvedEvent, Position>> subscribers,
        EventStoreClient eventStoreClient,
        UserCredentials? userCredentials = null) : base(subscribers)
    {
        _eventStoreClient = eventStoreClient;
        _userCredentials = userCredentials ?? new UserCredentials("admin", "changeit");
    }

    protected override async Task<IDisposable> Subscribe(
        Position? fromPositionExclusive,
        CancellationToken cancellationToken)
    {
        await NotifyCatchingUp();

        var position = fromPositionExclusive ?? Position.Start;

        if (position != Position.End)
        {
            var isFirstEventRequired = fromPositionExclusive == null;
            var skipCount = isFirstEventRequired ? 0 : 1;
            var historicEvents = ReadAllEvents(position, cancellationToken).Skip(skipCount);

            await foreach (var @event in historicEvents.WithCancellation(cancellationToken))
            {
                await NotifyEvent(@event, @event.Event.Position, cancellationToken);
                position = @event.Event.Position;
            }
        }

        var filter = new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents());

        var subscription = await _eventStoreClient.SubscribeToAllAsync(
            position,
            (_, e, ct) => NotifyEvent(e, e.Event.Position, ct),
            subscriptionDropped: (_, r, ex) =>
            {
                var reason = GetSubscriptionDroppedReason(r);
                NotifySubscriptionDropped(reason, ex);
            },
            filterOptions: filter,
            userCredentials: _userCredentials,
            cancellationToken: cancellationToken);

        await NotifyLive();

        return subscription;
    }

    private IAsyncEnumerable<ResolvedEvent> ReadAllEvents(Position position, CancellationToken cancellationToken)
    {
        return _eventStoreClient
            .ReadAllAsync(
                Direction.Forwards,
                position,
                userCredentials: _userCredentials,
                cancellationToken: cancellationToken)
            .Where(x => !x.Event.EventType.StartsWith('$'));
    }

    private static SubscriptionDroppedReason GetSubscriptionDroppedReason(
        EventStoreSubscriptionDroppedReason r) => r switch
    {
        EventStoreSubscriptionDroppedReason.Disposed => SubscriptionDroppedReason.Disposed,
        EventStoreSubscriptionDroppedReason.SubscriberError => SubscriptionDroppedReason.SubscriberError,
        EventStoreSubscriptionDroppedReason.ServerError => SubscriptionDroppedReason.ServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, null)
    };
}