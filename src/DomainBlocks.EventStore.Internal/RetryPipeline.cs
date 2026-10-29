using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.Internal;

/// <summary>
/// Creates resilience pipelines that retry transient errors until an execution has been failing for a timeout.
/// </summary>
internal static class RetryPipeline
{
    /// <summary>
    /// The delay before the first retry. It doubles, with jitter, for each later retry.
    /// </summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest delay between retries.
    /// </summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMinutes(1);

    private static readonly ResiliencePropertyKey<long> FailingSinceKey = new("DomainBlocks.FailingSince");

    /// <summary>
    /// Creates a pipeline that retries transient errors, after 1 second at first, doubling the delay with jitter up to
    /// 1 minute.
    /// </summary>
    /// <param name="isTransient">
    /// Whether to retry an error. No error is retried once the caller has asked to cancel.
    /// </param>
    /// <param name="timeout">
    /// How long after its first failure an execution stops retrying and throws its last error, or
    /// <see cref="Timeout.InfiniteTimeSpan"/> to retry without a limit.
    /// </param>
    /// <param name="onRetry">Called before each delay with the error, the retry's number from 1, and the delay.</param>
    public static ResiliencePipeline Create(
        Func<Exception, bool> isTransient,
        TimeSpan timeout,
        Action<Exception, int, TimeSpan> onRetry)
    {
        return Create(isTransient, timeout, onRetry, DefaultDelay, DefaultMaxDelay, TimeProvider.System);
    }

    /// <summary>
    /// Creates a pipeline that retries transient errors with the given delays, timed by the given clock.
    /// </summary>
    public static ResiliencePipeline Create(
        Func<Exception, bool> isTransient,
        TimeSpan timeout,
        Action<Exception, int, TimeSpan> onRetry,
        TimeSpan delay,
        TimeSpan maxDelay,
        TimeProvider timeProvider)
    {
        return new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = int.MaxValue,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = delay,
                MaxDelay = maxDelay,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is not { } ex || !isTransient(ex))
                        return PredicateResult.False();

                    // Each execution has a context of its own, so the first failure recorded in it is the execution's.
                    var now = timeProvider.GetTimestamp();

                    if (!args.Context.Properties.TryGetValue(FailingSinceKey, out var failingSince))
                    {
                        args.Context.Properties.Set(FailingSinceKey, now);
                        return PredicateResult.True();
                    }

                    var isWithinTimeout = timeout == Timeout.InfiniteTimeSpan ||
                                          timeProvider.GetElapsedTime(failingSince, now) < timeout;

                    return new ValueTask<bool>(isWithinTimeout);
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