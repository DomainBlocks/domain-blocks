using System.Collections.Concurrent;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Schema;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class CommitTracker(
    MongoEventStoreClientOptions options,
    ILogger<CommitTracker> logger) :
    ICommitTracker,
    IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
{
    private const string CommitPositionFieldPath =
        $"{LeaseDocument.FieldNames.State}.{LeaseState.FieldNames.CommitPosition}";

    private const string EpochFieldPath = LeaseDocument.FieldNames.Epoch;

    private readonly CollectionNamespace _eventLogNs = new(options.DatabaseName, options.EventLogCollectionName);

    private readonly CollectionNamespace _leasesNs = new(options.DatabaseName, options.LeasesCollectionName);

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _waiters = [];

    // Buffer: AppendBatchCompleted position → batch details (including epoch).
    // Only accessed from OnNextAsync (single writer from change stream producer).
    private readonly SortedDictionary<long, RecordedBatch> _recordedBatches = [];

    // The current epoch as observed from lease changes. Null until the first lease update is seen.
    private long? _currentEpoch;

    public Task WaitAsync(Guid commitId, CancellationToken cancellationToken = default)
    {
        var tcs = _waiters.GetOrAdd(
            commitId,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        return tcs.Task.WaitAsync(cancellationToken);
    }

    public ValueTask OnNextAsync(ChangeStreamDocument<BsonDocument> change, CancellationToken cancellationToken)
    {
        if (change.CollectionNamespace.Equals(_eventLogNs))
            HandleEventLogChange(change);

        else if (change.CollectionNamespace.Equals(_leasesNs))
            HandleLeaseChange(change);

        return ValueTask.CompletedTask;
    }

    private void HandleEventLogChange(ChangeStreamDocument<BsonDocument> change)
    {
        if (change.OperationType is not ChangeStreamOperationType.Insert and not ChangeStreamOperationType.Replace)
            return;

        var doc = change.FullDocument;
        if (doc is null)
            return;

        var position = doc["_id"].AsInt64;
        var eventName = doc.GetValue(EventLogEntry.FieldNames.EventName, BsonNull.Value);

        if (eventName.AsString != nameof(AppendBatchRecorded))
            return;

        var epoch = doc[EventLogEntry.FieldNames.Epoch].AsInt64;

        // Ignore batches from epochs we know are stale.
        if (epoch < _currentEpoch)
            return;

        var eventData = doc[EventLogEntry.FieldNames.EventData].AsBsonDocument;
        var batch = new RecordedBatch(epoch);

        foreach (var value in eventData[AppendBatchRecorded.FieldNames.AppendedCommitIds].AsBsonArray)
            batch.Committed.Add(value.AsGuid);

        foreach (var value in eventData[AppendBatchRecorded.FieldNames.DuplicateCommitIds].AsBsonArray)
            batch.Committed.Add(value.AsGuid);

        foreach (var value in eventData[AppendBatchRecorded.FieldNames.Rejections].AsBsonArray)
            batch.Rejections.Add(value.AsBsonDocument);

        _recordedBatches[position] = batch;
    }

    private void HandleLeaseChange(ChangeStreamDocument<BsonDocument> change)
    {
        if (change.OperationType != ChangeStreamOperationType.Update)
            return;

        // Only care about the log lease.
        var resourceId = change.DocumentKey["_id"].AsString;
        if (resourceId != LeaseContender.ResourceId)
            return;

        var updatedFields = change.UpdateDescription?.UpdatedFields;
        if (updatedFields is null)
            return;

        // Detect epoch transitions and purge stale buffered batches.
        if (updatedFields.Contains(EpochFieldPath))
        {
            var epoch = updatedFields[EpochFieldPath].AsInt64;

            if (!_currentEpoch.HasValue || epoch > _currentEpoch.Value)
            {
                _currentEpoch = epoch;
                PurgeStaleBatches(epoch);
            }
        }

        // Advance commit position if present.
        if (!updatedFields.Contains(CommitPositionFieldPath))
            return;

        var commitPosition = updatedFields[CommitPositionFieldPath].AsInt64;

        FlushUpTo(commitPosition);
    }

    private void PurgeStaleBatches(long currentEpoch)
    {
        var toRemove = new List<long>();

        foreach (var (position, batch) in _recordedBatches)
        {
            if (batch.Epoch < currentEpoch)
                toRemove.Add(position);
        }

        foreach (var position in toRemove)
            _recordedBatches.Remove(position);
    }

    private void FlushUpTo(long commitPosition)
    {
        var toRemove = new List<long>();

        foreach (var (position, batch) in _recordedBatches)
        {
            if (position > commitPosition)
                break; // SortedDictionary - everything after is also above

            // Skip batches from stale epochs that slipped through.
            if (_currentEpoch.HasValue && batch.Epoch < _currentEpoch.Value)
            {
                toRemove.Add(position);
                continue;
            }

            foreach (var commitId in batch.Committed)
            {
                if (_waiters.TryRemove(commitId, out var tcs))
                    tcs.TrySetResult();
            }

            foreach (var rejDoc in batch.Rejections)
            {
                var commitId = rejDoc[CommitRejection.FieldNames.CommitId].AsGuid;

                if (_waiters.TryRemove(commitId, out var tcs))
                {
                    tcs.TrySetException(new StreamAppendConflictException(
                        streamId: rejDoc[CommitRejection.FieldNames.StreamId].AsString,
                        expectedState: ParseExpectedStreamState(
                            rejDoc[CommitRejection.FieldNames.ExpectedStreamState].AsBsonDocument),
                        actualState: ParseStreamState(
                            rejDoc[CommitRejection.FieldNames.ActualStreamState].AsBsonDocument)));
                }
            }

            toRemove.Add(position);
        }

        foreach (var position in toRemove)
            _recordedBatches.Remove(position);
    }

    private static ExpectedStreamState ParseExpectedStreamState(BsonDocument doc)
    {
        return doc["kind"].AsString switch
        {
            "any" => ExpectedStreamState.Any,
            "streamExists" => ExpectedStreamState.StreamExists,
            "streamDoesNotExist" => ExpectedStreamState.StreamDoesNotExist,
            "version" => ExpectedStreamState.SpecificVersion(StreamVersion.FromInt64(doc["version"].AsInt64)),
            var k => throw new InvalidOperationException($"Unknown expected stream state kind: '{k}'")
        };
    }

    private static StreamState ParseStreamState(BsonDocument doc)
    {
        return doc["kind"].AsString switch
        {
            "streamDoesNotExist" => StreamState.StreamDoesNotExist,
            "streamExists" => StreamState.StreamExists(StreamVersion.FromInt64(doc["version"].AsInt64)),
            var k => throw new InvalidOperationException($"Unknown stream state kind: '{k}'")
        };
    }

    private sealed class RecordedBatch(long epoch)
    {
        public long Epoch { get; } = epoch;
        public HashSet<Guid> Committed { get; } = [];
        public List<BsonDocument> Rejections { get; } = [];
    }
}