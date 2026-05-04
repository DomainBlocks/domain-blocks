using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
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
        IAppendCompletionSource<AppendToStreamContext> completionSource,
        CancellationToken cancellationToken)
    {
        _buffers.ClearAll();
        _preCommitQuery.Reset();

        foreach (var append in batch)
        {
            var firstEvent = append.Documents[0];
            var bsonCommitId = firstEvent[EventLogEntry.FieldNames.CommitId];
            var bsonStreamId = firstEvent[EventLogEntry.FieldNames.StreamId];
            _preCommitQuery.AddInput(bsonCommitId, bsonStreamId);
        }

        await _preCommitQuery
            .ExecuteIntoAsync(_buffers.ExistingCommitIds, _buffers.HeadStreamVersions, cancellationToken)
            .ConfigureAwait(false);

        var writtenAtUtc = DateTime.UtcNow;

        foreach (var append in batch)
        {
            var commitId = append.Context.CommitId;

            if (!_buffers.SeenCommitIds.Add(commitId))
                continue;

            if (_buffers.ExistingCommitIds.Contains(commitId))
            {
                completionSource.TryComplete(append);
                continue;
            }

            var streamId = append.Context.StreamId;
            var expectedState = append.Context.ExpectedState;
            var streamVersion = _buffers.HeadStreamVersions.GetValueOrDefault(streamId, -1L);

            var actualState = streamVersion < 0
                ? StreamState.StreamDoesNotExist
                : StreamState.StreamExists(StreamVersion.FromInt64(streamVersion));

            if (!expectedState.Matches(actualState))
            {
                completionSource.TryComplete(
                    append,
                    new StreamAppendConflictException(streamId, expectedState, actualState));

                continue;
            }

            foreach (var eventDoc in append.Documents)
            {
                eventDoc[EventLogEntry.FieldNames.StreamVersion] = ++streamVersion;
                eventDoc[EventLogEntry.FieldNames.WrittenAtUtc] = writtenAtUtc;
            }

            _buffers.HeadStreamVersions[streamId] = streamVersion;
        }
    }

    public ConflictResolution OnConflict(
        AppendEntry<AppendToStreamContext> conflictingAppend,
        AppendConflictInfo conflictInfo)
    {
        // MongoDB does not populate WriteError.Details for duplicate key errors (code 11000). The only available signal
        // is the error message, which includes the index name. This is potentially fragile, but is the only option the
        // driver exposes.
        if (conflictInfo.Message?.Contains(EventLogIndexNames.UniqueStreamVersion) is not true)
        {
            var message = conflictInfo.Message is not null ? $"'{conflictInfo.Message}'" : "none";

            return ConflictResolution.Fail(
                new AppendConflictException(
                    $"Unknown conflict. Message: {message}.",
                    conflictInfo.OriginatingException));
        }

        var isRetryable = conflictingAppend.Context.ExpectedState.IsAny ||
                          conflictingAppend.Context.ExpectedState.IsStreamExists;

        if (isRetryable)
            return ConflictResolution.Retry;

        return ConflictResolution.Fail(
            new StreamAppendConflictException(
                conflictingAppend.Context.StreamId,
                conflictingAppend.Context.ExpectedState,
                innerException: conflictInfo.OriginatingException));
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