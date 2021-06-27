using DomainLib.Aggregates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainLib.Serialization.Json
{
    public class JsonEventSerializer<TRawData> : IEventSerializer<TRawData>
    {
        private readonly JsonSerializerOptions _options;
        private readonly IEventNameMap _eventNameMap;
        private readonly IEventSerializationAdapter<TRawData> _adapter;
        private readonly JsonEventDeserializer _deserializer = new JsonEventDeserializer();
        private EventMetadataContext _metadataContext;

        public JsonEventSerializer(IEventNameMap eventNameMap, IEventSerializationAdapter<TRawData> adapter)
        {
            _eventNameMap = eventNameMap ?? throw new ArgumentNullException(nameof(eventNameMap));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _options = new JsonSerializerOptions
            {
                WriteIndented = true,
                AllowTrailingCommas = false,
            };
        }

        public JsonEventSerializer(IEventNameMap eventNameMap, IEventSerializationAdapter<TRawData> adapter, JsonSerializerOptions options)
        {
            _eventNameMap = eventNameMap ?? throw new ArgumentNullException(nameof(eventNameMap));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
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

        public IEventPersistenceData<TRawData> GetPersistenceData(object @event, string eventNameOverride = null, params KeyValuePair<string, string>[] additionalMetadata)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            var eventName = eventNameOverride ?? _eventNameMap.GetEventNameForClrType(@event.GetType());
            var eventData = _adapter.ToRawData(JsonSerializer.SerializeToUtf8Bytes(@event, _options));

            if (additionalMetadata.Length > 0 && _metadataContext == null)
            {
                _metadataContext = EventMetadataContext.CreateEmpty();
            }

            var eventMetadata = _metadataContext == null
                ? default
                : _adapter.ToRawData(JsonSerializer.SerializeToUtf8Bytes(_metadataContext.BuildMetadata(additionalMetadata), _options));

            return new JsonEventPersistenceData<TRawData>(Guid.NewGuid(),
                                                          eventName,
                                                          eventData,
                                                          eventMetadata);
        }

        public TEvent DeserializeEvent<TEvent>(TRawData eventData, string eventName, Type typeOverride = null)
        {
            var clrType = typeOverride ?? _eventNameMap.GetClrTypeForEventName(eventName);
            return _deserializer.DeserializeEvent<TEvent>(_adapter.FromRawData(eventData).Span, eventName, clrType, _options);
        }

        public Dictionary<string, string> DeserializeMetadata(TRawData rawMetadata)
        {
            if (rawMetadata == null) throw new ArgumentNullException(nameof(rawMetadata));

            var metadata = JsonSerializer.Deserialize<IList<KeyValuePair<string, string>>>(_adapter.FromRawData(rawMetadata).Span)
                                         .ToDictionary(x => x.Key, x => x.Value);
                               
            return metadata;
        }
    }
}