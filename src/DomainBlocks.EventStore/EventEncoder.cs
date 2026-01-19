using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;

namespace DomainBlocks.EventStore;

public sealed class EventEncoder<TEvent, TEventData, TMetadata>(
    EventEncoderOptions<TEvent, TEventData, TMetadata> options) :
    IEventEncoder<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull
{
    private readonly IMetadataContributor<TEvent>[] _metadataContributors = options.MetadataContributors.ToArray();

    private readonly FrozenDictionary<Type, IAppendEventContractMapper<TEvent>> _contractMappers =
        options.ContractMappers.ToFrozenDictionary(x => x.EventType);

    public IEventEncodingSession<TEvent, TEventData, TMetadata> CreateSession()
    {
        return new EventEncodingSession<TEvent, TEventData, TMetadata>(
            options.TypeMap,
            options.EventSerializer,
            options.MetadataSerializer,
            _metadataContributors,
            _contractMappers);
    }
}