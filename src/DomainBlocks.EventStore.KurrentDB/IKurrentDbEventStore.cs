using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.KurrentDB;

public interface IKurrentDbEventStore : IEventStoreBackend<ReadOnlyMemory<byte>>;