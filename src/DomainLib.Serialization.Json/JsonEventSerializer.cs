using DomainLib.Aggregates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        public IEventPersistenceData GetPersistenceData(object @event, string eventNameOverride = null, params KeyValuePair<string, string>[] additionalMetadata)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            var eventName = eventNameOverride ?? _eventNameMap.GetEventNameForClrType(@event.GetType());
            var eventData = JsonSerializer.SerializeToUtf8Bytes(@event, _options);

            if (additionalMetadata.Length > 0 && _metadataContext == null)
            {
                _metadataContext = EventMetadataContext.CreateEmpty();
            }

            var eventMetadata = _metadataContext == null
                ? null
                : JsonSerializer.SerializeToUtf8Bytes(_metadataContext.BuildMetadata(additionalMetadata), _options);

            return new JsonEventPersistenceData(Guid.NewGuid(),
                                                eventName,
                                                eventData,
                                                eventMetadata);
        }

        public TEvent DeserializeEvent<TEvent>(byte[] eventData, string eventName, Type typeOverride = null)
        {
            if (eventData == null) throw new ArgumentNullException(nameof(eventData));
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));

            try
            {
                var clrType = typeOverride ?? _eventNameMap.GetClrTypeForEventName(eventName);
                var evt = JsonSerializer.Deserialize(eventData, clrType, _options);

                if (evt is TEvent @event)
                {
                    return @event;
                }
            }
            catch (Exception ex)
            {
                throw new EventDeserializeException("Unable to deserialize event", ex);
            }

            var runtTimeType = typeof(TEvent);
            throw new InvalidEventTypeException($"Cannot cast event of type {eventName} to {runtTimeType.FullName}", eventName, runtTimeType.FullName);
        }

        public Dictionary<string, string> DeserializeMetadata(byte[] metadataBytes)
        {
            if (metadataBytes == null) throw new ArgumentNullException(nameof(metadataBytes));

            var metadata = JsonSerializer.Deserialize<IList<KeyValuePair<string, string>>>(metadataBytes)
                                         .ToDictionary(x => x.Key, x => x.Value);
                               
            return metadata;
        }
    }
}