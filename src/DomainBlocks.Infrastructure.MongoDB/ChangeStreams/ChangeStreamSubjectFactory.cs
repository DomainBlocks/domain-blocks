using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public static class ChangeStreamSubjectFactory
{
    public static async Task<ChangeStreamSubject<TDocument, TResult>> CreateAsync<TDocument, TResult>(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        Func<TResult, BsonDocument> resumeTokenSelector,
        ChangeStreamSubjectOptions? options = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChangeStreamSubjectOptions();

        var resilientCursorFactory = cursorFactory.WithResilience(
            options.MaxRetryAttempts,
            options.MaxRetryDelay,
            logger);

        // If no resume option has been provided, establish an anchor to prevent missed notifications. The anchor
        // captures the current change stream position so a consumer can obtain the subject, perform catch-up work, and
        // later connect to process live changes without gaps. This avoids the need to internally buffer.
        if (!options.MongoOptions.HasResumeOption)
        {
            var cursor = await resilientCursorFactory(pipeline, options.MongoOptions, cancellationToken);

            if (!await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "The change stream cursor completed before an anchor resume token could be established.");
            }

            // Don't modify the provided options.
            options = options.Copy();

            options.MongoOptions.ResumeAfter = cursor.GetResumeToken() ?? throw new InvalidOperationException(
                "The change stream cursor did not provide a resume token for anchoring.");
        }

        return new ChangeStreamSubject<TDocument, TResult>(
            resilientCursorFactory,
            pipeline,
            resumeTokenSelector,
            options,
            logger);
    }
}