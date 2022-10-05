﻿using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using DomainBlocks.Core;

namespace DomainBlocks.Serialization.Json;

public class JsonEventSerializer<TRawData> : IEventSerializer<TRawData>
{
    private readonly JsonSerializerOptions _options;
    private readonly IEventNameMap _eventNameMap;
    private readonly IJsonSerializationAdapter<TRawData> _adapter;
    private EventMetadataContext _metadataContext;
        
    public JsonEventSerializer(IEventNameMap eventNameMap, IJsonSerializationAdapter<TRawData> adapter, JsonSerializerOptions options = null)
    {
        _eventNameMap = eventNameMap ?? throw new ArgumentNullException(nameof(eventNameMap));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _options = options ??
                   new JsonSerializerOptions
                   {
                       WriteIndented = true,
                       AllowTrailingCommas = false,
                   };
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

        var eventName = eventNameOverride ?? _eventNameMap.GetEventName(@event.GetType());
        var eventData = _adapter.Serialize(@event, _options);

        if (additionalMetadata.Length > 0 && _metadataContext == null)
        {
            _metadataContext = EventMetadataContext.CreateEmpty();
        }

        var eventMetadata = _metadataContext == null
            ? default
            : _adapter.Serialize(_metadataContext.BuildMetadata(additionalMetadata), _options);

        return new JsonEventPersistenceData<TRawData>(Guid.NewGuid(),
            eventName,
            eventData,
            eventMetadata);
    }

    public object DeserializeEvent(TRawData eventData, string eventName, Type typeOverride = null)
    {
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));
        if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            
        var eventType = typeOverride ?? _eventNameMap.GetEventType(eventName);

        try
        {
            var evt = _adapter.Deserialize(eventData, eventType, _options);
            return evt;
        }
        catch (Exception e)
        {
            throw new EventDeserializeException("Unable to deserialize event", e);
        }
    }

    public EventMetadata DeserializeMetadata(TRawData rawMetadata)
    {
        if (rawMetadata == null) throw new ArgumentNullException(nameof(rawMetadata));

        var metadata = _adapter.Deserialize<EventMetadata>(rawMetadata, _options);
                               
        return metadata;
    }
}