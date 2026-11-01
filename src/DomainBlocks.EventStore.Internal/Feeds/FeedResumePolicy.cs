using Microsoft.Extensions.Logging;
using Polly;

namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Represents how a store's feeds recover from errors: which errors are transient, and how to retry an operation that
/// fails with one. It holds nothing of any one feed, so a store creates one and shares it between its feeds.
/// </summary>
internal sealed class FeedResumePolicy
{
    // Polly rejects retry delays longer than 1 day.
    private static readonly TimeSpan PollyMaxDelay = TimeSpan.FromDays(1);
    private static readonly ResiliencePropertyKey<string> FeedIdKey = new("DomainBlocks.FeedId");

    private readonly Func<Exception, bool> _isTransient;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _stabilityPeriod;
    private readonly ResiliencePipeline _retryPipeline;

    /// <param name="isTransient">
    /// Whether an error is worth retrying. A feed creates a new session only after the errors that this accepts, and
    /// after a session that ends without an error. Any other error faults the feed.
    /// </param>
    /// <param name="options">The retry options, or <see langword="null"/> for the defaults.</param>
    /// <param name="logger">The logger for retries.</param>
    /// <exception cref="ArgumentOutOfRangeException">An option is outside the range that a policy supports.</exception>
    public FeedResumePolicy(
        Func<Exception, bool> isTransient,
        FeedResumeOptions? options = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(isTransient);

        options ??= FeedResumeOptions.Default;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.RetryDelay, PollyMaxDelay);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxRetryDelay, PollyMaxDelay);

        _isTransient = ex => ex is FeedSessionEndedException || isTransient(ex);
        _timeProvider = options.TimeProvider;
        _stabilityPeriod = options.StabilityPeriod;

        _retryPipeline = RetryPipeline.Create(
            _isTransient,
            options.MaxRetryAttempts,
            Timeout.InfiniteTimeSpan,
            (context, ex, attempt, delay) =>
                logger?.FeedRetrying(ex, context.Properties.GetValue(FeedIdKey, "unknown"), attempt, delay),
            options.RetryDelay,
            options.MaxRetryDelay,
            options.TimeProvider);
    }

    /// <summary>
    /// Determines whether an error is worth retrying.
    /// </summary>
    public bool IsTransient(Exception exception) => _isTransient(exception);

    /// <summary>
    /// Gets a timestamp from the policy's clock.
    /// </summary>
    public long GetTimestamp() => _timeProvider.GetTimestamp();

    /// <summary>
    /// Gets the time since a timestamp from <see cref="GetTimestamp"/>.
    /// </summary>
    public TimeSpan GetElapsedTime(long startingTimestamp) => _timeProvider.GetElapsedTime(startingTimestamp);

    /// <summary>
    /// Determines whether a session that connected at a timestamp from <see cref="GetTimestamp"/> has stayed up long
    /// enough to count as working.
    /// </summary>
    public bool IsStable(long connectedTimestamp) => GetElapsedTime(connectedTimestamp) >= _stabilityPeriod;

    /// <summary>
    /// Runs an operation for a feed, retrying it after a transient error with backoff.
    /// </summary>
    /// <param name="operation">The operation to run.</param>
    /// <param name="feedId">The feed's ID, which the log of each retry includes.</param>
    /// <param name="cancellationToken">The token that stops the retries.</param>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string feedId,
        CancellationToken cancellationToken)
    {
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(FeedIdKey, feedId);

        try
        {
            await _retryPipeline
                .ExecuteAsync(
                    static async (ctx, op) => await op(ctx.CancellationToken).ConfigureAwait(false),
                    context,
                    operation)
                .ConfigureAwait(false);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}