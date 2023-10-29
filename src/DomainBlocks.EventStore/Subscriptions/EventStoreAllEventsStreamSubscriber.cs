using DomainBlocks.Core.Subscriptions;
using EventStore.Client;
using EventStoreSubscriptionDroppedReason = EventStore.Client.SubscriptionDroppedReason;
using SubscriptionDroppedReason = DomainBlocks.Core.Subscriptions.SubscriptionDroppedReason;

namespace DomainBlocks.EventStore.Subscriptions;

public sealed class EventStoreAllEventsStreamSubscriber : IEventStreamSubscriber<ResolvedEvent, Position>
{
    private readonly EventStoreClient _eventStoreClient;
    private readonly UserCredentials _userCredentials;

    public EventStoreAllEventsStreamSubscriber(
        EventStoreClient eventStoreClient, UserCredentials? userCredentials = null)
    {
        _eventStoreClient = eventStoreClient;
        _userCredentials = userCredentials ?? new UserCredentials("admin", "changeit");
    }

    public async Task<IDisposable> Subscribe(
        Position? fromPositionExclusive,
        Func<CancellationToken, Task> onCatchingUp,
        Func<ResolvedEvent, Position, CancellationToken, Task> onEvent,
        Func<CancellationToken, Task> onLive,
        Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task> onSubscriptionDropped,
        CancellationToken cancellationToken)
    {
        await onCatchingUp(cancellationToken);

        var position = fromPositionExclusive ?? Position.Start;

        if (position != Position.End)
        {
            var isFirstEventRequired = fromPositionExclusive == null;
            var skipCount = isFirstEventRequired ? 0 : 1;
            var historicEvents = ReadAllEvents(position, cancellationToken).Skip(skipCount);

            await foreach (var @event in historicEvents.WithCancellation(cancellationToken))
            {
                await onEvent(@event, @event.Event.Position, cancellationToken);
                position = @event.Event.Position;
            }
        }

        var filter = new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents());

        var subscription = await _eventStoreClient.SubscribeToAllAsync(
            position,
            (_, e, ct) => onEvent(e, e.Event.Position, ct),
            subscriptionDropped: (_, r, ex) =>
            {
                var reason = GetSubscriptionDroppedReason(r);
                var task = Task.Run(
                    () => onSubscriptionDropped(reason, ex, cancellationToken), cancellationToken);
                task.Wait(cancellationToken);
            },
            filterOptions: filter,
            userCredentials: _userCredentials,
            cancellationToken: cancellationToken);

        await onLive(cancellationToken);

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