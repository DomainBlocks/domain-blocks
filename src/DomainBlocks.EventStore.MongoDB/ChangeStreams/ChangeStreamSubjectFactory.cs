using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal static class ChangeStreamSubjectFactory
{
    public static async Task<IChangeStreamSubject<TResult>> CreateAsync<TDocument, TResult>(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        Func<TResult, BsonDocument> resumeTokenSelector,
        ChangeStreamSubjectOptions? options = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChangeStreamSubjectOptions();
        cursorFactory = AddResilience(cursorFactory, options.MaxRetryAttempts, options.MaxRetryDelay, logger);

        // If no resume option has been provided, establish an anchor to prevent missed notifications. The anchor
        // captures the current change stream position so a consumer can obtain the subject, perform catch-up work, and
        // later connect to process live changes without gaps. This avoids the need to internally buffer.
        if (!HasResumeOption(options.MongoOptions))
        {
            using var cursor = await cursorFactory(pipeline, options.MongoOptions, cancellationToken);

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
            cursorFactory,
            pipeline,
            resumeTokenSelector,
            options,
            logger);
    }

    private static bool HasResumeOption(ChangeStreamOptions options) =>
        options.ResumeAfter is not null ||
        options.StartAfter is not null ||
        options.StartAtOperationTime is not null;

    private static ChangeStreamCursorFactory<TDocument, TResult> AddResilience<TDocument, TResult>(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
        int maxRetryAttempts,
        TimeSpan maxRetryDelay,
        ILogger? logger = null)
    {
        var resiliencePipeline = GetResiliencePipeline(maxRetryAttempts, maxRetryDelay, logger);

        return async (pipeline, options, ct) => await resiliencePipeline
            .ExecuteAsync(
                async innerCt => await cursorFactory(pipeline, options, innerCt).ConfigureAwait(false),
                ct)
            .ConfigureAwait(false);
    }

    private static ResiliencePipeline GetResiliencePipeline(
        int maxRetryAttempts,
        TimeSpan maxRetryDelay,
        ILogger? logger)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxDelay = maxRetryDelay,
                ShouldHandle = args =>
                {
                    var ex = args.Outcome.Exception;
                    var shouldRetry = ex is not null && ChangeStreamResumePolicy.CanResume(ex);
                    return ValueTask.FromResult(shouldRetry);
                },
                OnRetry = args =>
                {
                    var ex = args.Outcome.Exception;
                    var attempt = args.AttemptNumber + 1;
                    var delay = args.RetryDelay;

                    logger?.LogWarning(
                        ex,
                        "Attempt {Attempt}: Failed to connect; will retry in {Delay}",
                        attempt,
                        delay);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}