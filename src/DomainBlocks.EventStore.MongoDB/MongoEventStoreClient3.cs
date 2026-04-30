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
    private static readonly TransactionOptions TransactionOptions = new(
        ReadConcern.Snapshot,
        ReadPreference.Primary,
        WriteConcern.WMajority.With(journal: true));

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
    private readonly Buffers _buffers = new();

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

        var pendingAppend = new PendingAppend(options.CommitId, streamId, options.ExpectedState, eventDocuments);

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

                await ProcessAppendsAsync(batch, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            // Graceful stop: complete writer, then fault current batch + queued items.
            _appendChannel.Writer.TryComplete();
            FaultAll(batch, ex);
            DrainWithFault(_appendChannel, ex);
        }
        catch (Exception ex)
        {
            // Fatal worker failure: complete writer with error, then current batch + queued items.
            _appendChannel.Writer.TryComplete(ex);
            FaultAll(batch, ex);
            DrainWithFault(_appendChannel, ex);
        }
    }

    private async Task ProcessAppendsAsync(List<PendingAppend> batch, CancellationToken ct)
    {
        _buffers.ClearAll();
        await PrepareAndPruneAsync(batch, ct).ConfigureAwait(false);
        await CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task PrepareAndPruneAsync(List<PendingAppend> batch, CancellationToken ct)
    {
        _preAppendQuery.Reset();

        foreach (var append in batch)
            _preAppendQuery.AddInput(append.BsonCommitId, append.BsonStreamId);

        await _preAppendQuery
            .ExecuteAsync(
                _buffers.ExistingCommitIds,
                _buffers.HeadStreamVersions,
                ct)
            .ConfigureAwait(false);

        var writtenAtUtc = DateTime.UtcNow;

        foreach (var append in batch)
        {
            var commitId = append.CommitId;

            if (!_buffers.SeenCommitIds.Add(commitId))
                continue;

            if (_buffers.ExistingCommitIds.Contains(commitId))
            {
                append.Ack.TrySetResult();
                continue;
            }

            var streamId = append.StreamId;
            var expectedState = append.ExpectedState;
            var streamVersion = _buffers.HeadStreamVersions.GetValueOrDefault(streamId, -1L);

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
                _buffers.OutgoingEvents.Add(e);
            }

            _buffers.HeadStreamVersions[streamId] = streamVersion;
            _buffers.OutgoingAppends.Add(commitId, append);
        }

        // Remove everything we've already completed.
        batch.RemoveAll(x => x.Ack.Task.IsCompleted);
    }

    private async Task CommitAsync(CancellationToken ct)
    {
        var eventCount = _buffers.OutgoingEvents.Count;
        if (eventCount == 0)
            return;

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: ct).ConfigureAwait(false);
        session.StartTransaction(TransactionOptions);

        try
        {
            var events = _buffers.OutgoingEvents;
            var startPosition = await ClaimPositionsAsync(session, eventCount, ct).ConfigureAwait(false);

            for (var i = 0; i < eventCount; i++)
                events[i]["_id"] = startPosition + i;

            await _eventLog
                .InsertManyAsync(session, events, new InsertManyOptions { IsOrdered = true }, ct)
                .ConfigureAwait(false);

            await session.CommitTransactionAsync(ct).ConfigureAwait(false);

            foreach (var append in _buffers.OutgoingAppends.Values)
                append.Ack.TrySetResult();
        }
        catch (Exception ex)
        {
            // TODO
        }
    }

    private async Task<long> ClaimPositionsAsync(IClientSessionHandle session, long count, CancellationToken ct)
    {
        const string sequenceId = "event_log_seq";
        const string nextFieldName = "next";

        var filter = Builders<BsonDocument>.Filter.Eq("_id", sequenceId);
        var update = Builders<BsonDocument>.Update.Inc(nextFieldName, count);

        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            IsUpsert = true,
            Projection = Builders<BsonDocument>.Projection.Include(nextFieldName),
            ReturnDocument = ReturnDocument.Before
        };

        var result = await _sequences
            .FindOneAndUpdateAsync(session, filter, update, options, ct)
            .ConfigureAwait(false);

        var start = result?[nextFieldName].ToInt64() ?? 0;

        return start;
    }

    private static void FaultAll(List<PendingAppend> appends, Exception exception)
    {
        foreach (var append in appends)
            append.Ack.TrySetException(exception);
    }

    private static void DrainWithFault(ChannelReader<PendingAppend> reader, Exception exception)
    {
        while (reader.TryRead(out var pending))
            pending.Ack.TrySetException(exception);
    }

    private sealed class Buffers
    {
        public readonly HashSet<Guid> SeenCommitIds = [];
        public readonly HashSet<Guid> ExistingCommitIds = [];
        public readonly Dictionary<string, long> HeadStreamVersions = [];
        public readonly List<BsonDocument> OutgoingEvents = [];
        public readonly Dictionary<Guid, PendingAppend> OutgoingAppends = [];

        public void ClearAll()
        {
            SeenCommitIds.Clear();
            ExistingCommitIds.Clear();
            HeadStreamVersions.Clear();
            OutgoingEvents.Clear();
            OutgoingAppends.Clear();
        }
    }

    private sealed class PendingAppend
    {
        public PendingAppend(
            Guid commitId,
            string streamId,
            ExpectedStreamState expectedState,
            BsonDocument[] events)
        {
            if (events.Length == 0)
                throw new ArgumentException("Must have at least one event.", nameof(events));

            CommitId = commitId;
            StreamId = streamId;
            Events = events;
            ExpectedState = expectedState;
            Ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Guid CommitId { get; }
        public string StreamId { get; }
        public BsonDocument[] Events { get; }
        public ExpectedStreamState ExpectedState { get; }
        public TaskCompletionSource Ack { get; }

        // Computed properties
        public BsonValue BsonCommitId => Events[0][EventLogEntry.FieldNames.CommitId];
        public BsonValue BsonStreamId => Events[0][EventLogEntry.FieldNames.StreamId];
    }
}