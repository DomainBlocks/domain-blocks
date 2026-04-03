using System.Collections.Immutable;
using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public sealed class CommitTracker(
    MongoEventStoreNodeOptions options,
    ILogger<CommitTracker> logger) :
    IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
{
    private const string CommitPositionFieldPath =
        $"{LeaseDocument.FieldNames.State}.{LeaseState.FieldNames.CommitPosition}";

    private const string EpochFieldPath = LeaseDocument.FieldNames.Epoch;

    private readonly CollectionNamespace _eventLogNs = new(options.DatabaseName, options.EventLogCollectionName);

    private readonly CollectionNamespace _leasesNs = new(options.DatabaseName, options.LeasesCollectionName);

    private ImmutableArray<ICommitListener> _listeners = [];

    private readonly SortedDictionary<long, RecordedBatch> _recordedBatches = [];
    private long? _currentEpoch;

    public void AddListener(ICommitListener listener)
    {
        ImmutableInterlocked.Update(
            ref _listeners,
            static (current, item) => current.Add(item),
            listener);
    }

    ValueTask IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>.OnNextAsync(
        ChangeStreamDocument<BsonDocument> change,
        CancellationToken cancellationToken)
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
        var epoch = doc[EventLogEntry.FieldNames.Epoch].AsInt64;
        var eventName = doc[EventLogEntry.FieldNames.EventName].AsString;

        if (eventName != nameof(EventNames.AppendBatchRecorded))
            return;

        // Ignore batches from epochs we know are stale.
        if (epoch < _currentEpoch)
            return;

        if (_recordedBatches.TryGetValue(position, out var existing))
        {
            if (existing.Epoch == epoch)
            {
                logger.LogWarning(
                    "Duplicate batch received for position {Position} from epoch {Epoch}.",
                    position,
                    epoch);

                return;
            }

            if (existing.Epoch > epoch)
                return; // Out-of-order delivery - a newer leader already claimed this position.
        }

        _recordedBatches[position] = new RecordedBatch(
            epoch,
            doc[EventLogEntry.FieldNames.EventData].AsBsonDocument);
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

            var appendedCommitIds = batch.EventData[AppendBatchRecorded.FieldNames.AppendedCommitIds].AsBsonArray;
            var duplicateCommitIds = batch.EventData[AppendBatchRecorded.FieldNames.DuplicateCommitIds].AsBsonArray;
            var rejections = batch.EventData[AppendBatchRecorded.FieldNames.Rejections].AsBsonArray;

            foreach (var commitId in appendedCommitIds.Concat(duplicateCommitIds))
                NotifyCommitted(commitId.AsGuid);

            foreach (var rejection in rejections)
            {
                var commitId = rejection[CommitRejection.FieldNames.CommitId].AsGuid;
                NotifyCommitRejected(commitId, rejection);
            }

            toRemove.Add(position);
        }

        foreach (var position in toRemove)
            _recordedBatches.Remove(position);
    }

    private void NotifyCommitted(Guid commitId)
    {
        foreach (var listener in _listeners)
        {
            try
            {
                listener.OnCommitted(commitId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error invoking OnCommitted for commit ID {CommitId}", commitId);
            }
        }
    }

    private void NotifyCommitRejected(Guid commitId, BsonValue rejection)
    {
        foreach (var listener in _listeners)
        {
            try
            {
                listener.OnCommitRejected(commitId, rejection);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error invoking OnCommitRejected for commit ID {CommitId}", commitId);
            }
        }
    }

    private readonly struct RecordedBatch(long epoch, BsonDocument eventData)
    {
        public long Epoch { get; } = epoch;
        public BsonDocument EventData { get; } = eventData;
    }
}