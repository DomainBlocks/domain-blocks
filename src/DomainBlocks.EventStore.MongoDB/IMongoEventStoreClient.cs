using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStoreClient<TEvent> : IEventStoreClient<TEvent>, IAsyncDisposable where TEvent : notnull;