using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface ILeaderWorker : IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>, IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
}