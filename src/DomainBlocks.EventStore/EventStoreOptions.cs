using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public class EventStoreOptions<TPayload>
{
    public required IEventStoreBackend<TPayload> Backend { get; init; }
    public required EventTypeMap TypeMap { get; init; }
    public required IPayloadSerializer<TPayload> Serializer { get; init; }
    public IEnumerable<IEventContractMapper> ContractMappers { get; init; } = [];
    public IEnumerable<IEventReadTransform> ReadTransforms { get; init; } = [];
}