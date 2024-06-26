﻿using DomainBlocks.Core.Subscriptions;
using EventStore.Client;
using EventStoreSubscriptionDroppedReason = EventStore.Client.SubscriptionDroppedReason;
using SubscriptionDroppedReason = DomainBlocks.Core.Subscriptions.SubscriptionDroppedReason;

namespace DomainBlocks.EventStore.Subscriptions;

public sealed class EventStoreAllEventsStream : IEventStream<ResolvedEvent, Position>
{
    private static readonly UserCredentials DefaultUserCredentials = new("admin", "changeit");
    private readonly EventStoreClient _eventStoreClient;
    private readonly UserCredentials _userCredentials;

    public EventStoreAllEventsStream(EventStoreClient eventStoreClient, UserCredentials? userCredentials = null)
    {
        _eventStoreClient = eventStoreClient;
        _userCredentials = userCredentials ?? DefaultUserCredentials;
    }

    public async Task<IDisposable> Subscribe(
        IEventStreamSubscriber<ResolvedEvent, Position> subscriber,
        Position? fromPositionExclusive = null,
        CancellationToken cancellationToken = default)
    {
        await subscriber.OnCatchingUp(cancellationToken);

        var position = fromPositionExclusive ?? Position.Start;

        if (position != Position.End)
        {
            var isFirstEventRequired = fromPositionExclusive == null;
            var skipCount = isFirstEventRequired ? 0 : 1;
            var historicEvents = ReadAllEvents(position, cancellationToken).Skip(skipCount);

            await foreach (var @event in historicEvents.WithCancellation(cancellationToken))
            {
                await subscriber.OnEvent(@event, @event.Event.Position, cancellationToken);
                position = @event.Event.Position;
            }
        }

        var filter = new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents());

#pragma warning disable CS0618 // Type or member is obsolete

        // TODO: Upgrade to new method
        var subscription = await _eventStoreClient.SubscribeToAllAsync(
            FromAll.After(position),
            (_, e, ct) => subscriber.OnEvent(e, e.Event.Position, ct),
            subscriptionDropped: (_, r, ex) =>
            {
                var reason = GetSubscriptionDroppedReason(r);
                var task = Task.Run(
                    () => subscriber.OnSubscriptionDropped(reason, ex, cancellationToken), cancellationToken);
                task.Wait(cancellationToken);
            },
            filterOptions: filter,
            userCredentials: _userCredentials,
            cancellationToken: cancellationToken);

#pragma warning restore CS0618 // Type or member is obsolete

        await subscriber.OnLive(cancellationToken);

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