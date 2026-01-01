using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStoreConnectionProvider<TEventData, TMetadata> :
    IEventStoreConnectionProvider<TEventData, TMetadata>,
    IAsyncDisposable
    where TEventData : notnull
    where TMetadata : notnull;