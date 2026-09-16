using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.Codecs;

public sealed class EventCodecOptions<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    /// <summary>
    /// The mapping between event CLR types and their stored names. Types that are contract-mapped are registered by
    /// their contract type.
    /// </summary>
    public required EventTypeMap TypeMap { get; init; }

    public required IObjectSerializer<TEventData> EventSerializer { get; init; }

    public required IMetadataSerializer<TMetadata> MetadataSerializer { get; init; }

    /// <summary>
    /// Mappers between domain events and the contract types that are actually serialized, e.g. Protobuf messages.
    /// </summary>
    public IEnumerable<IEventContractMapper<TEvent>> ContractMappers { get; init; } = [];
}