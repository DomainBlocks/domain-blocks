using System.Collections.Frozen;
using DomainBlocks.Core.Identity;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

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
    private readonly AppendEventTypeMap _typeMap = options.TypeMap;
    private readonly IObjectSerializer<TEventData> _eventSerializer = options.EventSerializer;
    private readonly IMetadataSerializer<TMetadata> _metadataSerializer = options.MetadataSerializer;
    private readonly IMetadataContributor<TEvent>[] _metadataContributors = options.MetadataContributors.ToArray();

    private readonly FrozenDictionary<Type, IAppendEventContractMapper<TEvent>> _contractMappers =
        options.ContractMappers.ToFrozenDictionary(x => x.EventType);

    public IEnumerable<EncodedEvent<TEventData, TMetadata>> Encode(
        Guid commitId,
        IEnumerable<AppendEvent<TEvent>> events)
    {
        var metadataBuffer = new Dictionary<string, string>();
        var eventIndex = 0;

        foreach (var appendEvent in events)
        {
            var eventId = CreateEventId(commitId, eventIndex++);

            var @event = appendEvent.Event;
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

            var serializedMetadata = metadataBuffer.Count > 0
                ? _metadataSerializer.Serialize(metadataBuffer)
                : default;

            yield return EncodedEvent.Create(eventId, eventName, serializedEventData, serializedMetadata);
        }
    }

    private static Guid CreateEventId(Guid commitId, int eventIndex)
    {
        var name = $"commit:{commitId:N}/i:{eventIndex}/v1";
        return Guid.CreateVersion5(NamespaceIds.Events, name);
    }
}