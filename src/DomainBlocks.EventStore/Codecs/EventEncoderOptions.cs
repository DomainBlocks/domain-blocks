using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.Codecs;

public class EventEncoderOptions<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    public required EventTypeMap TypeMap { get; init; }
    public required IObjectSerializer<TEventData> EventSerializer { get; init; }
    public required IMetadataSerializer<TMetadata> MetadataSerializer { get; init; }
    public IEnumerable<IMetadataContributor<TEvent>> MetadataContributors { get; init; } = [];
    public IEnumerable<IAppendEventContractMapper<TEvent>> ContractMappers { get; init; } = [];
}