using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public sealed class EventDecoderOptions<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    public required ReadEventTypeMap TypeMap { get; init; }
    public required IObjectDeserializer<TEventData> EventDeserializer { get; init; }
    public required IMetadataDeserializer<TMetadata> MetadataDeserializer { get; init; }
    public IEnumerable<IReadEventContractMapper<TEvent>> ContractMappers { get; init; } = [];
}