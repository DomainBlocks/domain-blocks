using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.Internal;

/// <summary>
/// Creates resilience pipelines that retry transient errors until an execution has retried a number of times or has
/// been failing for a timeout, whichever comes first.
/// </summary>
internal static class RetryPipeline
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMinutes(1);
    private static readonly ResiliencePropertyKey<long> FailingSinceKey = new("DomainBlocks.FailingSince");

    /// <summary>
    /// Creates a pipeline that retries transient errors, doubling the delay with jitter after each retry.
    /// </summary>
    /// <param name="isTransient">
    /// Whether to retry an error. No error is retried once the caller has asked to cancel.
    /// </param>
    /// <param name="maxRetryAttempts">
    /// How many times an execution retries before it throws its last error, or <see cref="int.MaxValue"/> to retry
    /// without a limit.
    /// </param>
    /// <param name="timeout">
    /// How long after its first failure an execution stops retrying and throws its last error, or
    /// <see cref="Timeout.InfiniteTimeSpan"/> to retry without a limit.
    /// </param>
    /// <param name="onRetry">Called before each delay with the error, the retry's number from 1, and the delay.</param>
    /// <param name="delay">The delay before the first retry, or <see langword="null"/> for 1 second.</param>
    /// <param name="maxDelay">The longest delay between retries, or <see langword="null"/> for 1 minute.</param>
    /// <param name="timeProvider">
    /// The clock that times the delays and the timeout, or <see langword="null"/> for the system clock.
    /// </param>
    public static ResiliencePipeline Create(
        Func<Exception, bool> isTransient,
        int maxRetryAttempts,
        TimeSpan timeout,
        Action<Exception, int, TimeSpan> onRetry,
        TimeSpan? delay = null,
        TimeSpan? maxDelay = null,
        TimeProvider? timeProvider = null)
    {
        // Polly requires at least one retry.
        if (maxRetryAttempts == 0)
            return ResiliencePipeline.Empty;

        var clock = timeProvider ?? TimeProvider.System;

        return new ResiliencePipelineBuilder { TimeProvider = clock }
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = delay ?? DefaultDelay,
                MaxDelay = maxDelay ?? DefaultMaxDelay,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is not { } ex || !isTransient(ex))
                        return PredicateResult.False();

                    // Each execution has a context of its own, so the first failure recorded in it is the execution's.
                    var now = clock.GetTimestamp();

                    if (!args.Context.Properties.TryGetValue(FailingSinceKey, out var failingSince))
                    {
                        args.Context.Properties.Set(FailingSinceKey, now);
                        return PredicateResult.True();
                    }

                    var isWithinTimeout = timeout == Timeout.InfiniteTimeSpan ||
                                          clock.GetElapsedTime(failingSince, now) < timeout;

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