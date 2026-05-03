using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.MongoDB.Sequencing;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class AppendToStreamPolicy(IMongoCollection<BsonDocument> eventLog) :
    IMongoSequencedAppenderPolicy<AppendToStreamContext>
{
    private readonly PreAppendQuery _preAppendQuery = new(eventLog);
    private readonly Buffers _buffers = new();

    public async ValueTask OnBatchCommittingAsync(
        IReadOnlyList<AppendEntry<AppendToStreamContext>> batch,
        CancellationToken cancellationToken)
    {
        _buffers.ClearAll();
        _preAppendQuery.Reset();

        foreach (var append in batch)
        {
            var firstEvent = append.Documents[0];
            var bsonCommitId = firstEvent[EventLogEntry.FieldNames.CommitId];
            var bsonStreamId = firstEvent[EventLogEntry.FieldNames.StreamId];
            _preAppendQuery.AddInput(bsonCommitId, bsonStreamId);
        }

        await _preAppendQuery
            .ExecuteAsync(_buffers.ExistingCommitIds, _buffers.HeadStreamVersions, cancellationToken)
            .ConfigureAwait(false);

        var writtenAtUtc = DateTime.UtcNow;

        foreach (var append in batch)
        {
            var commitId = append.Context.CommitId;

            if (!_buffers.SeenCommitIds.Add(commitId))
                continue;

            if (_buffers.ExistingCommitIds.Contains(commitId))
            {
                append.TryComplete();
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
                append.TryComplete(new StreamAppendConflictException(streamId, expectedState, actualState));
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

    public void OnConflict(AppendEntry<AppendToStreamContext> conflict)
    {
        var isPermanentConflict = conflict.Context.ExpectedState.IsStreamDoesNotExist ||
                                  conflict.Context.ExpectedState.IsSpecificVersion;

        if (!isPermanentConflict)
            return;

        var exception = new StreamAppendConflictException(
            conflict.Context.StreamId,
            conflict.Context.ExpectedState);

        conflict.TryComplete(exception);
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