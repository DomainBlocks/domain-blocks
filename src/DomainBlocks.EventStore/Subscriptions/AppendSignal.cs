using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Signals the events appended to a store's log, so that any number of callers can wait for an event after their own
/// position. It runs the store's listener, and keeps the highest position the listener has yielded.
/// </summary>
/// <remarks>
/// <para>
/// The signal only saves time, so it never fails. When the store's listener fails or ends, it is restarted with
/// backoff, and while it is down, waits time out and the caller reads anyway. A listener that fails after it has heard
/// an append is restarted at once, and its next failure backs off from the start.
/// </para>
/// <para>
/// The high-water mark and the task that completes when it next advances are held together in one immutable state,
/// which only the listener replaces, so reads and waits take no lock.
/// </para>
/// </remarks>
internal sealed class AppendSignal<TLogPos> : IAsyncDisposable where TLogPos : struct, IComparable<TLogPos>
{
    private readonly Func<CancellationToken, IAsyncEnumerable<TLogPos>> _listen;
    private readonly ILogger? _logger;
    private readonly string _tailId;
    private readonly ResiliencePipeline _pipeline;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly CancellationToken _stopToken;
    private State _state = new(null);
    private Task? _listening;
    private int _started;
    private int _disposed;

    /// <param name="listen">Listens for appends, yielding the position of the last event in each.</param>
    /// <param name="options">The retry delays.</param>
    /// <param name="logger">The logger for restarts.</param>
    /// <param name="tailId">The ID of the tail session that listens, which correlates its restarts.</param>
    public AppendSignal(
        Func<CancellationToken, IAsyncEnumerable<TLogPos>> listen,
        EventLogTailOptions options,
        ILogger? logger,
        string tailId)
    {
        _listen = listen;
        _logger = logger;
        _tailId = tailId;
        _pipeline = CreatePipeline(options, logger, tailId);
        _stopToken = _stopCts.Token;
    }

    /// <summary>
    /// Gets the highest position heard of, or <see langword="null"/> if nothing has been heard yet.
    /// </summary>
    public TLogPos? HighWaterMark => Volatile.Read(ref _state).HighWaterMark;

    /// <summary>
    /// Starts listening, if it has not started. This runs the store's listener until it first waits, so a listener that
    /// starts listening without waiting is listening when this returns.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Interlocked.Exchange(ref _started, 1) == 0)
            Volatile.Write(ref _listening, ListenAsync());
    }

    /// <summary>
    /// Waits until an append after <paramref name="fromExclusive"/> has been heard, or for <paramref name="timeout"/>.
    /// It returns at once if one has been heard already.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if such an append has been heard, or <see langword="false"/> if the timeout elapsed
    /// first.
    /// </returns>
    public async ValueTask<bool> WaitAsync(
        TLogPos? fromExclusive,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            var state = Volatile.Read(ref _state);

            if (IsAfter(state.HighWaterMark, fromExclusive))
                return true;

            var remaining = timeout - Stopwatch.GetElapsedTime(started);

            if (remaining <= TimeSpan.Zero)
                return false;

            try
            {
                await state.Advancement.Task.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }

            // The high-water mark has advanced, but not necessarily past fromExclusive, so the loop checks it again.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _stopCts.CancelAsync().ConfigureAwait(false);

        if (Volatile.Read(ref _listening) is { } listening)
            await listening.ConfigureAwait(false);

        _stopCts.Dispose();
    }

    /// <summary>
    /// Whether a position is after another, where <see langword="null"/> is before every position.
    /// </summary>
    public static bool IsAfter(TLogPos? position, TLogPos? other)
    {
        return position is { } p && (other is not { } o || p.CompareTo(o) > 0);
    }

    private async Task ListenAsync()
    {
        try
        {
            while (true)
            {
                await _pipeline
                    .ExecuteAsync(static (signal, ct) => signal.ListenUntilFailureAsync(ct), this, _stopToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested)
        {
        }
    }

    // A listener that fails before it hears anything throws, so the pipeline backs off. One that fails after it has
    // heard an append returns, so the next attempt starts at once, with the backoff reset.
    private async ValueTask ListenUntilFailureAsync(CancellationToken cancellationToken)
    {
        var hasHeard = false;

        try
        {
            await foreach (var position in _listen(cancellationToken).ConfigureAwait(false))
            {
                hasHeard = true;
                Advance(position);
            }

            throw new InvalidOperationException("Listening for appends ended.");
        }
        catch (Exception ex) when (hasHeard && !cancellationToken.IsCancellationRequested)
        {
            _logger?.TailListenerRestarting(ex, _tailId);
        }
    }

    // Only the listener calls this, one position at a time, so the state needs no compare-and-swap. The new state is
    // published before the old advancement completes, so a waiter it wakes sees the new high-water mark.
    private void Advance(TLogPos position)
    {
        var state = _state;

        if (!IsAfter(position, state.HighWaterMark))
            return;

        Volatile.Write(ref _state, new State(position));
        state.Advancement.TrySetResult();
    }

    private static ResiliencePipeline CreatePipeline(EventLogTailOptions options, ILogger? logger, string tailId)
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
                    var shouldHandle = args.Outcome.Exception is not null &&
                                       !args.Context.CancellationToken.IsCancellationRequested;

                    return ValueTask.FromResult(shouldHandle);
                },
                OnRetry = args =>
                {
                    if (args.Outcome.Exception is { } ex)
                        logger?.TailListenerRetrying(ex, tailId, args.AttemptNumber + 1, args.RetryDelay);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private sealed class State(TLogPos? highWaterMark)
    {
        public TLogPos? HighWaterMark { get; } = highWaterMark;

        // Completes when the high-water mark advances past this state's.
        public TaskCompletionSource Advancement { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}