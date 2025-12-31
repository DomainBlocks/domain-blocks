using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.KurrentDB;

public interface IKurrentDBEventStoreClientAdapter :
    IEventStoreClientAdapter<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>;