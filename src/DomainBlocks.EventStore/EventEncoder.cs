using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public sealed class EventEncoder<TEventBase, TEventData, TMetadata> : IEventEncoder<TEventBase, TEventData, TMetadata>
    where TEventBase : class
    where TEventData : notnull
    where TMetadata : notnull
{
    private readonly EventTypeMap _eventTypeMap;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEventBase>> _contractMappers;
    private readonly IMetadataContributor<TEventBase>[] _metadataContributors;
    private readonly IObjectSerializer<TEventData> _eventSerializer;
    private readonly IMetadataSerializer<TMetadata> _metadataSerializer;
    private readonly Dictionary<string, string> _metadataBuffer = [];

    internal EventEncoder(
        EventTypeMap eventTypeMap,
        FrozenDictionary<Type, IEventContractMapper<TEventBase>> contractMappers,
        IMetadataContributor<TEventBase>[] metadataContributors,
        IObjectSerializer<TEventData> eventSerializer,
        IMetadataSerializer<TMetadata> metadataSerializer)
    {
        _eventTypeMap = eventTypeMap;
        _contractMappers = contractMappers;
        _metadataContributors = metadataContributors;
        _eventSerializer = eventSerializer;
        _metadataSerializer = metadataSerializer;
    }

    public EncodedAppendEvent<TEventData, TMetadata> Encode(AppendEvent<TEventBase> appendEvent)
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

        return EncodedAppendEvent.Create(eventName, serializedEventData, serializedMetadata);
    }
}