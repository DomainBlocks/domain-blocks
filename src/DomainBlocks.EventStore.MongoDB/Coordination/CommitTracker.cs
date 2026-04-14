using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using LeaseDocument = DomainBlocks.EventStore.MongoDB.Schema.LeaseDocument;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed partial class CommitTracker(
    MongoEventStoreNodeOptions options,
    CommitSubject commitSubject,
    ILogger<CommitTracker> logger) :
    IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
{
    private readonly CollectionNamespace _eventLogNs = new(options.DatabaseName, options.EventLogCollectionName);
    private readonly CollectionNamespace _leasesNs = new(options.DatabaseName, options.LeasesCollectionName);
    private readonly SortedDictionary<long, PendingEntry> _pendingEntries = [];
    private readonly List<long> _positionsBuffer = [];
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

        var epoch = doc[EventLogEntry.FieldNames.Epoch].AsInt64;

        if (!_currentEpoch.HasValue)
            _currentEpoch = epoch;
        else if (epoch < _currentEpoch)
            return; // Ignore entries from stale epochs.

        var position = doc["_id"].AsInt64;
        var eventName = doc[EventLogEntry.FieldNames.EventName].AsString;

        if (eventName is EventNames.DuplicatesSkipped or EventNames.ConflictsRejected)
            HandleSentinelEntry(position, epoch, eventName, doc);
        else
            HandleDomainEventEntry(position, epoch, doc);
    }

    private void HandleSentinelEntry(long position, long epoch, string eventName, BsonDocument doc)
    {
        if (_pendingEntries.TryGetValue(position, out var existing))
        {
            if (existing.Epoch == epoch)
            {
                LogPositionObservedMoreThanOnce(position, epoch);
                return;
            }

            if (existing.Epoch > epoch)
                return; // A newer leader already claimed this position.

            // A newer epoch is overwriting this position - evict the old entry.
            _pendingEntries.Remove(position);
        }

        var eventData = doc[EventLogEntry.FieldNames.EventData];
        _pendingEntries[position] = PendingEntry.ForSentinel(epoch, eventName, eventData);
    }

    private void HandleDomainEventEntry(long position, long epoch, BsonDocument doc)
    {
        var commitId = doc[EventLogEntry.FieldNames.CommitId].AsGuid;

        if (_pendingEntries.TryGetValue(position, out var existing))
        {
            if (existing.Epoch == epoch)
            {
                LogPositionObservedMoreThanOnce(position, epoch);
                return;
            }

            if (existing.Epoch > epoch)
                return; // A newer leader already claimed this position.

            // A newer epoch is overwriting this position - evict the old entry.
            _pendingEntries.Remove(position);
        }

        _pendingEntries[position] = PendingEntry.ForCommit(epoch, commitId);
    }

    private void HandleLeaseChange(ChangeStreamDocument<BsonDocument> change)
    {
        if (change.OperationType != ChangeStreamOperationType.Update)
            return;

        var id = change.DocumentKey["_id"].AsString;
        if (id != LeaseDocument.LeaseId)
            return;

        var updatedFields = change.UpdateDescription?.UpdatedFields;
        if (updatedFields is null)
            return;

        // Epoch changed (new leaseholder)
        if (updatedFields.TryGetValue(LeaseDocument.FieldNames.Epoch, out var e))
        {
            var epoch = e.AsInt64;

            if (!_currentEpoch.HasValue || epoch > _currentEpoch.Value)
            {
                LogEpochChanged(_currentEpoch, epoch);
                _currentEpoch = epoch;
                PurgeStaleEntries();
            }
        }

        // Commit position advanced
        if (updatedFields.TryGetValue(LeaseDocument.FieldNames.CommitPosition, out var commitPosition))
        {
            FlushUpTo(commitPosition.AsInt64);
        }
    }

    private void PurgeStaleEntries()
    {
        _positionsBuffer.Clear();

        foreach (var (position, entry) in _pendingEntries)
        {
            if (entry.Epoch < _currentEpoch)
                _positionsBuffer.Add(position);
        }

        foreach (var position in _positionsBuffer)
            _pendingEntries.Remove(position);
    }

    private void FlushUpTo(long commitPosition)
    {
        _positionsBuffer.Clear();

        commitSubject.NotifyCommitPositionAdvanced(commitPosition);

        foreach (var (position, entry) in _pendingEntries)
        {
            if (position > commitPosition)
                break;

            _positionsBuffer.Add(position);

            // Skip stale entries that slipped through before purge.
            if (_currentEpoch.HasValue && entry.Epoch < _currentEpoch.Value)
                continue;

            if (entry.CommitId.HasValue)
                commitSubject.NotifyCommitted(entry.CommitId.Value);
            else if (entry.EventName == EventNames.DuplicatesSkipped)
                commitSubject.NotifyDuplicatesSkipped(entry.EventData!);
            else if (entry.EventName == EventNames.ConflictsRejected)
                commitSubject.NotifyConflictsRejected(entry.EventData!);
        }

        foreach (var position in _positionsBuffer)
            _pendingEntries.Remove(position);
    }

    [LoggerMessage(LogLevel.Warning, "Position {Position} observed more than once for epoch {Epoch}; ignoring")]
    partial void LogPositionObservedMoreThanOnce(long position, long epoch);

    [LoggerMessage(LogLevel.Information, "Epoch changed from {OldEpoch} to {NewEpoch}")]
    partial void LogEpochChanged(long? oldEpoch, long newEpoch);

    private sealed class PendingEntry
    {
        private PendingEntry(long epoch, Guid? commitId, string? eventName, BsonValue? eventData)
        {
            Epoch = epoch;
            CommitId = commitId;
            EventName = eventName;
            EventData = eventData;
        }

        public long Epoch { get; }
        public Guid? CommitId { get; }
        public string? EventName { get; }
        public BsonValue? EventData { get; }

        public static PendingEntry ForCommit(long epoch, Guid commitId) =>
            new(epoch, commitId, null, null);

        public static PendingEntry ForSentinel(long epoch, string eventName, BsonValue eventData) =>
            new(epoch, null, eventName, eventData);
    }
}