using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public static class MongoCollectionExtensions
{
    extension<TDocument>(IMongoCollection<TDocument> collection)
    {
        public IChangeStreamSubscription<ChangeStreamDocument<TDocument>> SubscribeToChangeStream(
            ChangeStreamSubscriptionOptions? options = null,
            ILogger? logger = null)
        {
            return collection.SubscribeToChangeStream(
                new EmptyPipelineDefinition<ChangeStreamDocument<TDocument>>(),
                options,
                logger);
        }

        public IChangeStreamSubscription<ChangeStreamDocument<TDocument>> SubscribeToChangeStream(
            PipelineDefinition<ChangeStreamDocument<TDocument>, ChangeStreamDocument<TDocument>> pipeline,
            ChangeStreamSubscriptionOptions? options = null,
            ILogger? logger = null)
        {
            return collection.SubscribeToChangeStream(pipeline, x => x.ResumeToken, options, logger);
        }

        public IChangeStreamSubscription<TResult> SubscribeToChangeStream<TResult>(
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

        public Task<IChangeStreamSubject<ChangeStreamDocument<TDocument>>> CreateSubjectAsync(
            ChangeStreamSubjectOptions? options = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            return ChangeStreamSubjectFactory.CreateAsync(
                collection.WatchAsync,
                new EmptyPipelineDefinition<ChangeStreamDocument<TDocument>>(),
                x => x.ResumeToken,
                options,
                logger,
                cancellationToken);
        }

        public Task<IChangeStreamSubject<ChangeStreamDocument<TDocument>>> CreateSubjectAsync(
            PipelineDefinition<ChangeStreamDocument<TDocument>, ChangeStreamDocument<TDocument>> pipeline,
            ChangeStreamSubjectOptions? options = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            return ChangeStreamSubjectFactory.CreateAsync(
                collection.WatchAsync,
                pipeline,
                x => x.ResumeToken,
                options,
                logger,
                cancellationToken);
        }

        public Task<IChangeStreamSubject<TResult>> CreateSubjectAsync<TResult>(
            PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
            Func<TResult, BsonDocument> resumeTokenSelector,
            ChangeStreamSubjectOptions? options = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            return ChangeStreamSubjectFactory.CreateAsync(
                collection.WatchAsync,
                pipeline,
                resumeTokenSelector,
                options,
                logger,
                cancellationToken);
        }
    }
}