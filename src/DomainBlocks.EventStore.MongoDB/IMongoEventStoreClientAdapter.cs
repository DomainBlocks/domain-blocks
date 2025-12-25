using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStoreClientAdapter<TSerialized> : IEventStoreClientAdapter<TSerialized>
    where TSerialized : notnull;