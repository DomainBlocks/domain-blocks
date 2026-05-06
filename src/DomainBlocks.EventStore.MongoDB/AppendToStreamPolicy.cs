using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.MongoDB.Sequencing;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class AppendToStreamPolicy(IMongoCollection<BsonDocument> eventLog) :
    IMongoSequencedAppenderPolicy<AppendToStreamContext>
{
    private readonly PreCommitQuery _preCommitQuery = new(eventLog);
    private readonly Buffers _buffers = new();

    public async ValueTask OnBatchCommittingAsync(
        IReadOnlyList<AppendEntry<AppendToStreamContext>> batch,
        CancellationToken cancellationToken)
    {
        _buffers.ClearAll();
        _preCommitQuery.Reset();

        foreach (var entry in batch)
        {
            var firstEvent = entry.Documents[0];
            var bsonCommitId = firstEvent[EventLogEntry.FieldNames.CommitId];
            var bsonStreamId = firstEvent[EventLogEntry.FieldNames.StreamId];
            _preCommitQuery.AddInput(bsonCommitId, bsonStreamId);
        }

        await _preCommitQuery
            .ExecuteIntoAsync(_buffers.ExistingCommitIds, _buffers.HeadStreamVersions, cancellationToken)
            .ConfigureAwait(false);

        var writtenAtUtc = DateTime.UtcNow;

        foreach (var entry in batch)
        {
            var commitId = entry.Context.CommitId;

            if (!_buffers.SeenCommitIds.Add(commitId))
                continue;

            if (_buffers.ExistingCommitIds.Contains(commitId))
            {
                entry.TryComplete();
                continue;
            }

            var streamId = entry.Context.StreamId;
            var expectedStreamState = entry.Context.ExpectedStreamState;
            var streamVersion = _buffers.HeadStreamVersions.GetValueOrDefault(streamId, -1L);

            var actualStreamState = streamVersion < 0
                ? StreamState.StreamDoesNotExist
                : StreamState.StreamExists(StreamVersion.FromInt64(streamVersion));

            if (!expectedStreamState.Matches(actualStreamState))
            {
                entry.TryComplete(new StreamAppendConflictException(streamId, expectedStreamState, actualStreamState));
                continue;
            }

            foreach (var eventDoc in entry.Documents)
            {
                eventDoc[EventLogEntry.FieldNames.StreamVersion] = ++streamVersion;
                eventDoc[EventLogEntry.FieldNames.WrittenAtUtc] = writtenAtUtc;
            }

            _buffers.HeadStreamVersions[streamId] = streamVersion;
        }
    }

    public ConflictResolution OnConflict(ConflictingAppendEntry<AppendToStreamContext> conflict)
    {
        // MongoDB does not populate WriteError.Details for duplicate key errors (code 11000). The only available signal
        // is the error message, which includes the index name. This is potentially fragile, but is the only option the
        // driver exposes.
        if (conflict.ErrorMessage?.Contains(EventLogIndexNames.UniqueStreamVersion) is not true)
        {
            var message = conflict.ErrorMessage is not null ? $"'{conflict.ErrorMessage}'" : "none";

            return ConflictResolution.Fail(
                new AppendConflictException(
                    $"Unknown conflict. Message: {message}.",
                    conflict.OriginatingException));
        }

        var expectedStreamState = conflict.Context.ExpectedStreamState;
        var canRetry = expectedStreamState.IsAny || expectedStreamState.IsStreamExists;

        return canRetry
            ? ConflictResolution.Retry
            : ConflictResolution.Fail(
                new StreamAppendConflictException(
                    conflict.Context.StreamId,
                    expectedStreamState,
                    innerException: conflict.OriginatingException));
    }

    private sealed class Buffers
    {
        public readonly HashSet<Guid> SeenCommitIds = [];
        public readonly HashSet<Guid> ExistingCommitIds = [];
        public readonly Dictionary<string, long> HeadStreamVersions = [];

        public void ClearAll()
        {
            SeenCommitIds.Clear();
            ExistingCommitIds.Clear();
            HeadStreamVersions.Clear();
        }
    }
}