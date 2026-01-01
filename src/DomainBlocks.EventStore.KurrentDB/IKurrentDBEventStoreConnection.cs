using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.KurrentDB;

public interface IKurrentDBEventStoreConnection : IEventStoreConnection<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>;