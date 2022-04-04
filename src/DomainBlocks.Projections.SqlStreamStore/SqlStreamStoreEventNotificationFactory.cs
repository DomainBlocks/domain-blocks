using System;
using System.Collections.Generic;
using DomainBlocks.Common;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Projections.SqlStreamStore
{
    public class SqlStreamStoreEventNotificationFactory
    {
        private static readonly ILogger<SqlStreamStoreEventNotificationFactory> Log = Logger.CreateFor<SqlStreamStoreEventNotificationFactory>();
        private readonly IProjectionEventNameMap _projectionEventNameMap;
        private readonly IEventDeserializer<StreamMessageWrapper> _deserializer;

        public SqlStreamStoreEventNotificationFactory(IProjectionEventNameMap projectionEventNameMap,
                                                      IEventDeserializer<StreamMessageWrapper> deserializer)
        {
            _projectionEventNameMap = projectionEventNameMap;
            _deserializer = deserializer;
        }

        public EventNotification<TEventBase> CreateEventNotification<TEventBase>(StreamMessageWrapper streamMessage)
        {
            var events = new List<IReadEvent<TEventBase>>();

            foreach (var clrType in _projectionEventNameMap.GetClrTypesForEventName(streamMessage.Type))
            {
                try
                {
                    events.Add(_deserializer.DeserializeEventAndMetadata<TEventBase>(streamMessage,
                                                                                     streamMessage.Type,
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