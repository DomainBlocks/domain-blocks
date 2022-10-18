using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Common;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Projections;

public interface IEventDispatcher
{
    public Task StartAsync(CancellationToken cancellationToken = default);
}

public sealed class EventDispatcher<TRawData, TEventBase> : IEventDispatcher
{
    private static readonly ILogger<EventDispatcher<TRawData, TEventBase>> Log = Logger.CreateFor<EventDispatcher<TRawData, TEventBase>>();
    private readonly IEventPublisher<TRawData> _publisher;
    private readonly EventProjectionMap _projectionMap;
    private readonly ProjectionContextMap _projectionContextMap;
    private readonly EventDispatcherConfiguration _configuration;
    private readonly IEventDeserializer<TRawData> _deserializer;
    private readonly IProjectionEventNameMap _projectionEventNameMap;

    public EventDispatcher(
        IEventPublisher<TRawData> publisher,
        EventProjectionMap projectionMap,
        ProjectionContextMap projectionContextMap,
        IEventDeserializer<TRawData> deserializer,
        IProjectionEventNameMap projectionEventNameMap,
        EventDispatcherConfiguration configuration)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _projectionMap = projectionMap ?? throw new ArgumentNullException(nameof(projectionMap));
        _projectionContextMap = projectionContextMap ?? throw new ArgumentNullException(nameof(projectionContextMap));
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
        _projectionEventNameMap =
            projectionEventNameMap ?? throw new ArgumentNullException(nameof(projectionEventNameMap));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Log.LogDebug("Starting EventStream");
        await ForAllContexts((c, ct) => c.OnInitializing(ct), cancellationToken).ConfigureAwait(false);
        Log.LogDebug("Context OnSubscribing hooks called");

        await _publisher.StartAsync(HandleEventNotificationAsync, cancellationToken).ConfigureAwait(false);
        Log.LogDebug("Event publisher started");
    }

    private async Task ForAllContexts(
        Func<IProjectionContext, CancellationToken, Task> contextAction,
        CancellationToken cancellationToken = default)
    {
        foreach (var context in _projectionContextMap.GetAllContexts())
        {
            await contextAction(context, cancellationToken).ConfigureAwait(false); 
        }
    }

    private async Task HandleEventNotificationAsync(
        EventNotification<TRawData> notification,
        CancellationToken cancellationToken = default)
    {
        switch (notification.NotificationKind)
        {
            case EventNotificationKind.Event:
                await HandleEventAsync(notification.Event, notification.EventType, notification.EventId, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case EventNotificationKind.CaughtUpNotification:
                Log.LogDebug("Received caught up notification");
                await ForAllContexts((c, ct) => c.OnCaughtUp(ct), cancellationToken).ConfigureAwait(false);
                Log.LogDebug("Context OnCaughtUp hooks called");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task HandleEventAsync(
        TRawData eventData,
        string eventType,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();

        foreach (var type in _projectionEventNameMap.GetClrTypesForEventName(eventType))
        {
            TEventBase @event;
            EventMetadata metadata;
            try
            {
                (@event, metadata) = _deserializer.DeserializeEventAndMetadata<TEventBase>(eventData, eventType, type);
            }
            catch (Exception e)
            {
                Log.LogError(e, "Exception occurred with deserializing event");
                throw;
            }

            tasks.Add(DispatchEventToProjections(@event, metadata, eventId, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task DispatchEventToProjections(
        TEventBase @event,
        EventMetadata metadata,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Log.LogTrace("Handling event ID {EventId}", eventId);
            var eventType = @event.GetType();
            var contextsForEvent = _projectionContextMap.GetContextsForEventType(eventType);

            var beforeEventActions = contextsForEvent.Select(c => c.OnEventDispatching(cancellationToken));
            Log.LogTrace("Context OnBeforeHandleEvent hooks called for event ID {EventId}", eventId);
            await Task.WhenAll(beforeEventActions).ConfigureAwait(false);

            if (_projectionMap.TryGetValue(eventType, out var projections))
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                foreach (var executeAsync in projections)
                {
                    await executeAsync(@event, metadata).ConfigureAwait(false);

                    // Timeout is best effort as it's only able to check
                    // after each projection finishes.
                    if (stopwatch.Elapsed >= _configuration.ProjectionHandlerTimeout)
                    {
                        Log.LogError("Timed out waiting for projections for event if {EventId}", eventId);
                        if (!_configuration.ContinueAfterTimeout)
                        {
                            _publisher.Stop();
                            throw new
                                TimeoutException(
                                    $"Stopping event stream after timeout handling event ID {eventId}");
                        }
                    }
                }

                stopwatch.Stop();
            }

            Log.LogTrace("All projections completed for event ID {EventId}", eventId);

            foreach (var projectionContext in contextsForEvent)
            {
                await projectionContext.OnEventHandled(cancellationToken).ConfigureAwait(false);
            }

            Log.LogTrace("Context OnAfterHandleEvent hooks called for event ID {EventId}", eventId);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Exception occurred handling event id {EventId}", eventId);
            if (!_configuration.ContinueAfterProjectionException)
            {
                _publisher.Stop();
                throw new EventStreamException(
                    "Unhandled exception in event stream handling event ID" +
                    $" {eventId}. Stopping event publisher",
                    ex);
            }
        }
    }
}