using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public class EventStoreClientOptions<TEventBase, TEventData, TMetadata>
    where TEventBase : class
    where TEventData : notnull
    where TMetadata : notnull
{
    public required IEventStoreConnectionProvider<TEventData, TMetadata> ConnectionProvider { get; init; }
    public required EventTypeMap TypeMap { get; init; }
    public required IObjectSerializer<TEventData> EventSerializer { get; init; }
    public required IMetadataSerializer<TMetadata> MetadataSerializer { get; init; }
    public IEnumerable<IMetadataContributor<TEventBase>> MetadataContributors { get; init; } = [];
    public IEnumerable<IEventContractMapper<TEventBase>> ContractMappers { get; init; } = [];
}