using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.KurrentDB;

public interface IKurrentDBEventStoreConnectionProvider :
    IEventStoreConnectionProvider<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>,
    IAsyncDisposable;