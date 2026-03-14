using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public static class MongoDatabaseExtensions
{
    extension(IMongoDatabase database)
    {
        public IChangeStreamSubscription<ChangeStreamDocument<BsonDocument>> SubscribeToChangeStream(
            ChangeStreamSubscriptionOptions? options = null,
            ILogger? logger = null)
        {
            return database.SubscribeToChangeStream(
                new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
                options,
                logger);
        }

        public IChangeStreamSubscription<ChangeStreamDocument<BsonDocument>> SubscribeToChangeStream(
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>> pipeline,
            ChangeStreamSubscriptionOptions? options = null,
            ILogger? logger = null)
        {
            return database.SubscribeToChangeStream(pipeline, x => x.ResumeToken, options, logger);
        }

        public IChangeStreamSubscription<TResult> SubscribeToChangeStream<TResult>(
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

        public Task<IChangeStreamSubject<ChangeStreamDocument<BsonDocument>>> CreateSubjectAsync(
            ChangeStreamSubjectOptions? options = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            return ChangeStreamSubjectFactory.CreateAsync(
                database.WatchAsync,
                new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
                x => x.ResumeToken,
                options,
                logger,
                cancellationToken);
        }

        public Task<IChangeStreamSubject<ChangeStreamDocument<BsonDocument>>> CreateSubjectAsync(
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>> pipeline,
            ChangeStreamSubjectOptions? options = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            return ChangeStreamSubjectFactory.CreateAsync(
                database.WatchAsync,
                pipeline,
                x => x.ResumeToken,
                options,
                logger,
                cancellationToken);
        }

        public Task<IChangeStreamSubject<TResult>> CreateSubjectAsync<TResult>(
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            Func<TResult, BsonDocument> resumeTokenSelector,
            ChangeStreamSubjectOptions? options = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            return ChangeStreamSubjectFactory.CreateAsync(
                database.WatchAsync,
                pipeline,
                resumeTokenSelector,
                options,
                logger,
                cancellationToken);
        }
    }
}