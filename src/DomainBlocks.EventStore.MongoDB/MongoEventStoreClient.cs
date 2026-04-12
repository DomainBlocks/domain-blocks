using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Serialization;
using DomainBlocks.EventStore.MongoDB.Coordination;
using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal class MongoEventStoreClient<TEvent>(
    ChannelWriter<BsonDocument> requestWriter,
    IMongoCollection<BsonDocument> eventLog,
    IMongoCollection<LeaseDocument> leases,
    EventCodec<TEvent, BsonValue, BsonValue> eventCodec) :
    IEventStoreClient<TEvent>,
    ICommitObserver
    where TEvent : notnull
{
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _eventEncoder = eventCodec.Encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder = eventCodec.Decoder;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _pendingCommits = [];
    private long _commitPosition = -1;

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AppendToStreamOptions();

        var eventsArray = new BsonArray(
            _eventEncoder
                .Encode(events)
                .Select(x => new BsonDocument
                {
                    { PendingEvent.FieldNames.EventName, x.EventName },
                    { PendingEvent.FieldNames.EventData, x.EventData },
                    { PendingEvent.FieldNames.Metadata, x.Metadata ?? BsonNull.Value }
                }));

        var request = new BsonDocument
        {
            { AppendRequest.FieldNames.CommitId, new BsonBinaryData(options.CommitId, GuidRepresentation.Standard) },
            { AppendRequest.FieldNames.StreamId, streamId },
            { AppendRequest.FieldNames.ExpectedStreamState, BsonDocument.From(options.ExpectedState) },
            { AppendRequest.FieldNames.Events, eventsArray },
            { AppendRequest.FieldNames.CreatedAtUtc, DateTime.UtcNow }
        };

        var tcs = _pendingCommits.GetOrAdd(
            options.CommitId,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        try
        {
            await requestWriter.WriteAsync(request, timeoutCts.Token);
            await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller canceled.
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Append request did not complete within {options.Timeout}.");
        }
        finally
        {
            _pendingCommits.TryRemove(options.CommitId, out _);
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

        var commitPosition = await GetCommitPositionAsync(cancellationToken).ConfigureAwait(false);

        // Nothing has been committed yet - the log is empty.
        if (commitPosition is null)
        {
            if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
                throw new StreamNotFoundException(streamId);

            yield break;
        }

        // Edge cases that represent an empty sequence of events.
        if (position.IsStart && direction == StreamReadDirection.Backward ||
            position.IsEnd && direction == StreamReadDirection.Forward)
        {
            if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, commitPosition.Value, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }

        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId) &
                     Builders<BsonDocument>.Filter.Lte("_id", commitPosition.Value);

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

        using var cursor = await eventLog
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

                var (@event, decodedMetadata) = _eventDecoder.Decode(eventName, eventData, metadata);
                var context = new ReadEventContext(streamId, streamVersion, writtenAtUtc, globalPosition);

                yield return ReadEvent.Create(@event, decodedMetadata, context);
            }
        }

        if (isEmpty && options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
            throw new StreamNotFoundException(streamId);
    }

    void ICommitObserver.OnCommitPositionAdvanced(long commitPosition)
    {
        AdvanceCommitPosition(commitPosition);
    }

    void ICommitObserver.OnCommitted(Guid commitId)
    {
        if (_pendingCommits.TryGetValue(commitId, out var tcs))
            tcs.TrySetResult();
    }

    void ICommitObserver.OnConflictRejected(Guid commitId, BsonValue conflict)
    {
        if (!_pendingCommits.TryGetValue(commitId, out var tcs))
            return;

        var streamId = conflict[AppendConflict.FieldNames.StreamId].AsString;
        var expectedStreamState = conflict[AppendConflict.FieldNames.ExpectedStreamState].ToExpectedStreamState();
        var actualStreamState = conflict[AppendConflict.FieldNames.ActualStreamState].ToStreamState();

        tcs.TrySetException(new StreamAppendConflictException(streamId, expectedStreamState, actualStreamState));
    }

    private void AdvanceCommitPosition(long commitPosition)
    {
        // Only advance, never go backwards.
        long current;
        do
        {
            current = Volatile.Read(ref _commitPosition);
        } while (commitPosition > current &&
                 Interlocked.CompareExchange(ref _commitPosition, commitPosition, current) != current);
    }

    private ValueTask<long?> GetCommitPositionAsync(CancellationToken cancellationToken)
    {
        var commitPosition = Volatile.Read(ref _commitPosition);

        return commitPosition >= 0
            ? ValueTask.FromResult<long?>(commitPosition)
            : new ValueTask<long?>(FallbackAsync());

        async Task<long?> FallbackAsync()
        {
            var lease = await leases
                .Find(Builders<LeaseDocument>.Filter.Eq(x => x.Id, LeaseDocument.LeaseId))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var leaseCommitPosition = lease?.CommitPosition;
            if (leaseCommitPosition is null or -1)
                return null;

            // Ensure _commitPosition is at least leaseCommitPosition to avoid a lagging change stream setting a lower
            // value ahead of a caller's next read, i.e. sequential reads should never go back in time. In practice this
            // scenario is expected to be rare.
            AdvanceCommitPosition(leaseCommitPosition.Value);

            return leaseCommitPosition.Value;
        }
    }

    private async Task<bool> StreamExistsAsync(
        string streamId,
        long commitPosition,
        CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId) &
                     Builders<BsonDocument>.Filter.Lte("_id", commitPosition);

        return await eventLog.Find(filter).AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}