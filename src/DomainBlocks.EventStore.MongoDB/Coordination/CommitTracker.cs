using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using LeaseDocument = DomainBlocks.EventStore.MongoDB.Schema.LeaseDocument;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public sealed class CommitTracker(
    MongoEventStoreNodeOptions options,
    CommitSubject commitSubject,
    ILogger<CommitTracker> logger) :
    IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
{
    private readonly CollectionNamespace _eventLogNs = new(options.DatabaseName, options.EventLogCollectionName);

    private readonly CollectionNamespace _leasesNs = new(options.DatabaseName, options.LeasesCollectionName);

    private readonly SortedDictionary<long, RecordedBatch> _recordedBatches = [];
    private long? _currentEpoch;

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

        _recordedBatches[position] = new RecordedBatch(epoch, doc[EventLogEntry.FieldNames.EventData]);
    }

    private void HandleLeaseChange(ChangeStreamDocument<BsonDocument> change)
    {
        if (change.OperationType != ChangeStreamOperationType.Update)
            return;

        // Only care about the log lease.
        var id = change.DocumentKey["_id"].AsString;
        if (id != LeaseDocument.LeaseId)
            return;

        var updatedFields = change.UpdateDescription?.UpdatedFields;
        if (updatedFields is null)
            return;

        // Detect epoch transitions and purge stale buffered batches.
        if (updatedFields.TryGetValue(LeaseDocument.FieldNames.Epoch, out var e))
        {
            var epoch = e.AsInt64;

            if (!_currentEpoch.HasValue || epoch > _currentEpoch.Value)
            {
                _currentEpoch = epoch;
                PurgeStaleBatches();
            }
        }

        // Advance commit position if present.
        if (!updatedFields.TryGetValue(LeaseDocument.FieldNames.CommitPosition, out var commitPosition))
            return;

        FlushUpTo(commitPosition.AsInt64);
    }

    private void PurgeStaleBatches()
    {
        var toRemove = new List<long>();

        foreach (var (position, batch) in _recordedBatches)
        {
            if (batch.Epoch < _currentEpoch)
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

            commitSubject.Notify(batch.EventData);

            toRemove.Add(position);
        }

        foreach (var position in toRemove)
            _recordedBatches.Remove(position);
    }

    private readonly struct RecordedBatch(long epoch, BsonValue eventData)
    {
        public long Epoch { get; } = epoch;
        public BsonValue EventData { get; } = eventData;
    }
}