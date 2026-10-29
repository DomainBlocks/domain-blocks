using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// Opens change streams on the event log for a feed, counting them, telling when they have handed out changes, and
/// failing them on request.
/// </summary>
internal sealed class ChangeStreamProbe(IMongoCollection<BsonDocument> eventLog)
{
    private readonly Lock _lock = new();
    private readonly List<(int Total, TaskCompletionSource Reached)> _deliveryWaits = [];
    private int _deliveredChangeCount;
    private BsonTimestamp? _lastDeliveredClusterTime;
    private int _initialOpenCount;
    private ChangeStreamDisconnect? _disconnect;

    /// <summary>
    /// The number of change streams opened without a resume token, which is one for each connection of the feed.
    /// </summary>
    public int InitialOpenCount => Volatile.Read(ref _initialOpenCount);

    /// <summary>
    /// The number of changes that the change streams have handed to every observer attached, in all.
    /// </summary>
    public int DeliveredChangeCount
    {
        get
        {
            lock (_lock)
                return _deliveredChangeCount;
        }
    }

    /// <summary>
    /// The cluster time of the last change that the change streams have handed to every observer attached.
    /// </summary>
    public BsonTimestamp? LastDeliveredClusterTime
    {
        get
        {
            lock (_lock)
                return _lastDeliveredClusterTime;
        }
    }

    /// <summary>
    /// Waits until the change streams have handed out the given number of changes in all.
    /// </summary>
    public Task WaitForDeliveredChangesAsync(int total, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_deliveredChangeCount >= total)
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
    public ChangeStreamDisconnect SimulateDisconnect()
    {
        var disconnect = new ChangeStreamDisconnect();
        Volatile.Write(ref _disconnect, disconnect);

        return disconnect;
    }

    public async Task<IChangeStreamCursor<ChangeStreamDocument<BsonDocument>>> WatchAsync(
        PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>> pipeline,
        ChangeStreamOptions? options,
        CancellationToken cancellationToken)
    {
        if (options?.ResumeAfter is null)
        {
            Interlocked.Increment(ref _initialOpenCount);
        }
        else if (Interlocked.Exchange(ref _disconnect, null) is { } disconnect)
        {
            disconnect.SignalResumptionAttempt();
            await disconnect.ResumptionRelease.WaitAsync(cancellationToken);
        }

        var cursor = await eventLog.WatchAsync(pipeline, options, cancellationToken);

        return new Cursor(cursor, this);
    }

    private void OnDelivered(int count, BsonTimestamp? lastClusterTime)
    {
        lock (_lock)
        {
            _deliveredChangeCount += count;
            _lastDeliveredClusterTime = lastClusterTime ?? _lastDeliveredClusterTime;

            foreach (var (_, reached) in _deliveryWaits.Where(x => x.Total <= _deliveredChangeCount))
                reached.TrySetResult();

            _deliveryWaits.RemoveAll(x => x.Total <= _deliveredChangeCount);
        }
    }

    private Exception? TakeDisconnectError() => Volatile.Read(ref _disconnect)?.TakeDisconnectError();

    // The feed hands every change in a batch to every observer before its session asks for the next batch, so a
    // batch has been delivered once the next one is asked for.
    private sealed class Cursor(
        IChangeStreamCursor<ChangeStreamDocument<BsonDocument>> inner,
        ChangeStreamProbe changeStreamProbe) :
        IChangeStreamCursor<ChangeStreamDocument<BsonDocument>>
    {
        private int _undeliveredCount;
        private BsonTimestamp? _undeliveredClusterTime;

        public IEnumerable<ChangeStreamDocument<BsonDocument>> Current => inner.Current;

        public bool MoveNext(CancellationToken cancellationToken = default) =>
            MoveNextAsync(cancellationToken).GetAwaiter().GetResult();

        public async Task<bool> MoveNextAsync(CancellationToken cancellationToken = default)
        {
            changeStreamProbe.OnDelivered(_undeliveredCount, _undeliveredClusterTime);
            _undeliveredCount = 0;
            _undeliveredClusterTime = null;

            if (changeStreamProbe.TakeDisconnectError() is { } exception)
                throw exception;

            var hasBatch = await inner.MoveNextAsync(cancellationToken);

            if (hasBatch)
            {
                _undeliveredCount = inner.Current.Count();
                _undeliveredClusterTime = inner.Current.LastOrDefault()?.ClusterTime;
            }

            return hasBatch;
        }

        public BsonDocument GetResumeToken() => inner.GetResumeToken();

        public void Dispose() => inner.Dispose();
    }
}