using Npgsql;
using NpgsqlTypes;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Commits a batch of append requests with one <c>append_events</c> call and completes each from its result row.
/// </summary>
/// <remarks>
/// <para>
/// The command runs in autocommit mode and must never join a caller's transaction, because the sequence row lock must
/// be released as soon as the batch commits.
/// </para>
/// <para>
/// The requests are completed only once the batch has committed. The server sends the result rows before it commits,
/// and flushes them as soon as they fill its output buffer, so the rows of a large batch reach the client while the
/// commit still waits for its flush and for the synchronous standbys. A batch that fails to commit completes no
/// request, so the caller can retry it.
/// </para>
/// </remarks>
internal sealed class AppendBatchCommand : IDisposable
{
    private readonly NpgsqlCommand _command;
    private readonly NpgsqlParameter<string[]> _streamIds;
    private readonly NpgsqlParameter<AppendProtocol.ExpectedKind[]> _expectedKinds;
    private readonly NpgsqlParameter<long?[]> _expectedVersions;
    private readonly NpgsqlParameter<Guid[]> _commitIds;
    private readonly NpgsqlParameter<int[]> _eventCounts;
    private readonly NpgsqlParameter<string[]> _eventNames;
    private readonly NpgsqlParameter<string?[]> _eventData;
    private readonly NpgsqlParameter<byte[]?[]> _eventDataBytes;
    private readonly NpgsqlParameter<string?[]> _metadata;

    public AppendBatchCommand(NpgsqlDataSource dataSource, SchemaObjectNames names)
    {
        _command = dataSource.CreateCommand(
            "SELECT request_index, status, observed_version " +
            $"FROM {names.AppendEventsFunction}($1, $2, $3, $4, $5, $6, $7, $8, $9)");

        _streamIds = new NpgsqlParameter<string[]> { NpgsqlDbType = NpgsqlDbType.Text.AsArray() };

        // Naming the type selects this schema's mapping when a data source maps several schemas.
        _expectedKinds = new NpgsqlParameter<AppendProtocol.ExpectedKind[]>
        {
            DataTypeName = names.ExpectedStateKindArrayType
        };

        _expectedVersions = new NpgsqlParameter<long?[]> { NpgsqlDbType = NpgsqlDbType.Bigint.AsArray() };
        _commitIds = new NpgsqlParameter<Guid[]> { NpgsqlDbType = NpgsqlDbType.Uuid.AsArray() };
        _eventCounts = new NpgsqlParameter<int[]> { NpgsqlDbType = NpgsqlDbType.Integer.AsArray() };
        _eventNames = new NpgsqlParameter<string[]> { NpgsqlDbType = NpgsqlDbType.Text.AsArray() };
        _eventData = new NpgsqlParameter<string?[]> { NpgsqlDbType = NpgsqlDbType.Jsonb.AsArray() };
        _eventDataBytes = new NpgsqlParameter<byte[]?[]> { NpgsqlDbType = NpgsqlDbType.Bytea.AsArray() };
        _metadata = new NpgsqlParameter<string?[]> { NpgsqlDbType = NpgsqlDbType.Jsonb.AsArray() };

        _command.Parameters.AddRange(new NpgsqlParameter[]
        {
            _streamIds,
            _expectedKinds,
            _expectedVersions,
            _commitIds,
            _eventCounts,
            _eventNames,
            _eventData,
            _eventDataBytes,
            _metadata
        });
    }

    /// <summary>
    /// Executes the batch and, once it has committed, completes each request from its result row. A request without a
    /// result row is completed with an error.
    /// </summary>
    public async Task ExecuteAsync(IReadOnlyList<AppendRequest> batch, CancellationToken cancellationToken)
    {
        Fill(batch);

        var outcomes = new Exception?[batch.Count];
        var hasOutcome = new bool[batch.Count];

        await using (var reader = await _command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var index = reader.GetInt32(0);
                outcomes[index] = ReadOutcome(reader, batch[index]);
                hasOutcome[index] = true;
            }

            // The server reports the commit only after the rows, so closing the reader waits for it, and throws if the
            // batch did not commit.
            await reader.CloseAsync().ConfigureAwait(false);
        }

        // The function returns exactly one row per request, so a request without a row means the protocol was broken.
        for (var i = 0; i < batch.Count; i++)
        {
            batch[i].TryComplete(hasOutcome[i]
                ? outcomes[i]
                : new InvalidOperationException("The append function returned no result."));
        }
    }

    // Returns the error to complete the request with, or null if it was appended or was a duplicate.
    private static Exception? ReadOutcome(NpgsqlDataReader reader, AppendRequest request)
    {
        var status = reader.GetFieldValue<AppendProtocol.Status>(1);

        switch (status)
        {
            case AppendProtocol.Status.Appended:
            case AppendProtocol.Status.Duplicate:
                return null;

            case AppendProtocol.Status.Conflict:
                // A NULL observed version means the stream did not exist when the request was evaluated.
                var observedState = reader.IsDBNull(2)
                    ? ObservedStreamState.DoesNotExist<StreamPosition>()
                    : ObservedStreamState.AtVersion(StreamPosition.FromInt64(reader.GetInt64(2)));

                return new StreamAppendConflictException<StreamPosition>(
                    request.StreamId,
                    request.ExpectedState,
                    observedState);

            default:
                return new InvalidOperationException($"Unknown append status '{status}'.");
        }
    }

    public void Dispose() => _command.Dispose();

    private void Fill(IReadOnlyList<AppendRequest> batch)
    {
        var requestCount = batch.Count;
        var eventCount = 0;

        for (var i = 0; i < requestCount; i++)
            eventCount += batch[i].Events.Length;

        var streamIds = new string[requestCount];
        var expectedKinds = new AppendProtocol.ExpectedKind[requestCount];
        var expectedVersions = new long?[requestCount];
        var commitIds = new Guid[requestCount];
        var eventCounts = new int[requestCount];
        var eventNames = new string[eventCount];
        var eventData = new string?[eventCount];
        var eventDataBytes = new byte[]?[eventCount];
        var metadata = new string?[eventCount];

        var eventIndex = 0;

        for (var i = 0; i < requestCount; i++)
        {
            var request = batch[i];
            var expectedState = request.ExpectedState;

            streamIds[i] = request.StreamId;
            expectedKinds[i] = AppendProtocol.ToExpectedKind(expectedState.Kind);
            expectedVersions[i] = expectedState.HasVersion ? checked((long)expectedState.Version.Value) : null;
            commitIds[i] = request.CommitId;
            eventCounts[i] = request.Events.Length;

            foreach (var (eventName, data, eventMetadata) in request.Events)
            {
                eventNames[eventIndex] = eventName;
                eventData[eventIndex] = data.IsJson ? data.Json : null;
                eventDataBytes[eventIndex] = data.IsBytes ? data.Bytes.GetArrayOrCopy() : null;
                metadata[eventIndex] = eventMetadata;
                eventIndex++;
            }
        }

        _streamIds.TypedValue = streamIds;
        _expectedKinds.TypedValue = expectedKinds;
        _expectedVersions.TypedValue = expectedVersions;
        _commitIds.TypedValue = commitIds;
        _eventCounts.TypedValue = eventCounts;
        _eventNames.TypedValue = eventNames;
        _eventData.TypedValue = eventData;
        _eventDataBytes.TypedValue = eventDataBytes;
        _metadata.TypedValue = metadata;
    }
}