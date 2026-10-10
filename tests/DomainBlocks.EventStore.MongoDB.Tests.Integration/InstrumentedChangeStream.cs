using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// Opens change streams on the event log for a change stream subject, counting them, telling when they have handed out
/// changes, and failing them on request.
/// </summary>
internal sealed class InstrumentedChangeStream(IMongoCollection<BsonDocument> eventLog)
{
    private readonly Lock _lock = new();
    private readonly List<(int Total, TaskCompletionSource Reached)> _deliveryWaits = [];
    private int _deliveredCount;
    private int _freshOpenCount;
    private Fault? _fault;

    /// <summary>
    /// The number of change streams opened without a resume token, which is one for each connection of the subject.
    /// </summary>
    public int FreshOpenCount => Volatile.Read(ref _freshOpenCount);

    /// <summary>
    /// The number of changes that the change streams have handed to every observer attached, in all.
    /// </summary>
    public int DeliveredCount
    {
        get
        {
            lock (_lock)
                return _deliveredCount;
        }
    }

    /// <summary>
    /// Waits until the change streams have handed out the given number of changes in all.
    /// </summary>
    public Task DeliveredAsync(int total, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_deliveredCount >= total)
                return Task.CompletedTask;

            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _deliveryWaits.Add((total, reached));

            return reached.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Makes the change stream lose its connection at its next batch, and then fail with the given error when it tries
    /// to resume. The resume waits until the fault is released.
    /// </summary>
    public Fault Disconnect(Exception resumeError)
    {
        var fault = new Fault(resumeError);
        Volatile.Write(ref _fault, fault);

        return fault;
    }

    public async Task<IChangeStreamCursor<ChangeStreamDocument<BsonDocument>>> WatchAsync(
        PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>> pipeline,
        ChangeStreamOptions? options,
        CancellationToken cancellationToken)
    {
        if (options?.ResumeAfter is null)
        {
            Interlocked.Increment(ref _freshOpenCount);
        }
        else if (Interlocked.Exchange(ref _fault, null) is { } fault)
        {
            fault.SetResuming();
            await fault.Released.WaitAsync(cancellationToken);

            throw fault.ResumeError;
        }

        var cursor = await eventLog.WatchAsync(pipeline, options, cancellationToken);

        return new Cursor(cursor, this);
    }

    private void OnDelivered(int count)
    {
        lock (_lock)
        {
            _deliveredCount += count;

            foreach (var (_, reached) in _deliveryWaits.Where(x => x.Total <= _deliveredCount))
                reached.TrySetResult();

            _deliveryWaits.RemoveAll(x => x.Total <= _deliveredCount);
        }
    }

    private Exception? TakeDisconnect() => Volatile.Read(ref _fault)?.Disconnect();

    public sealed class Fault(Exception resumeError)
    {
        private readonly TaskCompletionSource _resuming = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _isDisconnected;

        public Exception ResumeError { get; } = resumeError;

        /// <summary>
        /// Completes when the change stream tries to resume.
        /// </summary>
        public Task Resuming => _resuming.Task;

        public Task Released => _released.Task;

        public void Release() => _released.TrySetResult();

        public void SetResuming() => _resuming.TrySetResult();

        // A timeout is an error that the change stream resumes from. Only the first batch after the request fails.
        public Exception? Disconnect() =>
            Interlocked.Exchange(ref _isDisconnected, 1) == 0 ? new TimeoutException("Disconnected.") : null;
    }

    // The subject's producer hands every change in a batch to every observer before it asks for the next batch, so a
    // batch has been delivered once the next one is asked for.
    private sealed class Cursor(
        IChangeStreamCursor<ChangeStreamDocument<BsonDocument>> inner,
        InstrumentedChangeStream changeStream) :
        IChangeStreamCursor<ChangeStreamDocument<BsonDocument>>
    {
        private int _undeliveredCount;

        public IEnumerable<ChangeStreamDocument<BsonDocument>> Current => inner.Current;

        public bool MoveNext(CancellationToken cancellationToken = default) =>
            MoveNextAsync(cancellationToken).GetAwaiter().GetResult();

        public async Task<bool> MoveNextAsync(CancellationToken cancellationToken = default)
        {
            changeStream.OnDelivered(_undeliveredCount);
            _undeliveredCount = 0;

            if (changeStream.TakeDisconnect() is { } exception)
                throw exception;

            var hasBatch = await inner.MoveNextAsync(cancellationToken);

            if (hasBatch)
                _undeliveredCount = inner.Current.Count();

            return hasBatch;
        }

        public BsonDocument GetResumeToken() => inner.GetResumeToken();

        public void Dispose() => inner.Dispose();
    }
}