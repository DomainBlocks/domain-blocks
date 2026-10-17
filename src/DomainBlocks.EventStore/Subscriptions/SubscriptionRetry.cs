using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Retries the reads of one reader that subscriptions are built on: the tail, or a catch-up.
/// </summary>
/// <remarks>
/// A transient error is retried with exponential backoff and jitter, from <see cref="EventLogTailOptions.RetryDelay"/>
/// up to <see cref="EventLogTailOptions.MaxRetryDelay"/>, until a streak of failures has lasted
/// <see cref="EventLogTailOptions.RetryTimeout"/>. A streak starts at the first failure after the reader last made
/// progress, and the reader ends it whenever it hands on an event or completes a read. A canceled read is not retried.
/// The reader uses it from one thread at a time.
/// </remarks>
internal sealed class SubscriptionRetry
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private long? _streakStartedAt;

    /// <param name="isTransient">Whether an error is transient.</param>
    /// <param name="options">The retry delays and timeout.</param>
    /// <param name="onRetry">Called before each retry, with the error, the attempt that is next, and the delay.</param>
    /// <param name="timeProvider">The time provider that times a streak of failures.</param>
    public SubscriptionRetry(
        Func<Exception, bool> isTransient,
        EventLogTailOptions options,
        Action<Exception, int, TimeSpan> onRetry,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timeout = options.RetryTimeout;

        Pipeline = new ResiliencePipelineBuilder()
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
                                       isTransient(ex) &&
                                       ContinueStreak();

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

    /// <summary>
    /// Gets the pipeline that runs the reader's reads.
    /// </summary>
    public ResiliencePipeline Pipeline { get; }

    /// <summary>
    /// Ends the streak of failures, because the reader has handed on an event or completed a read.
    /// </summary>
    public void OnProgress() => _streakStartedAt = null;

    // Records a failure, starting the streak if it is the first, and says whether the streak is still short enough to
    // retry.
    private bool ContinueStreak()
    {
        var now = _timeProvider.GetTimestamp();
        _streakStartedAt ??= now;

        return _timeout == Timeout.InfiniteTimeSpan ||
               _timeProvider.GetElapsedTime(_streakStartedAt.Value, now) < _timeout;
    }
}