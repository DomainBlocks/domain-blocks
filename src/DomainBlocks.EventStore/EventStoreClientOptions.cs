using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public class EventStoreClientOptions<TEventBase, TSerialized> where TEventBase : class where TSerialized : notnull
{
    public required Func<CancellationToken, ValueTask<IEventStoreClientAdapter<TSerialized>>> AdapterFactory
    {
        get;
        init;
    }

    public required EventTypeMap TypeMap { get; init; }
    public required IPayloadSerializer<TSerialized> Serializer { get; init; }
    public IEnumerable<IEventContractMapper<TEventBase>> ContractMappers { get; init; } = [];
}