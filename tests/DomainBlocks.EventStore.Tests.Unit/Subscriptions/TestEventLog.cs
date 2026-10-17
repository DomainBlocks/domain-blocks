using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Subscriptions;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// An event log in memory whose events are their positions: 1, 2, 3 and so on. Its sources read it as a store's source
/// would, treat an <see cref="IOException"/> as transient, and hear every append unless <paramref name="hearsAppends"/>
/// is <see langword="false"/>.
/// </summary>
internal sealed class TestEventLog(bool hearsAppends = true)
{
    private readonly Lock _lock = new();
    private readonly List<Channel<long>> _listeners = [];
    private readonly SemaphoreSlim _listenerStarts = new(0);
    private readonly Queue<Exception> _endReadFailures = new();
    private readonly Queue<Exception> _readFailures = new();

    private readonly TaskCompletionSource _readEndPositionFailure =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _readFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _end;
    private long? _readableEnd;
    private int _readCount;
    private int _cursorSourceCreatedCount;
    private int _endPositionReadCount;
    private int _cursorSourceDisposedCount;

    public int CursorSourceCreatedCount => Volatile.Read(ref _cursorSourceCreatedCount);

    public int EndPositionReadCount => Volatile.Read(ref _endPositionReadCount);

    public int ReadCount => Volatile.Read(ref _readCount);

    public int ActiveListenerCount
    {
        get
        {
            lock (_lock)
                return _listeners.Count;
        }
    }

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

            if (!hearsAppends)
                return;

            foreach (var listener in _listeners)
                listener.Writer.TryWrite(_end);
        }
    }

    /// <summary>
    /// Makes the events appended from now on unreadable until <see cref="ShowAppends"/>, although they are still heard.
    /// </summary>
    public void HideAppends()
    {
        lock (_lock)
            _readableEnd = _end;
    }

    public void ShowAppends()
    {
        lock (_lock)
            _readableEnd = null;
    }

    /// <summary>
    /// Makes every source's listener fail with the exception.
    /// </summary>
    public void FailListeners(Exception exception)
    {
        lock (_lock)
        {
            foreach (var listener in _listeners)
                listener.Writer.TryComplete(exception);

            _listeners.Clear();
        }
    }

    /// <summary>
    /// Waits until a source's listener starts.
    /// </summary>
    public Task WaitForListenerStartAsync(CancellationToken cancellationToken)
    {
        return _listenerStarts.WaitAsync(cancellationToken);
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

    private Channel<long> AddListener()
    {
        var listener = Channel.CreateUnbounded<long>();

        lock (_lock)
            _listeners.Add(listener);

        _listenerStarts.Release();

        return listener;
    }

    private void RemoveListener(Channel<long> listener)
    {
        lock (_lock)
            _listeners.Remove(listener);
    }

    private sealed class CursorSource(TestEventLog log) : IEventLogCursorSource<long, long>
    {

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
            Interlocked.Increment(ref log._readCount);

            long[] batch;

            lock (log._lock)
            {
                if (log._readFailures.TryDequeue(out var exception))
                {
                    log._readFailure.TrySetResult();
                    return ValueTask.FromException<IReadOnlyList<long>>(exception);
                }

                var start = fromExclusive ?? 0;
                var count = (int)Math.Max(0, Math.Min(maxCount, (log._readableEnd ?? log._end) - start));
                batch = [.. Enumerable.Range(1, count).Select(i => start + i)];
            }

            if (batch.Length > 0)
                log.OnRead?.Invoke(batch);

            return ValueTask.FromResult<IReadOnlyList<long>>(batch);
        }

        public long GetLogPosition(long sourceEvent) => sourceEvent;

        public async IAsyncEnumerable<long> ListenForAppendsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var listener = log.AddListener();

            try
            {
                await foreach (var position in listener.Reader.ReadAllAsync(cancellationToken))
                    yield return position;
            }
            finally
            {
                log.RemoveListener(listener);
            }
        }

        public bool IsTransientError(Exception exception) => exception is IOException;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref log._cursorSourceDisposedCount);
            return ValueTask.CompletedTask;
        }
    }
}