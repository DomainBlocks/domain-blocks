using DomainLib.Aggregates;
using DomainLib.Persistence;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainLib.Serialization
{
    public class JsonEventSerializer : IEventSerializer
    {
        private readonly JsonSerializerOptions _options;
        private readonly IEventNameMap _eventNameMap = new EventNameMap();

        public JsonEventSerializer()
        {
            _options = new JsonSerializerOptions
            {
                WriteIndented = true,
                AllowTrailingCommas = false,
            };
        }

        public JsonEventSerializer(JsonSerializerOptions options)
        {
            _options = options;
        }

        public void RegisterConverter(JsonConverter customConverter)
        {
            _options.Converters.Add(customConverter);
        }

        public void RegisterEventTypeMappings(IEventNameMap eventNameMap)
        {
            _eventNameMap.Merge(eventNameMap);
        }

        public IEventPersistenceData GetPersistenceData(object @event)
        {
            var eventName = _eventNameMap.GetEventNameForClrType(@event.GetType());
            return new JsonEventPersistenceData(Guid.NewGuid(), eventName, JsonSerializer.SerializeToUtf8Bytes(@event, _options), null);
        }

        public TEvent DeserializeEvent<TEvent>(byte[] eventData, string eventName)
        {
            var clrType = _eventNameMap.GetClrTypeForEventName(eventName);

            var evt = JsonSerializer.Deserialize(eventData, clrType, _options);

            if (evt is TEvent @event)
            {
                return @event;
            }

            var runtTimeType = typeof(TEvent);
            throw new InvalidEventTypeException($"Cannot cast event of type {eventName} to {runtTimeType.FullName}", eventName, runtTimeType.FullName);
        }
    }
}