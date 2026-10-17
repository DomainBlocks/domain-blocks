using DomainBlocks.EventStore.Subscriptions;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// An event log in memory whose events are their positions: 1, 2, 3 and so on. Its sources read it as a store's source
/// would, and treat an <see cref="IOException"/> as transient.
/// </summary>
internal sealed class TestEventLog(bool signalsAppends = true)
{
    private readonly Lock _lock = new();
    private readonly List<SemaphoreSlim> _appendSignals = [];
    private readonly Queue<Exception> _endReadFailures = new();
    private readonly Queue<Exception> _readFailures = new();

    private readonly TaskCompletionSource _readEndPositionFailure =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _readFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _end;
    private int _cursorSourceCreatedCount;
    private int _endPositionReadCount;
    private int _cursorSourceDisposedCount;

    public int CursorSourceCreatedCount => Volatile.Read(ref _cursorSourceCreatedCount);

    public int EndPositionReadCount => Volatile.Read(ref _endPositionReadCount);

    public int CursorSourceDisposedCount => Volatile.Read(ref _cursorSourceDisposedCount);

    /// <summary>
    /// Completes when a read of the end first fails.
    /// </summary>
    public Task ReadEndPositionFailure => _readEndPositionFailure.Task;

    /// <summary>
    /// Completes when a read first fails.
    /// </summary>
    public Task ReadFailure => _readFailure.Task;

    /// <summary>
    /// Runs at the end of each read that returns events, with those events.
    /// </summary>
    public Action<IReadOnlyList<long>>? OnRead { get; set; }

    public void Append(int count = 1)
    {
        lock (_lock)
        {
            _end += count;

            if (!signalsAppends)
                return;

            // Releases happen only under the lock, so a signal's count never exceeds one.
            foreach (var signal in _appendSignals)
            {
                if (signal.CurrentCount == 0)
                    signal.Release();
            }
        }
    }

    public void FailNextEndReads(params Exception[] exceptions)
    {
        lock (_lock)
        {
            foreach (var exception in exceptions)
                _endReadFailures.Enqueue(exception);
        }
    }

    public void FailNextReads(params Exception[] exceptions)
    {
        lock (_lock)
        {
            foreach (var exception in exceptions)
                _readFailures.Enqueue(exception);
        }
    }

    public IEventLogCursorSource<long, long> CreateSource()
    {
        Interlocked.Increment(ref _cursorSourceCreatedCount);
        return new CursorSource(this);
    }

    private SemaphoreSlim AddAppendSignal()
    {
        var signal = new SemaphoreSlim(0, 1);

        lock (_lock)
            _appendSignals.Add(signal);

        return signal;
    }

    private void RemoveAppendSignal(SemaphoreSlim signal)
    {
        lock (_lock)
            _appendSignals.Remove(signal);

        signal.Dispose();
    }

    private sealed class CursorSource(TestEventLog log) : IEventLogCursorSource<long, long>
    {
        private readonly SemaphoreSlim _appendSignal = log.AddAppendSignal();

        public ValueTask<long?> ReadEndPositionAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref log._endPositionReadCount);

            lock (log._lock)
            {
                if (log._endReadFailures.TryDequeue(out var exception))
                {
                    log._readEndPositionFailure.TrySetResult();
                    return ValueTask.FromException<long?>(exception);
                }

                return ValueTask.FromResult<long?>(log._end > 0 ? log._end : null);
            }
        }

        public ValueTask<IReadOnlyList<long>> ReadAsync(
            long? fromExclusive,
            int maxCount,
            CancellationToken cancellationToken)
        {
            long[] batch;

            lock (log._lock)
            {
                if (log._readFailures.TryDequeue(out var exception))
                {
                    log._readFailure.TrySetResult();
                    return ValueTask.FromException<IReadOnlyList<long>>(exception);
                }

                var start = fromExclusive ?? 0;
                var count = (int)Math.Min(maxCount, log._end - start);
                batch = [.. Enumerable.Range(1, count).Select(i => start + i)];
            }

            if (batch.Length > 0)
                log.OnRead?.Invoke(batch);

            return ValueTask.FromResult<IReadOnlyList<long>>(batch);
        }

        public long GetLogPosition(long sourceEvent) => sourceEvent;

        public ValueTask<bool> WaitForAppendAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            new(_appendSignal.WaitAsync(timeout, cancellationToken));

        public bool IsTransientError(Exception exception) => exception is IOException;

        public ValueTask DisposeAsync()
        {
            log.RemoveAppendSignal(_appendSignal);
            Interlocked.Increment(ref log._cursorSourceDisposedCount);
            return ValueTask.CompletedTask;
        }
    }
}