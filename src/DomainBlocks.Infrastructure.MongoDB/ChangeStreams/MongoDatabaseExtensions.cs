using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public static class MongoDatabaseExtensions
{
    public static IChangeStreamSubscription<ChangeStreamDocument<BsonDocument>> SubscribeToChangeStream(
        this IMongoDatabase database,
        ChangeStreamSubscriptionOptions? options = null,
        ILogger? logger = null)
    {
        return database.SubscribeToChangeStream(
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            options,
            logger);
    }

    public static IChangeStreamSubscription<ChangeStreamDocument<BsonDocument>> SubscribeToChangeStream(
        this IMongoDatabase database,
        PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>> pipeline,
        ChangeStreamSubscriptionOptions? options = null,
        ILogger? logger = null)
    {
        return database.SubscribeToChangeStream(pipeline, x => x.ResumeToken, options, logger);
    }

    public static IChangeStreamSubscription<TResult> SubscribeToChangeStream<TResult>(
        this IMongoDatabase database,
        PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
        Func<TResult, BsonDocument> resumeTokenSelector,
        ChangeStreamSubscriptionOptions? options = null,
        ILogger? logger = null)
    {
        return new ChangeStreamSubscription<BsonDocument, TResult>(
            database.WatchAsync,
            pipeline,
            resumeTokenSelector,
            options,
            logger);
    }
}