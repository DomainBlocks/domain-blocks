using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreClient3<TEvent> :
    IEventStoreClient<TEvent>,
    IAsyncDisposable
    where TEvent : notnull
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly IMongoCollection<BsonDocument> _sequences;
    private readonly PreAppendQuery _preAppendQuery;
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _decoder;
    private readonly Channel<PendingAppend> _appendChannel;
    private readonly int _appendBatchSize;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task _appendTask;

    // Reused buffers
    private readonly HashSet<Guid> _seenCommitIds = [];
    private readonly HashSet<Guid> _existingCommitIds = [];
    private readonly Dictionary<string, long> _headStreamVersions = [];
    private readonly List<BsonDocument> _batchedEvents = [];
    private readonly Dictionary<Guid, PendingAppend> _pendingAppends = [];

    public MongoEventStoreClient3(
        IMongoClient mongoClient,
        EventCodec<TEvent, BsonValue, BsonValue> eventCodec,
        MongoEventStoreClient2Options? options = null)
    {
        options ??= new MongoEventStoreClient2Options();

        var db = mongoClient
            .GetDatabase(options.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _mongoClient = mongoClient;
        _eventLog = db.GetCollection<BsonDocument>(options.EventLogCollectionName);
        _sequences = db.GetCollection<BsonDocument>(options.SequencesCollectionName);
        _preAppendQuery = new PreAppendQuery(_eventLog);
        _encoder = eventCodec.Encoder;
        _decoder = eventCodec.Decoder;

        _appendChannel = Channel.CreateBounded<PendingAppend>(new BoundedChannelOptions(options.AppendQueueCapacity)
        {
            SingleWriter = false,
            SingleReader = true
        });

        _appendBatchSize = options.AppendBatchSize;
        _appendTask = ProcessAppendsAsync(_stopCts.Token);
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AppendToStreamOptions();

        var bsonCommitId = new BsonBinaryData(options.CommitId, GuidRepresentation.Standard);

        var eventDocuments = _encoder
            .Encode(events)
            .Select((x, i) => new BsonDocument
            {
                { EventLogEntry.FieldNames.StreamId, streamId },
                { EventLogEntry.FieldNames.CommitId, bsonCommitId },
                { EventLogEntry.FieldNames.CommitIndex, i },
                { EventLogEntry.FieldNames.EventName, x.EventName },
                { EventLogEntry.FieldNames.EventData, x.EventData },
                { EventLogEntry.FieldNames.Metadata, x.Metadata ?? BsonNull.Value }
            })
            .ToArray();

        if (eventDocuments.Length == 0)
            return;

        var pendingAppend = new PendingAppend(eventDocuments, options.ExpectedState);

        using var linkedTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedTimeoutCts.CancelAfter(options.Timeout);

        try
        {
            await _appendChannel.Writer.WriteAsync(pendingAppend, linkedTimeoutCts.Token);
            await pendingAppend.Ack.Task.WaitAsync(linkedTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller canceled.
            throw;
        }
        catch (OperationCanceledException) when (linkedTimeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Append operation did not complete within {options.Timeout}.");
        }
    }

    public IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async ValueTask DisposeAsync()
    {
        _appendChannel.Writer.TryComplete();
        await _stopCts.CancelAsync().ConfigureAwait(false);
        await _appendTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stopCts.Dispose();
    }

    private async Task ProcessAppendsAsync(CancellationToken ct)
    {
        var batch = new List<PendingAppend>(_appendBatchSize);

        try
        {
            while (await _appendChannel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                batch.Clear();

                while (batch.Count < _appendBatchSize && _appendChannel.Reader.TryRead(out var append))
                    batch.Add(append);

                if (batch.Count == 0)
                    continue;

                await ProcessAppendBatchWithRetryAsync(batch, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            // Graceful stop: complete writer, then fault current batch + queued items.
            _appendChannel.Writer.TryComplete();
            FaultAll(batch, ex);
            DrainWithFault(ex);
        }
        catch (Exception ex)
        {
            // Fatal worker failure: complete writer with error, then current batch + queued items.
            _appendChannel.Writer.TryComplete(ex);
            FaultAll(batch, ex);
            DrainWithFault(ex);
        }
    }

    private async Task ProcessAppendBatchWithRetryAsync(List<PendingAppend> batch, CancellationToken ct)
    {
        await ProcessAppendBatchAsync(batch, ct).ConfigureAwait(false);
    }

    private async Task ProcessAppendBatchAsync(List<PendingAppend> batch, CancellationToken ct)
    {
        _preAppendQuery.Reset();

        // Clear buffers
        _seenCommitIds.Clear();
        _existingCommitIds.Clear();
        _headStreamVersions.Clear();
        _batchedEvents.Clear();
        _pendingAppends.Clear();

        foreach (var append in batch)
            _preAppendQuery.AddInput(append.CommitId, append.StreamId);

        await _preAppendQuery.ExecuteAsync(_existingCommitIds, _headStreamVersions, ct).ConfigureAwait(false);

        var writtenAtUtc = DateTime.UtcNow;

        foreach (var append in batch)
        {
            if (!_seenCommitIds.Add(append.CommitId.AsGuid))
                continue;

            if (_existingCommitIds.Contains(append.CommitId.AsGuid))
                append.Ack.TrySetResult();

            var streamId = append.StreamId.AsString;
            var expectedState = append.ExpectedState;
            var streamVersion = _headStreamVersions.GetValueOrDefault(streamId, -1L);

            var actualState = streamVersion < 0
                ? StreamState.StreamDoesNotExist
                : StreamState.StreamExists(StreamVersion.FromInt64(streamVersion));

            if (!expectedState.Matches(actualState))
            {
                append.Ack.TrySetException(new StreamAppendConflictException(streamId, expectedState, actualState));
                continue;
            }

            foreach (var e in append.Events)
            {
                e[EventLogEntry.FieldNames.StreamVersion] = ++streamVersion;
                e[EventLogEntry.FieldNames.WrittenAtUtc] = writtenAtUtc;
                _batchedEvents.Add(e);
            }

            _pendingAppends.Add(append.CommitId.AsGuid, append);
            _headStreamVersions[streamId] = streamVersion;
        }

        // Remove everything we've already completed.
        batch.RemoveAll(x => x.Ack.Task.IsCompleted);

        // TODO: Transaction!
    }

    private static void FaultAll(List<PendingAppend> appends, Exception exception)
    {
        foreach (var append in appends)
            append.Ack.TrySetException(exception);
    }

    private void DrainWithFault(Exception exception)
    {
        while (_appendChannel.Reader.TryRead(out var pending))
            pending.Ack.TrySetException(exception);
    }

    private sealed class PendingAppend
    {
        public PendingAppend(BsonDocument[] events, ExpectedStreamState expectedState)
        {
            if (events.Length == 0)
                throw new ArgumentException("Must have at least one event.", nameof(events));

            Events = events;
            ExpectedState = expectedState;
            Ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public BsonDocument[] Events { get; }
        public ExpectedStreamState ExpectedState { get; }
        public TaskCompletionSource Ack { get; }

        // Computed properties
        public BsonValue StreamId => Events[0][EventLogEntry.FieldNames.StreamId];
        public BsonValue CommitId => Events[0][EventLogEntry.FieldNames.CommitId];
    }
}