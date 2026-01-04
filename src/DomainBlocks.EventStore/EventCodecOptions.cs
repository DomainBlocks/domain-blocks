using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public class EventCodecOptions<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    public required EventTypeMap TypeMap { get; init; }
    public required IObjectSerializer<TEventData> EventSerializer { get; init; }
    public required IMetadataSerializer<TMetadata> MetadataSerializer { get; init; }
    public IEnumerable<IMetadataContributor<TEvent>> MetadataContributors { get; init; } = [];
    public IEnumerable<IEventContractMapper<TEvent>> ContractMappers { get; init; } = [];
}