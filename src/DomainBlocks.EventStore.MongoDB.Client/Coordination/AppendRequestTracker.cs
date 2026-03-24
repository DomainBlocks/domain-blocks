using System.Collections.Concurrent;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class AppendRequestTracker(
    EventStoreNamespaceSettings namespaceSettings,
    ILogger<AppendRequestTracker> logger) :
    IAppendRequestTracker,
    IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
{
    private const string CommitPositionFieldPath =
        $"{LeaseDocument.FieldNames.State}.{LeaseState.FieldNames.CommitPosition}";

    private const string EpochFieldPath = LeaseDocument.FieldNames.Epoch;

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _waiters = [];

    // Buffer: AppendBatchCompleted position → batch details (including epoch).
    // Only accessed from OnNextAsync (single writer from change stream producer).
    private readonly SortedDictionary<long, BufferedBatch> _bufferedBatches = [];

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
        if (change.CollectionNamespace.Equals(namespaceSettings.EventLogCollectionNamespace))
            HandleEventLogChange(change);

        else if (change.CollectionNamespace.Equals(namespaceSettings.LeasesCollectionNamespace))
            HandleLeaseChange(change);

        return ValueTask.CompletedTask;
    }

    private void HandleEventLogChange(ChangeStreamDocument<BsonDocument> change)
    {
        if (change.OperationType is not ChangeStreamOperationType.Insert and not ChangeStreamOperationType.Update)
            return;

        var doc = change.FullDocument;
        if (doc is null)
            return;

        var position = doc["_id"].AsInt64;
        var eventName = doc.GetValue(EventLogEntry.FieldNames.EventName, BsonNull.Value);

        if (eventName.AsString != "AppendBatchCompleted")
            return;

        var epoch = doc[EventLogEntry.FieldNames.Epoch].AsInt64;

        // Ignore batches from epochs we know are stale.
        if (epoch < _currentEpoch)
            return;

        var eventData = doc[EventLogEntry.FieldNames.EventData].AsBsonDocument;
        var batchCompleted = BsonSerializer.Deserialize<AppendBatchCompleted>(eventData);

        var batch = new BufferedBatch(epoch);

        foreach (var id in batchCompleted.Appends)
            batch.Committed.Add(id);

        foreach (var id in batchCompleted.Duplicates)
            batch.Committed.Add(id);

        foreach (var rejection in batchCompleted.Rejections)
            batch.Rejections.Add(rejection);

        _bufferedBatches[position] = batch;
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
                PurgeStaleBufferedBatches(epoch);
            }
        }

        // Advance commit position if present.
        if (!updatedFields.Contains(CommitPositionFieldPath))
            return;

        var commitPosition = updatedFields[CommitPositionFieldPath].AsInt64;

        FlushUpTo(commitPosition);
    }

    private void PurgeStaleBufferedBatches(long currentEpoch)
    {
        var toRemove = new List<long>();

        foreach (var (position, batch) in _bufferedBatches)
        {
            if (batch.Epoch < currentEpoch)
                toRemove.Add(position);
        }

        foreach (var position in toRemove)
            _bufferedBatches.Remove(position);
    }

    private void FlushUpTo(long commitPosition)
    {
        var toRemove = new List<long>();

        foreach (var (position, batch) in _bufferedBatches)
        {
            if (position > commitPosition)
                break; // SortedDictionary - everything after is also above

            // Final guard: skip batches from stale epochs that slipped through.
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

            foreach (var rejection in batch.Rejections)
            {
                if (_waiters.TryRemove(rejection.CommitId, out var tcs))
                {
                    tcs.TrySetException(new StreamAppendConflictException(
                        rejection.StreamId,
                        rejection.ExpectedStreamState,
                        rejection.ActualStreamState));
                }
            }

            toRemove.Add(position);
        }

        foreach (var position in toRemove)
            _bufferedBatches.Remove(position);
    }

    private sealed class BufferedBatch(long epoch)
    {
        public long Epoch { get; } = epoch;
        public HashSet<Guid> Committed { get; } = [];
        public List<CommitRejection> Rejections { get; } = [];
    }
}