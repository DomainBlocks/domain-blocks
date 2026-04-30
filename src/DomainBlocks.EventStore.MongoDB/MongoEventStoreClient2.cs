using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal static class MongoEventStoreClient2
{
    public static readonly TransactionOptions TransactionOptions = new(
        ReadConcern.Snapshot,
        ReadPreference.Primary,
        WriteConcern.WMajority.With(journal: true));
}

public sealed class MongoEventStoreClient2<TEvent> :
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
    private readonly Buffers _buffers = new();

    public MongoEventStoreClient2(
        IMongoClient mongoClient,
        EventCodec<TEvent, BsonValue, BsonValue> eventCodec,
        MongoEventStoreClientOptions2? options = null)
    {
        options ??= new MongoEventStoreClientOptions2();

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

    public async IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= ReadStreamOptions.Default;
        var position = options.Position;
        var direction = options.Direction;

        // Edge cases that represent an empty sequence of events.
        if (position.IsStart && direction == StreamReadDirection.Backward ||
            position.IsEnd && direction == StreamReadDirection.Forward)
        {
            if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }

        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);

        if (position.IsSpecificVersion)
        {
            var versionValue = position.Version.Value.ToInt64();

            var versionFilter = direction == StreamReadDirection.Forward
                ? Builders<BsonDocument>.Filter.Gte(EventLogEntry.FieldNames.StreamVersion, versionValue)
                : Builders<BsonDocument>.Filter.Lte(EventLogEntry.FieldNames.StreamVersion, versionValue);

            filter &= versionFilter;
        }

        var sort = direction == StreamReadDirection.Forward
            ? Builders<BsonDocument>.Sort.Ascending(EventLogEntry.FieldNames.StreamVersion)
            : Builders<BsonDocument>.Sort.Descending(EventLogEntry.FieldNames.StreamVersion);

        using var cursor = await _eventLog
            .Find(filter)
            .Sort(sort)
            .Limit(options.MaxCount)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        var isEmpty = true;

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
            {
                isEmpty = false;

                var globalPosition = LogPosition.FromInt64(doc["_id"].AsInt64);
                var streamVersion = StreamVersion.FromInt64(doc[EventLogEntry.FieldNames.StreamVersion].AsInt64);
                var eventName = doc[EventLogEntry.FieldNames.EventName].AsString;
                var eventData = doc[EventLogEntry.FieldNames.EventData];
                var metadata = doc[EventLogEntry.FieldNames.Metadata];
                var writtenAtUtc = doc[EventLogEntry.FieldNames.WrittenAtUtc].AsBsonDateTime.ToUniversalTime();

                var (@event, decodedMetadata) = _decoder.Decode(eventName, eventData, metadata);
                var context = new ReadEventContext(streamId, streamVersion, writtenAtUtc, globalPosition);

                yield return ReadEvent.Create(@event, decodedMetadata, context);
            }
        }

        if (isEmpty && options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
            throw new StreamNotFoundException(streamId);
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

                await ProcessAppendBatchAsync(batch, ct).ConfigureAwait(false);
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

    private async Task ProcessAppendBatchAsync(List<PendingAppend> batch, CancellationToken ct)
    {
        while (batch.Count > 0)
        {
            try
            {
                _buffers.ClearAll();
                await PrepareAndPruneAsync(batch, ct).ConfigureAwait(false);
                var result = await CommitAsync(ct).ConfigureAwait(false);

                if (result is CommitResult.Success)
                    return;

                if (result is CommitResult.Conflict { IsPermanent: true } conflict)
                {
                    var conflictingAppend = conflict.ConflictingAppend;

                    var exception = new StreamAppendConflictException(
                        conflictingAppend.StreamId,
                        conflictingAppend.ExpectedState);

                    conflictingAppend.Ack.TrySetException(exception);
                    batch.Remove(conflictingAppend);
                }
            }
            catch (MongoException ex) when (ex.HasErrorLabel(MongoErrorLabels.TransientTransactionError))
            {
                // Transient error - retry the whole pending batch.
            }
        }
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

    private async Task<CommitResult> CommitAsync(CancellationToken ct)
    {
        var eventCount = _buffers.OutgoingEvents.Count;
        if (eventCount == 0)
            return new CommitResult.Success();

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: ct).ConfigureAwait(false);
        session.StartTransaction(MongoEventStoreClient2.TransactionOptions);

        try
        {
            var events = _buffers.OutgoingEvents;
            var startPosition = await ClaimPositionsAsync(session, eventCount, ct).ConfigureAwait(false);

            for (var i = 0; i < eventCount; i++)
                events[i]["_id"] = startPosition + i;

            await _eventLog
                .InsertManyAsync(session, events, new InsertManyOptions { IsOrdered = true }, ct)
                .ConfigureAwait(false);

            await session.CommitWithRetryOnUnknownResultAsync(ct).ConfigureAwait(false);
        }
        catch (MongoBulkWriteException ex) when (ex.WriteErrors.Any(x => x.Code == MongoErrorCodes.DuplicateKey))
        {
            await session.AbortTransactionAsync(ct).ConfigureAwait(false);

            var firstError = ex.WriteErrors.First(e => e.Code == MongoErrorCodes.DuplicateKey);
            var failedEvent = _buffers.OutgoingEvents[firstError.Index];
            var commitId = failedEvent[EventLogEntry.FieldNames.CommitId].AsGuid;
            var conflictingAppend = _buffers.OutgoingAppends[commitId];

            return new CommitResult.Conflict(conflictingAppend);
        }
        catch (Exception)
        {
            try
            {
                await session.AbortTransactionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Expected during shutdown/timeout.
                // Don't replace original failure - cancellation will eventually propagate.
            }
            catch (Exception abortEx)
            {
                // Log here
            }

            throw;
        }

        foreach (var append in _buffers.OutgoingAppends.Values)
            append.Ack.TrySetResult();

        return new CommitResult.Success();
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

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);
        return await _eventLog.Find(filter).AnyAsync(cancellationToken).ConfigureAwait(false);
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

    private abstract class CommitResult
    {
        public sealed class Success : CommitResult;

        public sealed class Conflict(PendingAppend conflictingAppend) : CommitResult
        {
            public PendingAppend ConflictingAppend { get; } = conflictingAppend;

            public bool IsPermanent => ConflictingAppend.ExpectedState.IsStreamDoesNotExist ||
                                       ConflictingAppend.ExpectedState.IsSpecificVersion;
        }
    }
}