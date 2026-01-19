using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public sealed class EventCodecOptions<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    public required EventTypeMap TypeMap { get; init; }
    public required IObjectSerde<TEventData> EventSerde { get; init; }
    public required IMetadataSerde<TMetadata> MetadataSerde { get; init; }
    public IEnumerable<IMetadataContributor<TEvent>> MetadataContributors { get; init; } = [];
    public IEnumerable<IEventContractMapper<TEvent>> ContractMappers { get; init; } = [];
}