using System.Collections.Frozen;
using System.Runtime.InteropServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.Codecs;

public static class EventEncoder
{
    public static EventEncoder<TEvent, TEventData, TMetadata> Create<TEvent, TEventData, TMetadata>(
        EventEncoderOptions<TEvent, TEventData, TMetadata> options)
        where TEvent : notnull
        where TEventData : notnull
    {
        return new EventEncoder<TEvent, TEventData, TMetadata>(options);
    }
}

public sealed class EventEncoder<TEvent, TEventData, TMetadata>(
    EventEncoderOptions<TEvent, TEventData, TMetadata> options) :
    IEventEncoder<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull
{
    private readonly EventTypeMap _typeMap = options.TypeMap;
    private readonly IObjectSerializer<TEventData> _eventSerializer = options.EventSerializer;
    private readonly IMetadataSerializer<TMetadata> _metadataSerializer = options.MetadataSerializer;
    private readonly IMetadataContributor<TEvent>[] _metadataContributors = options.MetadataContributors.ToArray();

    private readonly FrozenDictionary<Type, IAppendEventContractMapper<TEvent>> _contractMappers =
        options.ContractMappers.ToFrozenDictionary(x => x.EventType);

    public IEnumerable<EncodedEvent<TEventData, TMetadata>> Encode(IEnumerable<AppendableEvent<TEvent>> events)
    {
        // Both buffers are reused across the batch: the dictionary de-duplicates keys, the list gives the
        // serializer a contiguous span without allocating per event.
        var metadataBuffer = new Dictionary<string, string>();
        var metadataEntries = new List<KeyValuePair<string, string>>();

        foreach (var appendEvent in events)
        {
            var @event = appendEvent.Payload;
            object? contract = null;
            string eventName;

            if (_contractMappers.TryGetValue(@event.GetType(), out var contractMapper))
            {
                contract = contractMapper.ToContract(@event);
                eventName = _typeMap.GetEventName(contract.GetType());
            }
            else
            {
                eventName = _typeMap.GetEventName(@event.GetType());
            }

            var serializedEventData = _eventSerializer.Serialize(contract ?? @event);

            metadataBuffer.Clear();
            var metadataWriter = new MetadataWriter(metadataBuffer);

            foreach (var metadataContributor in _metadataContributors)
                metadataContributor.Contribute(@event, contract, eventName, metadataWriter);

            foreach (var (key, value) in appendEvent.Metadata)
                metadataBuffer[key] = value;

            TMetadata? serializedMetadata = default;

            if (metadataBuffer.Count > 0)
            {
                metadataEntries.Clear();
                metadataEntries.AddRange(metadataBuffer);
                serializedMetadata = _metadataSerializer.Serialize(CollectionsMarshal.AsSpan(metadataEntries));
            }

            yield return EncodedEvent.Create(eventName, serializedEventData, serializedMetadata);
        }
    }
}