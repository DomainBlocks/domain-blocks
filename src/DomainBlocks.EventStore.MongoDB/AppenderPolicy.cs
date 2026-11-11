using DomainBlocks.MongoDB.Sequencing;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal sealed class AppenderPolicy(IMongoCollection<BsonDocument> eventLog) :
    IMongoSequencedAppenderPolicy<AppendContext>
{
    private readonly PreCommitQuery _preCommitQuery = new(eventLog);
    private readonly Buffers _buffers = new();

    public async Task OnBatchCommittingAsync(
        IReadOnlyList<AppendRequest<AppendContext>> batch,
        CancellationToken cancellationToken)
    {
        _buffers.ClearAll();
        _preCommitQuery.Reset();

        foreach (var request in batch)
        {
            var firstEvent = request.Documents[0];
            _preCommitQuery.AddStreamId(firstEvent[EventLogEntry.FieldNames.StreamId]);

            // Only commit IDs that callers supplied need looking up. A generated commit ID is new, and this method only
            // runs before its batch commits, so the log can't hold it yet.
            if (!request.Context.IsCommitIdGenerated)
                _preCommitQuery.AddCommitId(firstEvent[EventLogEntry.FieldNames.CommitId]);
        }

        await _preCommitQuery
            .ExecuteIntoAsync(_buffers.ExistingCommitIds, _buffers.HeadStreamVersions, cancellationToken)
            .ConfigureAwait(false);

        var writtenAtUtc = DateTime.UtcNow;

        foreach (var request in batch)
        {
            if (_buffers.ExistingCommitIds.Contains(request.Context.CommitId))
            {
                request.TryComplete();
                continue;
            }

            var streamId = request.Context.StreamId;
            var expectedStreamState = request.Context.ExpectedStreamState;
            var streamVersion = _buffers.HeadStreamVersions.GetValueOrDefault(streamId, -1L);

            var observedStreamState = streamVersion < 0
                ? ObservedStreamState.DoesNotExist<StreamPosition>()
                : ObservedStreamState.AtVersion(StreamPosition.FromInt64(streamVersion));

            if (!expectedStreamState.Matches(observedStreamState))
            {
                request.TryComplete(new StreamAppendConflictException<StreamPosition>(
                    streamId,
                    expectedStreamState,
                    observedStreamState));

                continue;
            }

            foreach (var doc in request.Documents)
            {
                doc[EventLogEntry.FieldNames.StreamPosition] = ++streamVersion;
                doc[EventLogEntry.FieldNames.CreatedAtUtc] = writtenAtUtc;
            }

            _buffers.HeadStreamVersions[streamId] = streamVersion;
        }
    }

    public ConflictResolution OnConflict(AppendConflict<AppendContext> conflict)
    {
        // MongoDB does not populate WriteError.Details for duplicate key errors (code 11000). The only signal is the
        // error message, which includes the index name. Matching on it is fragile, but it is the only option that the
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
        var canRetry = expectedStreamState.Kind is ExpectedStreamStateKind.Any or ExpectedStreamStateKind.Exists;

        return canRetry
            ? ConflictResolution.Retry
            : ConflictResolution.Fail(new StreamAppendConflictException<StreamPosition>(
                conflict.Context.StreamId,
                expectedStreamState,
                innerException: conflict.OriginatingException));
    }

    private sealed class Buffers
    {
        public readonly HashSet<Guid> ExistingCommitIds = [];
        public readonly Dictionary<string, long> HeadStreamVersions = [];

        public void ClearAll()
        {
            ExistingCommitIds.Clear();
            HeadStreamVersions.Clear();
        }
    }
}