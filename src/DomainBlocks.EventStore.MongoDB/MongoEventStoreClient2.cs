using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// An alternative event store client using a CAS sequence counter and multi-document transactions for batched writes.
/// Unlike <see cref="MongoEventStoreNode"/>, there is no single-writer leader, no lease, and no change streams —
/// any number of processes can write concurrently. Intended for benchmarking against the leader-based approach.
///
/// OCC correctness is enforced by a unique index on (streamId, streamVersion): if a concurrent writer commits to
/// the same stream between a batch's read phase and its transaction commit, the insertMany hits a duplicate key
/// error, the transaction aborts, and the batch is retried with fresh stream head versions.
/// </summary>
public sealed class MongoEventStoreClient2<TEvent> : IEventStoreClient<TEvent>, IAsyncDisposable
    where TEvent : notnull
{
    private const string SequenceDocId = "global";
    private const string SequenceField = "next";

    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly IMongoCollection<BsonDocument> _sequences;
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _decoder;
    private readonly int _batchSize;
    private readonly Channel<PendingWrite> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;

    public MongoEventStoreClient2(
        IMongoClient mongoClient,
        EventCodec<TEvent, BsonValue, BsonValue> eventCodec,
        MongoEventStoreClient2Options? options = null)
    {
        options ??= new MongoEventStoreClient2Options();
        _mongoClient = mongoClient;
        _encoder = eventCodec.Encoder;
        _decoder = eventCodec.Decoder;
        _batchSize = options.AppendBatchSize;

        var db = mongoClient
            .GetDatabase(options.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _eventLog = db.GetCollection<BsonDocument>(options.EventLogCollectionName);
        _sequences = db.GetCollection<BsonDocument>(options.SequencesCollectionName);

        _channel = Channel.CreateBounded<PendingWrite>(new BoundedChannelOptions(options.AppendQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false
        });

        _writerTask = Task.Run(() => RunWriterAsync(_cts.Token));
    }

    /// <summary>
    /// Creates the required indexes. Must be called once before any writes.
    /// The unique index on (streamId, streamVersion) is what enforces OCC correctness.
    /// </summary>
    public static async Task EnsureInitializedAsync(
        IMongoClient mongoClient,
        MongoEventStoreClient2Options options,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(options.DatabaseName);
        var eventLog = db.GetCollection<EventLogEntry>(options.EventLogCollectionName);

        CreateIndexModel<EventLogEntry>[] indexModels =
        [
            new(Builders<EventLogEntry>.IndexKeys
                    .Ascending(x => x.StreamId)
                    .Ascending(x => x.StreamVersion),
                new CreateIndexOptions { Unique = true }),
            new(Builders<EventLogEntry>.IndexKeys
                .Ascending(x => x.CommitId))
        ];

        await eventLog.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // IEventStoreClient
    // -------------------------------------------------------------------------

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AppendToStreamOptions();

        var encodedEvents = _encoder.Encode(events).ToList();
        if (encodedEvents.Count == 0)
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var write = new PendingWrite(options.CommitId, streamId, options.ExpectedState, encodedEvents, tcs);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        try
        {
            await _channel.Writer.WriteAsync(write, timeoutCts.Token).ConfigureAwait(false);
            await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Append request did not complete within {options.Timeout}.");
        }
    }

    public async IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= ReadStreamOptions.Default;

        // No commitPosition gate needed: transaction isolation means all visible data is committed.
        if (options.Position.IsStart && options.Direction == StreamReadDirection.Backward ||
            options.Position.IsEnd && options.Direction == StreamReadDirection.Forward)
        {
            if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }

        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);

        if (options.Position.IsSpecificVersion)
        {
            var versionValue = options.Position.Version.Value.ToInt64();
            var versionFilter = options.Direction == StreamReadDirection.Forward
                ? Builders<BsonDocument>.Filter.Gte(EventLogEntry.FieldNames.StreamVersion, versionValue)
                : Builders<BsonDocument>.Filter.Lte(EventLogEntry.FieldNames.StreamVersion, versionValue);
            filter &= versionFilter;
        }

        var sort = options.Direction == StreamReadDirection.Forward
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
        _channel.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);
        await _writerTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _cts.Dispose();
    }

    // -------------------------------------------------------------------------
    // Background writer loop
    // -------------------------------------------------------------------------

    private async Task RunWriterAsync(CancellationToken ct)
    {
        var batch = new List<PendingWrite>(_batchSize);

        while (true)
        {
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    return; // Channel completed normally.
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                FaultAll(batch, new OperationCanceledException(ct));
                return;
            }

            batch.Clear();

            while (batch.Count < _batchSize && _channel.Reader.TryRead(out var write))
                batch.Add(write);

            if (batch.Count == 0)
                continue;

            try
            {
                await ProcessBatchWithRetryAsync(batch, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                FaultAll(batch, new OperationCanceledException(ct));
                return;
            }
            catch (Exception ex)
            {
                // Fault this batch but keep the writer alive for subsequent batches.
                FaultAll(batch, ex);
            }
        }
    }

    private async Task ProcessBatchWithRetryAsync(IReadOnlyList<PendingWrite> batch, CancellationToken ct)
    {
        var pending = new List<PendingWrite>(batch);

        while (pending.Count > 0)
        {
            try
            {
                var conflict = await ProcessBatchAsync(pending, ct).ConfigureAwait(false);

                if (conflict is null)
                    return; // All writes resolved.

                if (IsPermanentConflict(conflict.Write.ExpectedState))
                {
                    // The specific version slot is permanently taken — fault immediately, don't retry.
                    conflict.Write.Tcs.TrySetException(new StreamAppendConflictException(
                        conflict.Write.StreamId, conflict.Write.ExpectedState, conflict.ActualState));

                    pending.Remove(conflict.Write);
                }
                // For Any / StreamExists: retry with fresh reads — the next version will be picked up.
            }
            catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError") ||
                                            ex.HasErrorLabel("UnknownTransactionCommitResult"))
            {
                // Transient error — retry the whole pending batch.
            }
        }
    }

    /// <summary>
    /// Returns a <see cref="ConflictInfo"/> describing the first DuplicateKey conflict detected during
    /// insert, or <c>null</c> if the batch committed successfully.
    /// Throws <see cref="MongoException"/> with a "TransientTransactionError" label on transient failures.
    /// </summary>
    private async Task<ConflictInfo?> ProcessBatchAsync(List<PendingWrite> pending, CancellationToken ct)
    {
        // Build distinct stream IDs and commit IDs for the parallel read phase.
        var streamIds = new BsonArray(pending.Select(w => (BsonValue)w.StreamId).Distinct());
        var commitIdsBson = new BsonArray(pending
            .Select(w => (BsonValue)new BsonBinaryData(w.CommitId, GuidRepresentation.Standard))
            .Distinct());

        // --- Round-trips 1 + 2: run concurrently, outside the transaction ---
        var existingCommitIdsTask = ReadExistingCommitIdsAsync(commitIdsBson, ct);
        var headVersionsTask = ReadHeadVersionsAsync(streamIds, ct);

        await Task.WhenAll(existingCommitIdsTask, headVersionsTask).ConfigureAwait(false);

        var existingCommitIds = existingCommitIdsTask.Result;
        var headVersions = headVersionsTask.Result;

        // --- In-memory OCC checks + build event doc list ---
        var writtenAtUtc = DateTime.UtcNow;
        var eventDocs = new List<BsonDocument>();
        var outcomes = new Dictionary<Guid, WriteOutcome>();

        // Track doc index → PendingWrite for DuplicateKey resolution below.
        var docIndexToWrite = new Dictionary<int, PendingWrite>();

        foreach (var write in pending)
        {
            if (outcomes.ContainsKey(write.CommitId))
                continue;

            if (existingCommitIds.Contains(write.CommitId))
            {
                outcomes[write.CommitId] = WriteOutcome.Duplicate;
                continue;
            }

            var streamVersion = headVersions.GetValueOrDefault(write.StreamId, -1L);
            var actualState = streamVersion < 0
                ? StreamState.StreamDoesNotExist
                : StreamState.StreamExists(StreamVersion.FromInt64(streamVersion));

            if (!write.ExpectedState.Matches(actualState))
            {
                outcomes[write.CommitId] = WriteOutcome.Conflicted(actualState);
                continue;
            }

            var commitIndex = 0;
            foreach (var e in write.EncodedEvents)
            {
                docIndexToWrite[eventDocs.Count] = write;
                eventDocs.Add(new BsonDocument
                {
                    { EventLogEntry.FieldNames.StreamId, write.StreamId },
                    { EventLogEntry.FieldNames.StreamVersion, ++streamVersion },
                    {
                        EventLogEntry.FieldNames.CommitId,
                        new BsonBinaryData(write.CommitId, GuidRepresentation.Standard)
                    },
                    { EventLogEntry.FieldNames.CommitIndex, commitIndex++ },
                    { EventLogEntry.FieldNames.EventName, e.EventName },
                    { EventLogEntry.FieldNames.EventData, e.EventData },
                    { EventLogEntry.FieldNames.Metadata, e.Metadata ?? BsonNull.Value },
                    { EventLogEntry.FieldNames.WrittenAtUtc, writtenAtUtc }
                });
            }

            headVersions[write.StreamId] = streamVersion;
            outcomes[write.CommitId] = WriteOutcome.Committed;
        }

        // Signal Duplicate and Conflicted outcomes now — they don't depend on the transaction.
        foreach (var write in pending)
        {
            if (!outcomes.TryGetValue(write.CommitId, out var outcome))
                continue;

            switch (outcome.Kind)
            {
                case WriteOutcomeKind.Duplicate:
                    write.Tcs.TrySetResult();
                    break;
                case WriteOutcomeKind.Conflicted:
                    write.Tcs.TrySetException(new StreamAppendConflictException(
                        write.StreamId, write.ExpectedState, outcome.ActualState!.Value));
                    break;
            }
        }

        // --- Transaction: claim positions → insertMany → commit ---
        using var session = await _mongoClient
            .StartSessionAsync(cancellationToken: ct)
            .ConfigureAwait(false);

        session.StartTransaction(new TransactionOptions(writeConcern: WriteConcern.WMajority));

        try
        {
            if (eventDocs.Count > 0)
            {
                var startPosition = await ClaimPositionsAsync(session, eventDocs.Count, ct)
                    .ConfigureAwait(false);

                for (var i = 0; i < eventDocs.Count; i++)
                    eventDocs[i]["_id"] = startPosition + i;

                // IsOrdered = true is required: MongoDB applies inserts to the oplog in submission order,
                // guaranteeing change stream consumers observe events in ascending _id (global position) order.
                await _eventLog
                    .InsertManyAsync(session, eventDocs, new InsertManyOptions { IsOrdered = true }, ct)
                    .ConfigureAwait(false);
            }

            await session.CommitTransactionAsync(ct).ConfigureAwait(false);
        }
        catch (MongoBulkWriteException<BsonDocument> ex)
            when (ex.WriteErrors.Any(e => e.Code == MongoErrorCodes.DuplicateKey))
        {
            try
            {
                await session.AbortTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                /* best effort */
            }

            // Identify the first conflicting doc and the write that owns it.
            var firstError = ex.WriteErrors.First(e => e.Code == MongoErrorCodes.DuplicateKey);
            var failedDoc = eventDocs[firstError.Index];
            var failedStreamVersion = failedDoc[EventLogEntry.FieldNames.StreamVersion].AsInt64;
            var failedWrite = docIndexToWrite[firstError.Index];
            var actualState = StreamState.StreamExists(StreamVersion.FromInt64(failedStreamVersion));

            return new ConflictInfo(failedWrite, actualState);
        }
        catch (Exception)
        {
            try
            {
                await session.AbortTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                /* best effort */
            }

            throw;
        }

        // Signal outcomes inline.
        // foreach (var write in pending)
        // {
        //     if (!outcomes.TryGetValue(write.CommitId, out var outcome))
        //         continue;
        //
        //     switch (outcome.Kind)
        //     {
        //         case WriteOutcomeKind.Committed:
        //         case WriteOutcomeKind.Duplicate:
        //             write.Tcs.TrySetResult();
        //             break;
        //         case WriteOutcomeKind.Conflicted:
        //             write.Tcs.TrySetException(new StreamAppendConflictException(
        //                 write.StreamId, write.ExpectedState, outcome.ActualState!.Value));
        //             break;
        //     }
        // }
        //
        // return null;

        // Signal Committed outcomes — only possible after successful commit.
        foreach (var write in pending)
        {
            if (outcomes.TryGetValue(write.CommitId, out var outcome) &&
                outcome.Kind == WriteOutcomeKind.Committed)
            {
                write.Tcs.TrySetResult();
            }
        }

        return null;
    }

    private static bool IsPermanentConflict(ExpectedStreamState state) =>
        state.IsSpecificVersion || state.IsStreamDoesNotExist;

    private sealed record ConflictInfo(PendingWrite Write, StreamState ActualState);

    // -------------------------------------------------------------------------
    // Read helpers (called outside the transaction)
    // -------------------------------------------------------------------------

    private async Task<HashSet<Guid>> ReadExistingCommitIdsAsync(BsonArray commitIds, CancellationToken ct)
    {
        var result = new HashSet<Guid>();
        var filter = new BsonDocument(
            EventLogEntry.FieldNames.CommitId,
            new BsonDocument("$in", commitIds));

        var cursor = await _eventLog
            .DistinctAsync<BsonValue>(EventLogEntry.FieldNames.CommitId, filter, cancellationToken: ct)
            .ConfigureAwait(false);

        await cursor.ForEachAsync(v => result.Add(v.AsGuid), ct).ConfigureAwait(false);
        return result;
    }

    private async Task<Dictionary<string, long>> ReadHeadVersionsAsync(BsonArray streamIds, CancellationToken ct)
    {
        var result = new Dictionary<string, long>();

        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument(
                EventLogEntry.FieldNames.StreamId,
                new BsonDocument("$in", streamIds))),
            new("$group", new BsonDocument
            {
                { "_id", $"${EventLogEntry.FieldNames.StreamId}" },
                { "version", new BsonDocument("$max", $"${EventLogEntry.FieldNames.StreamVersion}") }
            })
        ];

        await _eventLog
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .ForEachAsync(doc => result[doc["_id"].AsString] = doc["version"].AsInt64, ct)
            .ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Atomically claims <paramref name="count"/> positions from the global sequence counter,
    /// returning the start of the exclusive range [start, start + count).
    /// </summary>
    private async Task<long> ClaimPositionsAsync(IClientSessionHandle session, int count, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", SequenceDocId);
        var update = Builders<BsonDocument>.Update.Inc(SequenceField, (long)count);
        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            ReturnDocument = ReturnDocument.Before,
            IsUpsert = true,
            Projection = Builders<BsonDocument>.Projection.Include(SequenceField)
        };

        var doc = await _sequences
            .FindOneAndUpdateAsync(session, filter, update, options, ct)
            .ConfigureAwait(false);

        // doc is null when the sequence document is upserted for the first time — positions start at 0.
        if (doc is null || !doc.TryGetValue(SequenceField, out var next))
            return 0L;

        return next.AsInt64;
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);
        return await _eventLog.Find(filter).AnyAsync(ct).ConfigureAwait(false);
    }

    private static void FaultAll(IEnumerable<PendingWrite> writes, Exception ex)
    {
        foreach (var write in writes)
            write.Tcs.TrySetException(ex);
    }

    // -------------------------------------------------------------------------
    // Inner types
    // -------------------------------------------------------------------------

    private sealed class PendingWrite(
        Guid commitId,
        string streamId,
        ExpectedStreamState expectedState,
        IReadOnlyList<EncodedEvent<BsonValue, BsonValue>> encodedEvents,
        TaskCompletionSource tcs)
    {
        public Guid CommitId { get; } = commitId;
        public string StreamId { get; } = streamId;
        public ExpectedStreamState ExpectedState { get; } = expectedState;
        public IReadOnlyList<EncodedEvent<BsonValue, BsonValue>> EncodedEvents { get; } = encodedEvents;
        public TaskCompletionSource Tcs { get; } = tcs;
    }

    private enum WriteOutcomeKind
    {
        Committed,
        Duplicate,
        Conflicted
    }

    private readonly struct WriteOutcome
    {
        private WriteOutcome(WriteOutcomeKind kind, StreamState? actualState = null)
        {
            Kind = kind;
            ActualState = actualState;
        }

        public WriteOutcomeKind Kind { get; }
        public StreamState? ActualState { get; }

        public static WriteOutcome Committed { get; } = new(WriteOutcomeKind.Committed);
        public static WriteOutcome Duplicate { get; } = new(WriteOutcomeKind.Duplicate);
        public static WriteOutcome Conflicted(StreamState actualState) => new(WriteOutcomeKind.Conflicted, actualState);
    }
}