using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DomainBlocks.Common;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Projections
{
    public sealed class EventDispatcher<TEventBase>
    {
        private static readonly ILogger<EventDispatcher<TEventBase>> Log = Logger.CreateFor<EventDispatcher<TEventBase>>();
        private readonly IEventPublisher<TEventBase> _publisher;
        private readonly EventProjectionMap _projectionMap;
        private readonly ProjectionContextMap _projectionContextMap;
        private readonly EventDispatcherConfiguration _configuration;

        public EventDispatcher(IEventPublisher<TEventBase> publisher,
                               EventProjectionMap projectionMap,
                               ProjectionContextMap projectionContextMap,
                               EventDispatcherConfiguration configuration)
        {
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _projectionMap = projectionMap ?? throw new ArgumentNullException(nameof(projectionMap));
            _projectionContextMap = projectionContextMap ?? throw new ArgumentNullException(nameof(projectionContextMap));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public async Task StartAsync()
        {
            Log.LogDebug("Starting EventStream");
            await ForAllContexts(c => c.OnSubscribing()).ConfigureAwait(false);
            Log.LogDebug("Context OnSubscribing hooks called");

            await _publisher.StartAsync(HandleEventNotificationAsync).ConfigureAwait(false);
            Log.LogDebug("Event publisher started");
        }

        private async Task ForAllContexts(Func<IProjectionContext, Task> contextAction)
        {
            foreach (var context in _projectionContextMap.GetAllContexts())
            {
                await contextAction(context).ConfigureAwait(false); 
            }
        }

        private async Task HandleEventNotificationAsync(EventNotification<TEventBase> notification)
        {
            switch (notification.NotificationKind)
            {
                case EventNotificationKind.Event:
                    await HandleEventAsync(notification.Events)
                        .ConfigureAwait(false);
                    break;
                case EventNotificationKind.CaughtUpNotification:
                    Log.LogDebug("Received caught up notification");
                    await ForAllContexts(c => c.OnCaughtUp()).ConfigureAwait(false);
                    Log.LogDebug("Context OnCaughtUp hooks called");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task HandleEventAsync(IEnumerable<IReadEvent<TEventBase>> readEvents)
        {
            var tasks = readEvents.Select(DispatchEventToProjections);
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task DispatchEventToProjections(IReadEvent<TEventBase> readEvent)
        {
            try
            {
                Log.LogTrace("Handling event ID {EventId}", readEvent.Id);
                var eventType = readEvent.Payload.GetType();
                var contextsForEvent = _projectionContextMap.GetContextsForEventType(eventType);

                var beforeEventActions = contextsForEvent.Select(c => c.OnBeforeHandleEvent());
                Log.LogTrace("Context OnBeforeHandleEvent hooks called for event ID {EventId}", readEvent.Id);
                await Task.WhenAll(beforeEventActions).ConfigureAwait(false);

                if (_projectionMap.TryGetValue(eventType, out var projections))
                {
                    var stopwatch = new Stopwatch();
                    stopwatch.Start();

                    foreach (var (_, executeAsync) in projections)
                    {
                        await executeAsync(readEvent.Payload, readEvent.Metadata).ConfigureAwait(false);

                        // Timeout is best effort as it's only able to check
                        // after each projection finishes.
                        if (stopwatch.Elapsed >= _configuration.ProjectionHandlerTimeout)
                        {
                            Log.LogError("Timed out waiting for projections for event if {EventId}", readEvent.Id);
                            if (!_configuration.ContinueAfterTimeout)
                            {
                                _publisher.Stop();
                                throw new
                                    TimeoutException(
                                        $"Stopping event stream after timeout handling event ID {readEvent.Id}");
                            }
                        }
                    }
                    stopwatch.Stop();
                }

                Log.LogTrace("All projections completed for event ID {EventId}", readEvent.Id);

                foreach (var projectionContext in contextsForEvent)
                {
                    await projectionContext.OnAfterHandleEvent().ConfigureAwait(false);
                }
                
                Log.LogTrace("Context OnAfterHandleEvent hooks called for event ID {EventId}", readEvent.Id);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Exception occurred handling event id {EventId}.", readEvent.Id);
                if (!_configuration.ContinueAfterProjectionException)
                {
                    _publisher.Stop();
                    throw new EventStreamException("Unhandled exception in event stream handling event ID" +
                                                   $" {readEvent.Id}. Stopping event publisher",
                                                   ex);
                }
            }
        }
    }
}


