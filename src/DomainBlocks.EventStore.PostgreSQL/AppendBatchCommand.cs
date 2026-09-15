using DomainBlocks.EventStore.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Commits a batch of append requests with one call to the <c>append_events</c> function and completes each request
/// from the per-request result rows.
/// </summary>
/// <remarks>
/// The command is executed in autocommit mode, so the function call is its own transaction. It must never be enlisted
/// in a caller-managed transaction: the sequence row lock must be released at the moment the batch commits.
/// </remarks>
internal sealed class AppendBatchCommand : IDisposable
{
    private readonly NpgsqlCommand _command;
    private readonly NpgsqlParameter<string[]> _streamIds;
    private readonly NpgsqlParameter<string[]> _expectedKinds;
    private readonly NpgsqlParameter<long?[]> _expectedVersions;
    private readonly NpgsqlParameter<Guid[]> _commitIds;
    private readonly NpgsqlParameter<int[]> _eventCounts;
    private readonly NpgsqlParameter<string[]> _eventNames;
    private readonly NpgsqlParameter<string?[]> _eventData;
    private readonly NpgsqlParameter<byte[]?[]> _eventDataBytes;
    private readonly NpgsqlParameter<string?[]> _metadata;

    public AppendBatchCommand(NpgsqlDataSource dataSource, SchemaObjectNames names)
    {
        // The expected kinds travel as text and are cast to the enum here; the status comes back as text. Both keep
        // the data source free of type mappings for the enums.
        _command = dataSource.CreateCommand(
            "SELECT request_index, status::text, observed_version " +
            $"FROM {names.AppendEventsFunction}($1, $2::{names.ExpectedStateKindType}[], $3, $4, $5, $6, $7, $8, $9)");

        _streamIds = new NpgsqlParameter<string[]> { NpgsqlDbType = NpgsqlDbType.Text.AsArray() };
        _expectedKinds = new NpgsqlParameter<string[]> { NpgsqlDbType = NpgsqlDbType.Text.AsArray() };
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
    /// Executes the batch and completes every request. Throws only if the whole batch failed, in which case no
    /// request has been completed and the caller is expected to fault them all.
    /// </summary>
    public async Task ExecuteAsync(IReadOnlyList<AppendRequest> batch, CancellationToken cancellationToken)
    {
        Fill(batch);

        await using var reader = await _command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var request = batch[reader.GetInt32(0)];
            var status = reader.GetString(1);

            switch (status)
            {
                case AppendProtocol.StatusAppended:
                case AppendProtocol.StatusDuplicate:
                    request.TryComplete();
                    break;

                case AppendProtocol.StatusConflict:
                    // A NULL observed version means the stream did not exist when the request was evaluated.
                    var observedState = reader.IsDBNull(2)
                        ? ObservedStreamState.DoesNotExist<StreamPosition>()
                        : ObservedStreamState.AtVersion(StreamPosition.FromInt64(reader.GetInt64(2)));

                    request.TryComplete(new StreamAppendConflictException<StreamPosition>(
                        request.StreamId,
                        request.ExpectedState,
                        observedState));

                    break;

                default:
                    request.TryComplete(new InvalidOperationException($"Unknown append status '{status}'."));
                    break;
            }
        }

        // The function returns exactly one row per request; anything left over is a protocol error.
        foreach (var request in batch)
        {
            if (!request.IsCompleted)
                request.TryComplete(new InvalidOperationException("The append function returned no result."));
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
        var expectedKinds = new string[requestCount];
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