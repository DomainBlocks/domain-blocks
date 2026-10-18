using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Subscriptions;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// An event log in memory, whose log positions start at 1 and whose stream positions start at 0. Its sources read it as
/// a store's would, treat an <see cref="IOException"/> as transient, and hear every append unless
/// <paramref name="hearsAppends"/> is <see langword="false"/>.
/// </summary>
internal sealed class TestEventLog(bool hearsAppends = true)
{
    private readonly Lock _lock = new();
    private readonly List<TestSourceEvent> _events = [];
    private readonly Dictionary<string, long> _streamLengths = [];
    private readonly List<Channel<LogPosition>> _listeners = [];
    private readonly SemaphoreSlim _listenerStarts = new(0);
    private readonly Queue<Exception> _endReadFailures = new();
    private readonly Queue<(int EventCount, Exception Exception)> _readFailures = new();

    private readonly TaskCompletionSource _readEndPositionFailure =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _readFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int? _readableCount;
    private int _readCount;
    private int _sourceCreatedCount;
    private int _endPositionReadCount;
    private int _sourceDisposedCount;

    public TestDecoder Decoder { get; } = new();

    public int SourceCreatedCount => Volatile.Read(ref _sourceCreatedCount);

    public int EndPositionReadCount => Volatile.Read(ref _endPositionReadCount);

    public int ReadCount => Volatile.Read(ref _readCount);

    public int SourceDisposedCount => Volatile.Read(ref _sourceDisposedCount);

    public int ActiveListenerCount
    {
        get
        {
            lock (_lock)
                return _listeners.Count;
        }
    }

    /// <summary>
    /// Completes when a read of the end first fails.
    /// </summary>
    public Task ReadEndPositionFailure => _readEndPositionFailure.Task;

    /// <summary>
    /// Completes when a read first fails.
    /// </summary>
    public Task ReadFailure => _readFailure.Task;

    /// <summary>
    /// Runs at the end of each read of the log that returns events, with their log positions.
    /// </summary>
    public Action<IReadOnlyList<long>>? OnRead { get; set; }

    public void Append(int count = 1, string streamId = "s1")
    {
        lock (_lock)
        {
            for (var i = 0; i < count; i++)
            {
                var streamPosition = _streamLengths.GetValueOrDefault(streamId);
                _streamLengths[streamId] = streamPosition + 1;

                var logPosition = _events.Count + 1;
                var payload = $"event-{logPosition}";
                _events.Add(new TestSourceEvent(logPosition, streamId, streamPosition, payload, Decoder));
            }

            if (!hearsAppends)
                return;

            foreach (var listener in _listeners)
                listener.Writer.TryWrite(LogPosition.FromInt64(_events.Count));
        }
    }

    /// <summary>
    /// Makes the events appended from now on unreadable until <see cref="ShowAppends"/>, although they are still heard.
    /// </summary>
    public void HideAppends()
    {
        lock (_lock)
            _readableCount = _events.Count;
    }

    public void ShowAppends()
    {
        lock (_lock)
            _readableCount = null;
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
                _readFailures.Enqueue((0, exception));
        }
    }

    /// <summary>
    /// Makes the next read fail with the exception once it has returned the given number of events.
    /// </summary>
    public void FailNextReadAfter(int eventCount, Exception exception)
    {
        lock (_lock)
            _readFailures.Enqueue((eventCount, exception));
    }

    /// <summary>
    /// Waits until a source's listener starts.
    /// </summary>
    public Task WaitForListenerStartAsync(CancellationToken cancellationToken)
    {
        return _listenerStarts.WaitAsync(cancellationToken);
    }

    public IEventSubscriptionSource<TestSourceEvent, string, StreamPosition, LogPosition> CreateSource()
    {
        Interlocked.Increment(ref _sourceCreatedCount);
        return new Source(this);
    }

    private Channel<LogPosition> AddListener()
    {
        var listener = Channel.CreateUnbounded<LogPosition>();

        lock (_lock)
            _listeners.Add(listener);

        _listenerStarts.Release();

        return listener;
    }

    private void RemoveListener(Channel<LogPosition> listener)
    {
        lock (_lock)
            _listeners.Remove(listener);
    }

    private void OnSourceDisposed() => Interlocked.Increment(ref _sourceDisposedCount);

    // A read returns the readable events that it selects as of its start, and fails after the number of events that the
    // next queued failure says.
    private IEnumerable<TestSourceEvent> Read(Func<TestSourceEvent, bool> selects, Action<TestSourceEvent[]>? onRead)
    {
        Interlocked.Increment(ref _readCount);

        TestSourceEvent[] events;
        (int EventCount, Exception Exception)? failure;

        lock (_lock)
        {
            failure = _readFailures.TryDequeue(out var next) ? next : null;
            events = [.. _events.Take(_readableCount ?? _events.Count).Where(selects)];
        }

        for (var i = 0; i <= events.Length; i++)
        {
            if (failure is { } f && f.EventCount == Math.Min(i, events.Length))
            {
                _readFailure.TrySetResult();
                throw f.Exception;
            }

            if (i < events.Length)
                yield return events[i];
        }

        if (events.Length > 0)
            onRead?.Invoke(events);
    }

    private sealed class Source(TestEventLog log) :
        IEventSubscriptionSource<TestSourceEvent, string, StreamPosition, LogPosition>
    {
        public ValueTask<LogPosition?> ReadEndLogPositionAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref log._endPositionReadCount);

            lock (log._lock)
            {
                if (log._endReadFailures.TryDequeue(out var exception))
                {
                    log._readEndPositionFailure.TrySetResult();
                    return ValueTask.FromException<LogPosition?>(exception);
                }

                return ValueTask.FromResult<LogPosition?>(
                    log._events.Count > 0 ? LogPosition.FromInt64(log._events.Count) : null);
            }
        }

        public IAsyncEnumerable<TestSourceEvent> ReadAllAsync(
            LogPosition? fromExclusive,
            LogPosition? toInclusive,
            EventFilter filter,
            CancellationToken cancellationToken)
        {
            var events = log.Read(
                e => (fromExclusive is not { } from || e.Context.LogPosition.Value > from.Value) &&
                     (toInclusive is not { } to || e.Context.LogPosition.Value <= to.Value) &&
                     filter.Matches(e),
                events => log.OnRead?.Invoke([.. events.Select(e => (long)e.Context.LogPosition.Value)]));

            return events.ToAsyncEnumerable();
        }

        public ValueTask<StreamPosition?> ReadEndStreamPositionAsync(
            string streamId,
            CancellationToken cancellationToken)
        {
            lock (log._lock)
            {
                var end = log._streamLengths.TryGetValue(streamId, out var length)
                    ? StreamPosition.FromInt64(length - 1)
                    : (StreamPosition?)null;

                return ValueTask.FromResult(end);
            }
        }

        public IAsyncEnumerable<TestSourceEvent> ReadStreamAsync(
            string streamId,
            StreamPosition? fromExclusive,
            StreamPosition toInclusive,
            EventFilter filter,
            CancellationToken cancellationToken)
        {
            var events = log.Read(
                e => e.StreamId == streamId &&
                     (fromExclusive is not { } from || e.Context.StreamPosition.Value > from.Value) &&
                     e.Context.StreamPosition.Value <= toInclusive.Value &&
                     filter.Matches(e),
                onRead: null);

            return events.ToAsyncEnumerable();
        }

        public async IAsyncEnumerable<LogPosition> ListenForAppendsAsync(
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
            log.OnSourceDisposed();
            return ValueTask.CompletedTask;
        }
    }
}