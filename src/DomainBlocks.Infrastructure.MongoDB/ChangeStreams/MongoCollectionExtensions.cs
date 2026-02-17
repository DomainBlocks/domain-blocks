using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public static class MongoCollectionExtensions
{
    public static IChangeStreamSubscription<ChangeStreamDocument<TDocument>> SubscribeToChangeStream<TDocument>(
        this IMongoCollection<TDocument> collection,
        ChangeStreamSubscriptionOptions? options = null,
        ILogger? logger = null)
    {
        return collection.SubscribeToChangeStream(
            new EmptyPipelineDefinition<ChangeStreamDocument<TDocument>>(),
            options,
            logger);
    }

    public static IChangeStreamSubscription<ChangeStreamDocument<TDocument>> SubscribeToChangeStream<TDocument>(
        this IMongoCollection<TDocument> collection,
        PipelineDefinition<ChangeStreamDocument<TDocument>, ChangeStreamDocument<TDocument>> pipeline,
        ChangeStreamSubscriptionOptions? options = null,
        ILogger? logger = null)
    {
        return collection.SubscribeToChangeStream(pipeline, x => x.ResumeToken, options, logger);
    }

    public static IChangeStreamSubscription<TResult> SubscribeToChangeStream<TDocument, TResult>(
        this IMongoCollection<TDocument> collection,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        Func<TResult, BsonDocument> resumeTokenSelector,
        ChangeStreamSubscriptionOptions? options = null,
        ILogger? logger = null)
    {
        return new ChangeStreamSubscription<TDocument, TResult>(
            collection.WatchAsync,
            pipeline,
            resumeTokenSelector,
            options,
            logger);
    }
}