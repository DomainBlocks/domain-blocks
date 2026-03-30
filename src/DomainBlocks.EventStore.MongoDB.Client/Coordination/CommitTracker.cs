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
    private readonly SortedDictionary<long, RecordedBatch> _recordedBatches = [];
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

        var entry = new EventLogEntryView(doc);

        if (entry.EventName != nameof(EventNames.AppendBatchRecorded))
            return;

        // Ignore batches from epochs we know are stale.
        if (entry.Epoch < _currentEpoch)
            return;

        if (_recordedBatches.TryGetValue(entry.Position, out var existing))
        {
            if (existing.Epoch == entry.Epoch)
            {
                logger.LogWarning(
                    "Duplicate batch received for position {Position} from epoch {Epoch}.",
                    entry.Position,
                    entry.Epoch);

                return;
            }

            if (existing.Epoch > entry.Epoch)
                return; // Out-of-order delivery - a newer leader already claimed this position.
        }

        _recordedBatches[entry.Position] = new RecordedBatch(
            entry.Epoch,
            new AppendBatchRecordedView(entry.EventData.AsBsonDocument));
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

            foreach (var commitId in batch.Event.AppendedCommitIds.Concat(batch.Event.DuplicateCommitIds))
            {
                if (_waiters.TryRemove(commitId, out var tcs))
                    tcs.TrySetResult();
            }

            foreach (var rejection in batch.Event.Rejections)
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
            _recordedBatches.Remove(position);
    }

    private readonly struct RecordedBatch(long epoch, AppendBatchRecordedView @event)
    {
        public long Epoch { get; } = epoch;
        public AppendBatchRecordedView Event { get; } = @event;
    }
}