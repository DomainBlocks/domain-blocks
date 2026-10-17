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
/// <remarks>
/// A source that listens for appends is the tail's, and the others are catch-ups'. The catch-ups' reads are recorded,
/// and can be held.
/// </remarks>
internal sealed class TestEventLog(bool hearsAppends = true)
{
    private readonly Lock _lock = new();
    private readonly List<TestSourceEvent> _events = [];
    private readonly Dictionary<string, long> _streamLengths = [];
    private readonly List<Channel<LogPosition>> _listeners = [];
    private readonly SemaphoreSlim _listenerStarts = new(0);
    private readonly Queue<Exception> _endReadFailures = new();
    private readonly Queue<Exception> _catchUpEndReadFailures = new();
    private readonly Queue<(int EventCount, Exception Exception)> _readFailures = new();
    private readonly List<CatchUpRead> _catchUpReads = [];
    private readonly Dictionary<int, CatchUpHold> _catchUpEndReadHolds = [];
    private readonly Dictionary<int, CatchUpHold> _catchUpReadHolds = [];
    private readonly List<(long Position, TaskCompletionSource Delivered)> _deliveryWaiters = [];

    private readonly TaskCompletionSource _readEndPositionFailure =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _readFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int? _readableCount;
    private long _deliveredThrough;
    private int _catchUpEndReadCount;
    private int _readCount;
    private int _sourceCreatedCount;
    private int _listenerStartCount;
    private int _endPositionReadCount;
    private int _sourceDisposedCount;

    public TestDecoder Decoder { get; } = new();

    public int SourceCreatedCount => Volatile.Read(ref _sourceCreatedCount);

    /// <summary>
    /// Gets the number of times a source has started listening, which is once for each start of the tail unless a
    /// listener fails.
    /// </summary>
    public int ListenerStartCount => Volatile.Read(ref _listenerStartCount);

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

    public IReadOnlyList<CatchUpRead> CatchUpReads
    {
        get
        {
            lock (_lock)
                return [.. _catchUpReads];
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

    /// <summary>
    /// Runs when each catch-up begins, before it reads its end position.
    /// </summary>
    public Action? OnCatchUpBegin { get; set; }

    public void Append(int count = 1, string streamId = "s1")
    {
        Append(count, streamId, position => $"event-{position}");
    }

    /// <summary>
    /// Appends events to a stream, with payloads made from the prefix and a count from 0.
    /// </summary>
    public string[] Append(string prefix, int count, string streamId = "s1")
    {
        var payloads = Enumerable.Range(0, count).Select(i => $"{prefix}-{i}").ToArray();
        var next = 0;
        Append(count, streamId, _ => payloads[next++]);

        return payloads;
    }

    /// <summary>
    /// Appends events, and waits until the tail has handed them to every observer attached to it.
    /// </summary>
    public async Task<string[]> AppendLiveAsync(
        string prefix,
        int count,
        CancellationToken cancellationToken,
        string streamId = "s1")
    {
        var payloads = Append(prefix, count, streamId);
        TaskCompletionSource delivered;

        lock (_lock)
        {
            if (_deliveredThrough >= _events.Count)
                return payloads;

            delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _deliveryWaiters.Add((_events.Count, delivered));
        }

        await delivered.Task.WaitAsync(cancellationToken);

        return payloads;
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

    public void FailNextCatchUpEndRead(Exception exception)
    {
        lock (_lock)
            _catchUpEndReadFailures.Enqueue(exception);
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
    /// Holds the catch-up with the given index once it has read its end position, until the hold is released.
    /// </summary>
    public CatchUpHold HoldCatchUpEndRead(int index) => AddHold(_catchUpEndReadHolds, index);

    /// <summary>
    /// Holds the catch-up read with the given index once it has returned its first event, until the hold is released.
    /// </summary>
    public CatchUpHold HoldCatchUpRead(int index) => AddHold(_catchUpReadHolds, index);

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

    private void Append(int count, string streamId, Func<long, string> payloadOf)
    {
        lock (_lock)
        {
            for (var i = 0; i < count; i++)
            {
                var streamPosition = _streamLengths.GetValueOrDefault(streamId);
                _streamLengths[streamId] = streamPosition + 1;

                var logPosition = _events.Count + 1;
                var payload = payloadOf(logPosition);
                _events.Add(new TestSourceEvent(logPosition, streamId, streamPosition, payload, Decoder));
            }

            if (!hearsAppends)
                return;

            foreach (var listener in _listeners)
                listener.Writer.TryWrite(LogPosition.FromInt64(_events.Count));
        }
    }

    private CatchUpHold AddHold(Dictionary<int, CatchUpHold> holds, int index)
    {
        var hold = new CatchUpHold();

        lock (_lock)
            holds[index] = hold;

        return hold;
    }

    private Channel<LogPosition> AddListener()
    {
        var listener = Channel.CreateUnbounded<LogPosition>();

        lock (_lock)
            _listeners.Add(listener);

        Interlocked.Increment(ref _listenerStartCount);
        _listenerStarts.Release();

        return listener;
    }

    private void RemoveListener(Channel<LogPosition> listener)
    {
        lock (_lock)
            _listeners.Remove(listener);
    }

    private void OnSourceDisposed() => Interlocked.Increment(ref _sourceDisposedCount);

    // The tail has handed every event up to the position to its observers.
    private void MarkDelivered(long position)
    {
        lock (_lock)
        {
            if (position <= _deliveredThrough)
                return;

            _deliveredThrough = position;

            foreach (var waiter in _deliveryWaiters.Where(x => x.Position <= position).ToArray())
            {
                waiter.Delivered.TrySetResult();
                _deliveryWaiters.Remove(waiter);
            }
        }
    }

    private async ValueTask<T> ReadCatchUpEndAsync<T>(Func<T> readEnd, CancellationToken cancellationToken)
    {
        OnCatchUpBegin?.Invoke();

        T end;
        CatchUpHold? hold;

        lock (_lock)
        {
            if (_catchUpEndReadFailures.TryDequeue(out var exception))
                throw exception;

            end = readEnd();
            _catchUpEndReadHolds.TryGetValue(_catchUpEndReadCount++, out hold);
        }

        if (hold is not null)
            await hold.WaitAsync(cancellationToken);

        return end;
    }

    // A read returns the readable events that it selects as of its start, and fails after the number of events that
    // the next queued failure says. A catch-up's read, which has an end, is recorded. The tail's read, which has none,
    // tells the log when it has handed each event on, which is when it asks for the next.
    private async IAsyncEnumerable<TestSourceEvent> ReadAsync(
        Func<TestSourceEvent, bool> selects,
        ulong? fromExclusive,
        ulong? toInclusive,
        bool notifiesOnRead,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _readCount);

        TestSourceEvent[] events;
        (int EventCount, Exception Exception)? failure;
        int? catchUpIndex = null;
        CatchUpHold? hold = null;

        lock (_lock)
        {
            failure = _readFailures.TryDequeue(out var next) ? next : null;
            events = [.. _events.Take(_readableCount ?? _events.Count).Where(selects)];

            if (toInclusive is { } to)
            {
                catchUpIndex = _catchUpReads.Count;
                _catchUpReads.Add(new CatchUpRead(fromExclusive, to, IsCompleted: false));
                _catchUpReadHolds.TryGetValue(catchUpIndex.Value, out hold);
            }
        }

        for (var i = 0; i <= events.Length; i++)
        {
            if (failure is { } f && f.EventCount == Math.Min(i, events.Length))
            {
                _readFailure.TrySetResult();
                throw f.Exception;
            }

            if (i == events.Length)
                break;

            yield return events[i];

            if (catchUpIndex is null)
                MarkDelivered((long)events[i].Context.LogPosition.Value);
            else if (i == 0 && hold is not null)
                await hold.WaitAsync(cancellationToken);
        }

        if (catchUpIndex is { } index)
        {
            lock (_lock)
                _catchUpReads[index] = _catchUpReads[index] with { IsCompleted = true };
        }

        if (notifiesOnRead && events.Length > 0)
            OnRead?.Invoke([.. events.Select(e => (long)e.Context.LogPosition.Value)]);
    }

    private sealed class Source(TestEventLog log) :
        IEventSubscriptionSource<TestSourceEvent, string, StreamPosition, LogPosition>
    {
        private bool _isTail;

        public async ValueTask<LogPosition?> ReadEndLogPositionAsync(CancellationToken cancellationToken)
        {
            if (!_isTail)
                return await log.ReadCatchUpEndAsync(ReadEnd, cancellationToken);

            Interlocked.Increment(ref log._endPositionReadCount);

            lock (log._lock)
            {
                if (log._endReadFailures.TryDequeue(out var exception))
                {
                    log._readEndPositionFailure.TrySetResult();
                    throw exception;
                }
            }

            // The tail starts here, so it never hands on the events before it.
            var end = ReadEnd();
            log.MarkDelivered((long)(end?.Value ?? 0));

            return end;

            LogPosition? ReadEnd()
            {
                lock (log._lock)
                    return log._events.Count > 0 ? LogPosition.FromInt64(log._events.Count) : null;
            }
        }

        public IAsyncEnumerable<TestSourceEvent> ReadAllAsync(
            LogPosition? fromExclusive,
            LogPosition? toInclusive,
            EventFilter filter,
            CancellationToken cancellationToken)
        {
            return log.ReadAsync(
                e => (fromExclusive is not { } from || e.Context.LogPosition.Value > from.Value) &&
                     (toInclusive is not { } to || e.Context.LogPosition.Value <= to.Value) &&
                     filter.Matches(e),
                fromExclusive?.Value,
                toInclusive?.Value,
                notifiesOnRead: true,
                cancellationToken);
        }

        public ValueTask<StreamPosition?> ReadEndStreamPositionAsync(
            string streamId,
            CancellationToken cancellationToken)
        {
            return log.ReadCatchUpEndAsync(
                () => log._streamLengths.TryGetValue(streamId, out var length)
                    ? StreamPosition.FromInt64(length - 1)
                    : (StreamPosition?)null,
                cancellationToken);
        }

        public IAsyncEnumerable<TestSourceEvent> ReadStreamAsync(
            string streamId,
            StreamPosition? fromExclusive,
            StreamPosition toInclusive,
            EventFilter filter,
            CancellationToken cancellationToken)
        {
            return log.ReadAsync(
                e => e.StreamId == streamId &&
                     (fromExclusive is not { } from || e.Context.StreamPosition.Value > from.Value) &&
                     e.Context.StreamPosition.Value <= toInclusive.Value &&
                     filter.Matches(e),
                fromExclusive?.Value,
                toInclusive.Value,
                notifiesOnRead: false,
                cancellationToken);
        }

        public async IAsyncEnumerable<LogPosition> ListenForAppendsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _isTail = true;
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