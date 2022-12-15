using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Core;
using DomainBlocks.Core.Metadata;
using DomainBlocks.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Projections;

public sealed class EventDispatcher<TReadEvent> : IEventDispatcher
{
    private static readonly ILogger<EventDispatcher<TReadEvent>> Log = Logger.CreateFor<EventDispatcher<TReadEvent>>();

    private readonly IEventPublisher<TReadEvent> _publisher;
    private readonly EventProjectionMap _projectionMap;
    private readonly ProjectionContextMap _projectionContextMap;
    private readonly IReadEventAdapter<TReadEvent> _eventAdapter;
    private readonly IProjectionEventNameMap _projectionEventNameMap;
    private readonly EventDispatcherConfiguration _configuration;

    public EventDispatcher(
        IEventPublisher<TReadEvent> publisher,
        EventProjectionMap projectionMap,
        ProjectionContextMap projectionContextMap,
        IReadEventAdapter<TReadEvent> eventAdapter,
        IProjectionEventNameMap projectionEventNameMap,
        EventDispatcherConfiguration configuration)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _projectionMap = projectionMap ?? throw new ArgumentNullException(nameof(projectionMap));
        _projectionContextMap = projectionContextMap ?? throw new ArgumentNullException(nameof(projectionContextMap));
        _eventAdapter = eventAdapter ?? throw new ArgumentNullException(nameof(eventAdapter));
        _projectionEventNameMap =
            projectionEventNameMap ?? throw new ArgumentNullException(nameof(projectionEventNameMap));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Log.LogDebug("Starting EventStream");
        await ForAllContexts((c, ct) => c.OnInitializing(ct), cancellationToken).ConfigureAwait(false);
        Log.LogDebug("Context OnInitializing hooks called");

        // TODO (DS): Hack for now to get the position from the first projection. We'll need to get all positions here,
        // and subscribe from the lowest. To be fixed in a future PR.
        var position = await _projectionContextMap.GetAllContexts().First().OnSubscribing(cancellationToken);
        Log.LogDebug("Context OnSubscribing hooks called");

        await _publisher.StartAsync(HandleEventNotificationAsync, position, cancellationToken).ConfigureAwait(false);
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
        EventNotification<TReadEvent> notification,
        CancellationToken cancellationToken = default)
    {
        switch (notification.NotificationKind)
        {
            case EventNotificationKind.CatchingUp:
                Log.LogDebug("Received catching up notification");
                await ForAllContexts((c, ct) => c.OnCatchingUp(ct), cancellationToken).ConfigureAwait(false);
                Log.LogDebug("Context OnCatchingUp hooks called");
                break;
            case EventNotificationKind.CaughtUp:
                Log.LogDebug("Received caught up notification");
                await ForAllContexts(
                    (c, ct) => c.OnCaughtUp(notification.Position, ct), cancellationToken).ConfigureAwait(false);
                Log.LogDebug("Context OnCaughtUp hooks called");
                break;
            case EventNotificationKind.Event:
                await HandleEventAsync(
                    notification.Event,
                    notification.EventType,
                    notification.EventId,
                    notification.Position,
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task HandleEventAsync(
        TReadEvent readEvent,
        string eventType,
        Guid eventId,
        IStreamPosition position,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();

        foreach (var type in _projectionEventNameMap.GetClrTypesForEventName(eventType))
        {
            object @event;
            EventMetadata metadata;
            try
            {
                @event = await _eventAdapter.DeserializeEvent(readEvent, type, cancellationToken);
                metadata = EventMetadata.FromKeyValuePairs(_eventAdapter.DeserializeMetadata(readEvent));
            }
            catch (Exception e)
            {
                Log.LogError(e, "Exception occurred with deserializing event");
                throw;
            }

            tasks.Add(DispatchEventToProjections(@event, metadata, eventId, position, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task DispatchEventToProjections(
        object @event,
        EventMetadata metadata,
        Guid eventId,
        IStreamPosition position,
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

            if (_projectionMap.TryGetProjections(eventType, out var projections))
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                foreach (var executeAsync in projections)
                {
                    await executeAsync(@event, metadata, cancellationToken).ConfigureAwait(false);

                    // Timeout is best effort as it's only able to check after each projection finishes.
                    if (stopwatch.Elapsed >= _configuration.ProjectionHandlerTimeout)
                    {
                        Log.LogError("Timed out waiting for projections for event if {EventId}", eventId);

                        if (!_configuration.ContinueAfterTimeout)
                        {
                            _publisher.Stop();

                            throw new TimeoutException(
                                $"Stopping event stream after timeout handling event ID {eventId}");
                        }
                    }
                }

                stopwatch.Stop();
            }

            Log.LogTrace("All projections completed for event ID {EventId}", eventId);

            foreach (var projectionContext in contextsForEvent)
            {
                await projectionContext.OnEventHandled(position, cancellationToken).ConfigureAwait(false);
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
                    $"Unhandled exception in event stream handling event ID {eventId}. Stopping event publisher", ex);
            }
        }
    }
}