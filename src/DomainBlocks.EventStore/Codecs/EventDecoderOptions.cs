using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.Codecs;

public sealed class EventDecoderOptions<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    public required EventTypeMap TypeMap { get; init; }
    public required IObjectSerializer<TEventData> EventDeserializer { get; init; }
    public required IMetadataSerializer<TMetadata> MetadataDeserializer { get; init; }
    public IEnumerable<IReadEventContractMapper<TEvent>> ContractMappers { get; init; } = [];
}