using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.Subscriptions;

internal static class SubscriptionRetry
{
    /// <summary>
    /// Creates the pipeline that retries the reads that subscriptions make. A transient error is retried with
    /// exponential backoff and jitter, from <see cref="EventLogTailOptions.RetryDelay"/> up to
    /// <see cref="EventLogTailOptions.MaxRetryDelay"/>, for as long as it takes. A canceled read is not retried.
    /// </summary>
    /// <param name="isTransient">Whether an error is transient.</param>
    /// <param name="options">The retry delays.</param>
    /// <param name="onRetry">Called before each retry, with the error, the attempt that is next, and the delay.</param>
    public static ResiliencePipeline CreatePipeline(
        Func<Exception, bool> isTransient,
        EventLogTailOptions options,
        Action<Exception, int, TimeSpan> onRetry)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = int.MaxValue,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryDelay,
                MaxDelay = options.MaxRetryDelay,
                ShouldHandle = args =>
                {
                    var shouldHandle = args.Outcome.Exception is { } ex &&
                                       !args.Context.CancellationToken.IsCancellationRequested &&
                                       isTransient(ex);

                    return ValueTask.FromResult(shouldHandle);
                },
                OnRetry = args =>
                {
                    if (args.Outcome.Exception is { } ex)
                        onRetry(ex, args.AttemptNumber + 1, args.RetryDelay);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}