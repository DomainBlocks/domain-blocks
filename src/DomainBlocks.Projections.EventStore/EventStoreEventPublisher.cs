using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;

namespace DomainBlocks.Projections.EventStore;

public class EventStoreEventPublisher : IEventPublisher<EventRecord>, IDisposable
{
    private readonly EventStoreClient _client;
    private Func<EventNotification<EventRecord>, CancellationToken, Task> _onEvent;
    private StreamSubscription _subscription;
    private readonly EventStoreDroppedSubscriptionHandler _subscriptionDroppedHandler;
    private Position _lastProcessedPosition;

    public EventStoreEventPublisher(EventStoreClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _subscriptionDroppedHandler = new EventStoreDroppedSubscriptionHandler(
            Stop,
            ReSubscribeAfterDrop);
    }

    public async Task StartAsync(
        Func<EventNotification<EventRecord>, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
        await SubscribeToEventStore(Position.Start, cancellationToken);
    }

    public void Stop()
    {
        Dispose();
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }

    private async Task SubscribeToEventStore(Position position, CancellationToken cancellationToken = default)
    {
        async Task SendEventNotification(ResolvedEvent resolvedEvent)
        {
            await _onEvent(resolvedEvent.ToEventNotification(), cancellationToken);

            if (resolvedEvent.OriginalPosition.HasValue)
            {
                _lastProcessedPosition = resolvedEvent.OriginalPosition.Value;
            }
        }

        var historicEvents = _client.ReadAllAsync(Direction.Forwards, position, cancellationToken: cancellationToken);

        await foreach (var historicEvent in historicEvents.WithCancellation(cancellationToken))
        {
            await SendEventNotification(historicEvent);
        }

        await _onEvent(EventNotification.CaughtUp<EventRecord>(), cancellationToken);

        _subscription = await _client.SubscribeToAllAsync(
            _lastProcessedPosition,
            (_, evt, _) => SendEventNotification(evt),
            false,
            OnSubscriptionDropped,
            userCredentials: new UserCredentials("admin", "changeit"),
            cancellationToken: cancellationToken);
    }

    private void OnSubscriptionDropped(StreamSubscription subscription, SubscriptionDroppedReason reason, Exception exception)
    {
        _subscriptionDroppedHandler.HandleDroppedSubscription(reason, exception);
    }

    private async Task ReSubscribeAfterDrop(CancellationToken cancellationToken = default)
    {
        await SubscribeToEventStore(_lastProcessedPosition, cancellationToken);
    }
}