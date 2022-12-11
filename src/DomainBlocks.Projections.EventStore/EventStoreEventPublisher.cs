using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Projections.New;
using EventStore.Client;
using EventRecord = EventStore.Client.EventRecord;
using StreamPosition = DomainBlocks.Projections.New.StreamPosition;

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
        IStreamPosition position = null,
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
        // Initially signal that we're catching up.
        await _onEvent(EventNotification.CatchingUp<EventRecord>(), cancellationToken);

        async Task SendEventNotification(ResolvedEvent resolvedEvent)
        {
            await _onEvent(resolvedEvent.ToEventNotification(), cancellationToken);

            if (resolvedEvent.OriginalPosition.HasValue)
            {
                _lastProcessedPosition = resolvedEvent.OriginalPosition.Value;
            }
        }

        // TODO (DS): We need to limit the number of events we read at a time here.
        // Do we even need to do this in combination with the call to SubscribeToAllAsync?
        var historicEvents = _client.ReadAllAsync(Direction.Forwards, position, cancellationToken: cancellationToken);

        await foreach (var historicEvent in historicEvents.WithCancellation(cancellationToken))
        {
            await SendEventNotification(historicEvent);
        }

        await _onEvent(EventNotification.CaughtUp<EventRecord>(StreamPosition.Empty), cancellationToken);

        _subscription = await _client.SubscribeToAllAsync(
            _lastProcessedPosition,
            (_, evt, _) => SendEventNotification(evt),
            false,
            OnSubscriptionDropped,
            // TODO (DS): Don't hardcode credentials here. Address in a future PR.
            userCredentials: new UserCredentials("admin", "changeit"),
            cancellationToken: cancellationToken);
    }

    private void OnSubscriptionDropped(
        StreamSubscription subscription, SubscriptionDroppedReason reason, Exception exception)
    {
        _subscriptionDroppedHandler.HandleDroppedSubscription(reason, exception);
    }

    private async Task ReSubscribeAfterDrop(CancellationToken cancellationToken = default)
    {
        await SubscribeToEventStore(_lastProcessedPosition, cancellationToken);
    }
}