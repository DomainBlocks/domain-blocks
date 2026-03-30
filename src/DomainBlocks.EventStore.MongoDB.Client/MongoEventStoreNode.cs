// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Logging.Abstractions;
// using MongoDB.Driver;
//
// namespace DomainBlocks.EventStore.MongoDB.Client;
//
// public interface IMongoEventStoreNode
// {
// }
//
// public class MongoEventStoreNode : IMongoEventStoreNode, IAsyncDisposable
// {
//     private MongoEventStoreNode()
//     {
//     }
//
//     public static async Task<MongoEventStoreNode> CreateAsync(
//         IMongoClient mongoClient,
//         MongoEventStoreOptions? options = null,
//         ILoggerFactory? loggerFactory = null,
//         CancellationToken cancellationToken = default)
//     {
//         options ??= new MongoEventStoreOptions();
//         loggerFactory ??= NullLoggerFactory.Instance;
//     }
//
//     public ValueTask DisposeAsync()
//     {
//         throw new NotImplementedException();
//     }
// }