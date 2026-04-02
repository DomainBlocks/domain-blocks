using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStoreNode : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);

    IEventStoreClient<TEvent> CreateClient<TEvent>(EventCodec<TEvent, BsonValue, BsonValue> eventCodec)
        where TEvent : notnull;
}