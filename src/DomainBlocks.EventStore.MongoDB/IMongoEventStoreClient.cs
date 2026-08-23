using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStoreClient<TEvent> :
    IEventStoreClient<TEvent, string, StreamPosition, LogPosition>,
    IAsyncDisposable
    where TEvent : notnull;