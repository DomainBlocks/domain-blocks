using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.KurrentDB;

public interface IKurrentDbEventStoreAdapter : IEventStoreAdapter<ReadOnlyMemory<byte>>;