using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public sealed class EventEncoder<TEvent, TEventData, TMetadata> : IEventEncoder<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull
{
    private readonly EventTypeMap _eventTypeMap;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEvent>> _contractMappers;
    private readonly IMetadataContributor<TEvent>[] _metadataContributors;
    private readonly IObjectSerializer<TEventData> _eventSerializer;
    private readonly IMetadataSerializer<TMetadata> _metadataSerializer;
    private readonly Dictionary<string, string> _metadataBuffer = [];

    internal EventEncoder(
        EventTypeMap eventTypeMap,
        FrozenDictionary<Type, IEventContractMapper<TEvent>> contractMappers,
        IMetadataContributor<TEvent>[] metadataContributors,
        IObjectSerializer<TEventData> eventSerializer,
        IMetadataSerializer<TMetadata> metadataSerializer)
    {
        _eventTypeMap = eventTypeMap;
        _contractMappers = contractMappers;
        _metadataContributors = metadataContributors;
        _eventSerializer = eventSerializer;
        _metadataSerializer = metadataSerializer;
    }

    public EncodedEvent<TEventData, TMetadata> Encode(AppendEvent<TEvent> appendEvent)
    {
        var @event = appendEvent.Event;
        object? contract = null;
        string eventName;

        if (_contractMappers.TryGetValue(@event.GetType(), out var contractMapper))
        {
            contract = contractMapper.ToContract(@event);
            eventName = _eventTypeMap.GetEventName(contractMapper.ContractType);
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