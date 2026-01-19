using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public static class EventEncoder
{
    public static EventEncoder<TEvent, TEventData, TMetadata> CreateFactory<TEvent, TEventData, TMetadata>(
        EventEncoderOptions<TEvent, TEventData, TMetadata> options)
        where TEvent : notnull
        where TEventData : notnull
    {
        return new EventEncoder<TEvent, TEventData, TMetadata>(options);
    }
}

public sealed class EventEncodingSession<TEvent, TEventData, TMetadata> : IEventEncodingSession<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull
{
    private readonly AppendEventTypeMap _eventTypeMap;
    private readonly IObjectSerializer<TEventData> _eventSerializer;
    private readonly IMetadataSerializer<TMetadata> _metadataSerializer;
    private readonly IMetadataContributor<TEvent>[] _metadataContributors;
    private readonly FrozenDictionary<Type, IAppendEventContractMapper<TEvent>> _contractMappers;
    private readonly Dictionary<string, string> _metadataBuffer = [];

    internal EventEncodingSession(
        AppendEventTypeMap eventTypeMap,
        IObjectSerializer<TEventData> eventSerializer,
        IMetadataSerializer<TMetadata> metadataSerializer,
        IMetadataContributor<TEvent>[] metadataContributors,
        FrozenDictionary<Type, IAppendEventContractMapper<TEvent>> contractMappers)
    {
        _eventTypeMap = eventTypeMap;
        _eventSerializer = eventSerializer;
        _metadataSerializer = metadataSerializer;
        _metadataContributors = metadataContributors;
        _contractMappers = contractMappers;
    }

    public EncodedEvent<TEventData, TMetadata> Encode(AppendEvent<TEvent> appendEvent)
    {
        var @event = appendEvent.Event;
        object? contract = null;
        string eventName;

        if (_contractMappers.TryGetValue(@event.GetType(), out var contractMapper))
        {
            contract = contractMapper.ToContract(@event);
            eventName = _eventTypeMap.GetEventName(contract.GetType());
        }
        else
        {
            eventName = _eventTypeMap.GetEventName(@event.GetType());
        }

        var serializedEventData = _eventSerializer.Serialize(contract ?? @event);

        _metadataBuffer.Clear();
        var metadataWriter = new MetadataWriter(_metadataBuffer);

        foreach (var metadataContributor in _metadataContributors)
            metadataContributor.Contribute(@event, contract, eventName, metadataWriter);

        foreach (var (key, value) in appendEvent.Metadata)
            _metadataBuffer[key] = value;

        var serializedMetadata = _metadataBuffer.Count > 0
            ? _metadataSerializer.Serialize(_metadataBuffer)
            : default;

        return EncodedEvent.Create(eventName, serializedEventData, serializedMetadata);
    }
}