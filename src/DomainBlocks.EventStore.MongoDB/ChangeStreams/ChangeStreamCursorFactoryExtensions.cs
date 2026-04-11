using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal static class ChangeStreamCursorFactoryExtensions
{
    extension<TDocument, TResult>(ChangeStreamCursorFactory<TDocument, TResult> factory)
    {
        public ChangeStreamCursorFactory<TDocument, TResult> WithResilience(
            int maxRetryAttempts,
            TimeSpan maxRetryDelay,
            ILogger? logger = null)
        {
            var resiliencePipeline = GetResiliencePipeline(maxRetryAttempts, maxRetryDelay, logger);

            return async (pipeline, options, ct) => await resiliencePipeline
                .ExecuteAsync(
                    async innerCt => await factory(pipeline, options, innerCt).ConfigureAwait(false),
                    ct)
                .ConfigureAwait(false);
        }
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