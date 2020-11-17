using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using DomainLib.Aggregates;

namespace DomainLib.Serialization.Json
{
    public class JsonEventSerializer : IEventSerializer
    {
        private readonly JsonSerializerOptions _options;
        private readonly IEventNameMap _eventNameMap;
        private EventMetadataContext _metadataContext;

        public JsonEventSerializer(IEventNameMap eventNameMap)
        {
            _eventNameMap = eventNameMap ?? throw new ArgumentNullException(nameof(eventNameMap));
            _options = new JsonSerializerOptions
            {
                WriteIndented = true,
                AllowTrailingCommas = false,
            };
        }

        public JsonEventSerializer(IEventNameMap eventNameMap, JsonSerializerOptions options)
        {
            _eventNameMap = eventNameMap ?? throw new ArgumentNullException(nameof(eventNameMap));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void RegisterConverter(JsonConverter customConverter)
        {
            if (customConverter == null) throw new ArgumentNullException(nameof(customConverter));
            _options.Converters.Add(customConverter);
        }

        public void UseMetaDataContext(EventMetadataContext metadataContext)
        {
            _metadataContext = metadataContext ?? throw new ArgumentNullException(nameof(metadataContext));
        }

        public IEventPersistenceData GetPersistenceData(object @event, params KeyValuePair<string, string>[] additionalMetadata)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            var eventName = _eventNameMap.GetEventNameForClrType(@event.GetType());
            var eventData = JsonSerializer.SerializeToUtf8Bytes(@event, _options);
            var eventMetadata = _metadataContext == null
                ? null
                : JsonSerializer.SerializeToUtf8Bytes(_metadataContext.BuildMetadata(additionalMetadata), _options);

            return new JsonEventPersistenceData(Guid.NewGuid(),
                                                eventName,
                                                eventData,
                                                eventMetadata);
        }

        public TEvent DeserializeEvent<TEvent>(byte[] eventData, string eventName)
        {
            if (eventData == null) throw new ArgumentNullException(nameof(eventData));
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));

            var clrType = _eventNameMap.GetClrTypeForEventName(eventName);
            var evt = JsonSerializer.Deserialize(eventData, clrType, _options);

            if (evt is TEvent @event)
            {
                return @event;
            }

            var runtTimeType = typeof(TEvent);
            throw new InvalidEventTypeException($"Cannot cast event of type {eventName} to {runtTimeType.FullName}", eventName, runtTimeType.FullName);
        }

        public Dictionary<string, string> DeserializeMetadata(byte[] metadataBytes)
        {
            if (metadataBytes == null) throw new ArgumentNullException(nameof(metadataBytes));

            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataBytes);
            return metadata;
        }
    }
}