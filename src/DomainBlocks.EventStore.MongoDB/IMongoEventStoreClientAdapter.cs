using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStoreClientAdapter<TEventData, TMetadata> : IEventStoreClientAdapter<TEventData, TMetadata>
    where TEventData : notnull
    where TMetadata : notnull;