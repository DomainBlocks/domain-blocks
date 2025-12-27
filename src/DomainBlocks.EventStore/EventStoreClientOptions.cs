using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public class EventStoreClientOptions<TPayload> where TPayload : notnull
{
    public required Func<CancellationToken, ValueTask<IEventStoreAdapter<TPayload>>> AdapterFactory { get; init; }
    public required EventTypeMap TypeMap { get; init; }
    public required IPayloadSerializer<TPayload> Serializer { get; init; }
    public IEnumerable<IEventContractMapper> ContractMappers { get; init; } = [];
    public IEnumerable<IEventReadTransform> ReadTransforms { get; init; } = [];
}