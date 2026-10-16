using DomainBlocks.EventStore.Subscriptions;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// An event log in memory whose events are their positions: 1, 2, 3 and so on. Its sources read it as a store's source
/// would, and treat an <see cref="IOException"/> as transient.
/// </summary>
internal sealed class TestEventLog(bool signalsAppends = true)
{
    private readonly Lock _lock = new();
    private readonly List<Signal> _signals = [];
    private readonly Queue<Exception> _startFailures = new();
    private readonly Queue<Exception> _readFailures = new();
    private readonly TaskCompletionSource _startFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _readFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _end;
    private int _sourceCount;
    private int _startCount;
    private int _disposedSourceCount;
    private int _disposedSignalCount;

    public int SourceCount => Volatile.Read(ref _sourceCount);

    public int StartCount => Volatile.Read(ref _startCount);

    public int DisposedSourceCount => Volatile.Read(ref _disposedSourceCount);

    public int DisposedSignalCount => Volatile.Read(ref _disposedSignalCount);

    /// <summary>
    /// Completes when a start first fails.
    /// </summary>
    public Task StartFailed => _startFailed.Task;

    /// <summary>
    /// Completes when a read first fails.
    /// </summary>
    public Task ReadFailed => _readFailed.Task;

    /// <summary>
    /// Runs at the end of each read that returns events, with those events.
    /// </summary>
    public Action<IReadOnlyList<long>>? DuringRead { get; set; }

    public void Append(int count = 1)
    {
        Signal[] signals;

        lock (_lock)
        {
            _end += count;
            signals = signalsAppends ? [.. _signals] : [];
        }

        foreach (var signal in signals)
            signal.Set();
    }

    public void FailNextStarts(params Exception[] exceptions)
    {
        lock (_lock)
        {
            foreach (var exception in exceptions)
                _startFailures.Enqueue(exception);
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

    public IEventLogSource<long> CreateSource()
    {
        Interlocked.Increment(ref _sourceCount);
        return new Source(this);
    }

    private sealed class Source(TestEventLog log) : IEventLogSource<long>
    {
        private long _lastRead;

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref log._startCount);

            lock (log._lock)
            {
                if (log._startFailures.TryDequeue(out var exception))
                {
                    log._startFailed.TrySetResult();
                    return ValueTask.FromException(exception);
                }

                _lastRead = log._end;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<long>> ReadNextAsync(int maxCount, CancellationToken cancellationToken)
        {
            long[] batch;

            lock (log._lock)
            {
                if (log._readFailures.TryDequeue(out var exception))
                {
                    log._readFailed.TrySetResult();
                    return ValueTask.FromException<IReadOnlyList<long>>(exception);
                }

                var count = (int)Math.Min(maxCount, log._end - _lastRead);
                batch = [.. Enumerable.Range(1, count).Select(i => _lastRead + i)];
                _lastRead += count;
            }

            if (batch.Length > 0)
                log.DuringRead?.Invoke(batch);

            return ValueTask.FromResult<IReadOnlyList<long>>(batch);
        }

        public IAppendSignal CreateAppendSignal()
        {
            var signal = new Signal(log);

            lock (log._lock)
                log._signals.Add(signal);

            return signal;
        }

        public bool IsTransient(Exception exception) => exception is IOException;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref log._disposedSourceCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Signal(TestEventLog log) : IAppendSignal
    {
        private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Set() => Volatile.Read(ref _wake).TrySetResult();

        public async ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var wake = Volatile.Read(ref _wake);

            try
            {
                await wake.Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return;
            }

            // The wake is replaced only after it has fired, so an append signalled during a read ends the next wait at
            // once.
            Interlocked.CompareExchange(
                ref _wake,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                wake);
        }

        public ValueTask DisposeAsync()
        {
            lock (log._lock)
                log._signals.Remove(this);

            Interlocked.Increment(ref log._disposedSignalCount);
            return ValueTask.CompletedTask;
        }
    }
}