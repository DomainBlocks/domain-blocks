using System;
using System.Collections.Generic;
using DomainBlocks.Common;
using DomainBlocks.Serialization;
using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Projections.EventStore
{
    public interface IEventNotificationFactory<in TRawData>
    {
        EventNotification<TEventBase> CreateEventNotification<TEventBase>(TRawData resolvedEvent);
    }

    public class EventStoreEventNotificationFactory : IEventNotificationFactory<ResolvedEvent>
    {
        private static readonly ILogger<EventStoreEventNotificationFactory> Log = Logger.CreateFor<EventStoreEventNotificationFactory>();
        private readonly IProjectionEventNameMap _projectionEventNameMap;
        private readonly IEventDeserializer<ResolvedEvent> _deserializer;

        public EventStoreEventNotificationFactory(
            IProjectionEventNameMap projectionEventNameMap,
            IEventDeserializer<ResolvedEvent> deserializer)
        {
            _projectionEventNameMap = projectionEventNameMap ?? throw new ArgumentNullException(nameof(projectionEventNameMap));
            _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
        }

        public EventNotification<TEventBase> CreateEventNotification<TEventBase>(ResolvedEvent resolvedEvent)
        {
            var events = new List<IReadEvent<TEventBase>>();

            foreach (var clrType in _projectionEventNameMap.GetClrTypesForEventName(resolvedEvent.Event.EventType))
            {
                try
                {
                    events.Add(_deserializer.DeserializeEventAndMetadata<TEventBase>(resolvedEvent,
                                                                                     resolvedEvent.Event.EventType,
                                                                                     clrType));
                }
                catch (Exception e)
                {
                    Log.LogError(e, "Exception occurred with deserializing event");
                    throw;
                }
            }

            return EventNotification.FromEvents(events);
        }
    }
}