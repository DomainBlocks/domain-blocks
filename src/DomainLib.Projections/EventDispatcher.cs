using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DomainLib.Common;
using DomainLib.Serialization;
using Microsoft.Extensions.Logging;

namespace DomainLib.Projections
{
    public sealed class EventDispatcher<TEventBase>
    {
        private static readonly ILogger<EventDispatcher<TEventBase>> Log = Logger.CreateFor<EventDispatcher<TEventBase>>();
        private readonly IEventPublisher<byte[]> _publisher;
        private readonly EventProjectionMap _projectionMap;
        private readonly EventContextMap _eventContextMap;
        private readonly EventDispatcherConfiguration _configuration;
        private readonly IEventDeserializer _deserializer;
        private readonly IProjectionEventNameMap _projectionEventNameMap;

        public EventDispatcher(IEventPublisher<byte[]> publisher,
                               EventProjectionMap projectionMap,
                               EventContextMap eventContextMap,
                               IEventDeserializer serializer,
                               IProjectionEventNameMap projectionEventNameMap,
                               EventDispatcherConfiguration configuration)
        {
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _projectionMap = projectionMap ?? throw new ArgumentNullException(nameof(projectionMap));
            _eventContextMap = eventContextMap ?? throw new ArgumentNullException(nameof(eventContextMap));
            _deserializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _projectionEventNameMap =
                projectionEventNameMap ?? throw new ArgumentNullException(nameof(projectionEventNameMap));
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

        private async Task ForAllContexts(Func<IContext, Task> contextAction)
        {
            foreach (var context in _eventContextMap.GetAllContexts())
            {
                await contextAction(context).ConfigureAwait(false); 
            }
        }

        private async Task HandleEventNotificationAsync(EventNotification<byte[]> notification)
        {
            switch (notification.NotificationKind)
            {
                case EventNotificationKind.Event:
                    await HandleEventAsync(notification.Event, notification.EventType, notification.EventId)
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

        private async Task HandleEventAsync(byte[] eventData, string eventType, Guid eventId)
        {
            var tasks = new List<Task>();

            foreach (var type in _projectionEventNameMap.GetClrTypesForEventName(eventType))
            {
                TEventBase @event;
                try
                {
                    @event = _deserializer.DeserializeEvent<TEventBase>(eventData, eventType, type);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }

                tasks.Add(DispatchEventToProjections(@event, eventId));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task DispatchEventToProjections(TEventBase @event, Guid eventId)
        {
            try
            {
                Log.LogTrace("Handling event ID {EventId}", eventId);
                var eventType = @event.GetType();
                var contextsForEvent = _eventContextMap.GetContextsForEventType(eventType);

                var beforeEventActions = contextsForEvent.Select(c => c.OnBeforeHandleEvent());
                Log.LogTrace("Context OnBeforeHandleEvent hooks called for event ID {EventId}", eventId);
                await Task.WhenAll(beforeEventActions).ConfigureAwait(false);

                if (_projectionMap.TryGetValue(eventType, out var projections))
                {
                    var tasks = new List<Task>();
                    var timeoutTask = Task.Delay(_configuration.ProjectionHandlerTimeout);

                    foreach (var (_, executeAsync) in projections)
                    {
                        tasks.Add(executeAsync(@event));
                    }

                    var projectionsTask = Task.WhenAll(tasks);
                    await Task.WhenAny(timeoutTask, projectionsTask).ConfigureAwait(false);

                    if (timeoutTask.IsCompleted)
                    {
                        Log.LogError("Timed out waiting for projections for event if {EventId}", eventId);
                        if (!_configuration.ContinueAfterTimeout)
                        {
                            _publisher.Stop();
                            throw new
                                TimeoutException($"Stopping event stream after timeout handling event ID {eventId}");
                        }
                    }
                }

                Log.LogTrace("Run all projections for event ID {EventId}", eventId);

                var afterEventActions = contextsForEvent.Select(c => c.OnAfterHandleEvent());
                await Task.WhenAll(afterEventActions).ConfigureAwait(false);
                Log.LogTrace("Context OnAfterHandleEvent hooks called for event ID {EventId}", eventId);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Exception occurred handling event id {EventId}.", eventId);
                if (!_configuration.ContinueAfterProjectionException)
                {
                    _publisher.Stop();
                    throw new EventStreamException("Unhandled exception in event stream handling event ID" +
                                                   $" {eventId}. Stopping event publisher",
                                                   ex);
                }
            }
        }
    }
}


