using DomainBlocks.Core;
using DomainBlocks.Core.Projections;
using EventStore.Client;
using Microsoft.Extensions.Logging;
using EventRecord = EventStore.Client.EventRecord;

namespace DomainBlocks.EventStore.Projections;

public class AcknowledgingEventStoreEventPublisher : IEventPublisher<EventRecord>, IDisposable
{
    private static readonly ILogger<AcknowledgingEventStoreEventPublisher> Log =
        Logger.CreateFor<AcknowledgingEventStoreEventPublisher>();

    private readonly EventStorePersistentSubscriptionsClient _client;
    private Func<EventNotification<EventRecord>, CancellationToken, Task>? _onEvent;
    private readonly EventStorePersistentConnectionDescriptor _persistentConnectionDescriptor;
    private PersistentSubscription? _subscription;
    private readonly EventStoreDroppedSubscriptionHandler _subscriptionDroppedHandler;

    public AcknowledgingEventStoreEventPublisher(
        EventStorePersistentSubscriptionsClient client,
        EventStorePersistentConnectionDescriptor persistentConnectionDescriptor)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _persistentConnectionDescriptor = persistentConnectionDescriptor;
        _subscriptionDroppedHandler = new EventStoreDroppedSubscriptionHandler(Stop, ReSubscribeAfterDrop);
    }

    public async Task StartAsync(
        Func<EventNotification<EventRecord>, CancellationToken, Task> onEvent,
        IStreamPosition? position = null,
        CancellationToken cancellationToken = default)
    {
        _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
        await SubscribeToPersistentSubscription(cancellationToken);
    }

    public void Stop()
    {
        Dispose();
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }

    private async Task SubscribeToPersistentSubscription(CancellationToken cancellationToken = default)
    {
        _subscription = await _client.SubscribeAsync(
            _persistentConnectionDescriptor.Stream,
            _persistentConnectionDescriptor.GroupName,
            HandleEvent,
            OnSubscriptionDropped,
            _persistentConnectionDescriptor.UserCredentials,
            _persistentConnectionDescriptor.BufferSize,
            false,
            cancellationToken);
    }

    private async Task HandleEvent(
        PersistentSubscription subscription,
        ResolvedEvent resolvedEvent,
        int? retryCount,
        CancellationToken cancellationToken = default)
    {
        // TODO: I'm not sure awaiting here is the best move if we want to utilize the ES buffer properly
        // It might be better to return straight away and then ack/nack as and when we finish processing
        // This needs some more investigation
        await TryHandlingEvent(subscription, resolvedEvent, 0, cancellationToken: cancellationToken);
    }

    private async Task TryHandlingEvent(
        PersistentSubscription subscription,
        ResolvedEvent resolvedEvent,
        int retryNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _onEvent!(resolvedEvent.ToEventNotification(), cancellationToken);
            await subscription.Ack(resolvedEvent);
            Log.LogTrace("Handled and acknowledged event {EventId}", resolvedEvent.Event.EventId);
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Failed to handle event {EventId}", resolvedEvent.Event.EventId);
            try
            {
                await RetryHandlingEventOrFail(subscription, resolvedEvent, retryNumber, cancellationToken);
            }
            catch (Exception ex2)
            {
                Log.LogCritical(
                    ex2,
                    "Failed while trying to handle the failure case for event {EventId}. Stopping persistent " +
                    "subscription for stream {StreamName} and group {GroupName}",
                    resolvedEvent.Event.EventId,
                    _persistentConnectionDescriptor.Stream,
                    _persistentConnectionDescriptor.GroupName);

                await subscription.Nack(
                    PersistentSubscriptionNakEventAction.Stop,
                    "Stopping subscription after unhandled exception trying to process event",
                    resolvedEvent);

                throw;
            }
        }
    }

    private async Task RetryHandlingEventOrFail(
        PersistentSubscription subscription,
        ResolvedEvent resolvedEvent,
        int retryNumber,
        CancellationToken cancellationToken = default)
    {
        var retrySettings = _persistentConnectionDescriptor.RetrySettings;
        if (retryNumber <= retrySettings.MaxRetryCount)
        {
            var nextRetryNumber = retryNumber + 1;
            var delay = retrySettings.GetRetryDelay(nextRetryNumber);

            Log.LogInformation(
                "Retrying event {EventId}. Retry number {RetryNumber}. Delaying for {RetryDelay} before trying again",
                resolvedEvent.Event.EventId,
                nextRetryNumber,
                delay);

            await Task.Delay(delay, cancellationToken);

            // Recurse back into TryHandlingEvent with the retry number incremented
            await TryHandlingEvent(subscription, resolvedEvent, nextRetryNumber, cancellationToken);
        }
        else
        {
            var (actionDescription, failureAction) = retrySettings.MaxRetriesFailureAction switch
            {
                MaxRetriesFailureAction.Park => ("Parking", PersistentSubscriptionNakEventAction.Park),
                MaxRetriesFailureAction.Skip => ("Skipping", PersistentSubscriptionNakEventAction.Skip),
                _ => throw new ArgumentOutOfRangeException()
            };

            var reason = $"{actionDescription} event {resolvedEvent.Event.EventId} after maximum retries reached " +
                         "and event could not be processed successfully";

            Log.LogError(
                "Could not handle event {EventId} after maximum retries. {ActionDescription} event",
                resolvedEvent.Event.EventId,
                actionDescription);

            await subscription.Nack(failureAction, reason, resolvedEvent);
        }
    }

    private void OnSubscriptionDropped(
        PersistentSubscription subscription,
        SubscriptionDroppedReason reason,
        Exception? exception)
    {
        _subscriptionDroppedHandler.HandleDroppedSubscription(reason, exception);
    }

    private async Task ReSubscribeAfterDrop(CancellationToken cancellationToken = default)
    {
        await SubscribeToPersistentSubscription(cancellationToken);
    }
}