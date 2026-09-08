using System.Runtime.InteropServices;
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
    private readonly NpgsqlParameter<short[]> _expectedKinds;
    private readonly NpgsqlParameter<long?[]> _expectedVersions;
    private readonly NpgsqlParameter<Guid[]> _commitIds;
    private readonly NpgsqlParameter<int[]> _eventCounts;
    private readonly NpgsqlParameter<string[]> _eventNames;
    private readonly NpgsqlParameter<string?[]> _eventData;
    private readonly NpgsqlParameter<byte[]?[]> _eventDataBytes;
    private readonly NpgsqlParameter<string?[]> _metadata;

    public AppendBatchCommand(NpgsqlDataSource dataSource, SqlNames names)
    {
        _command = dataSource.CreateCommand(
            "SELECT request_index, status, observed_kind, observed_version " +
            $"FROM {names.AppendEventsFunction}($1, $2, $3, $4, $5, $6, $7, $8, $9)");

        _streamIds = new NpgsqlParameter<string[]> { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text };
        _expectedKinds = new NpgsqlParameter<short[]> { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Smallint };
        _expectedVersions = new NpgsqlParameter<long?[]> { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bigint };
        _commitIds = new NpgsqlParameter<Guid[]> { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid };
        _eventCounts = new NpgsqlParameter<int[]> { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer };
        _eventNames = new NpgsqlParameter<string[]> { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text };
        _eventData = new NpgsqlParameter<string?[]> { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb };
        _eventDataBytes = new NpgsqlParameter<byte[]?[]> { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bytea };
        _metadata = new NpgsqlParameter<string?[]> { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb };

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
            var status = reader.GetInt16(1);

            switch (status)
            {
                case AppendProtocol.StatusAppended:
                case AppendProtocol.StatusDuplicate:
                    request.TryComplete();
                    break;

                case AppendProtocol.StatusConflict:
                    var observedState = reader.GetInt16(2) == AppendProtocol.ObservedAtVersion
                        ? ObservedStreamState.AtVersion(StreamPosition.FromInt64(reader.GetInt64(3)))
                        : ObservedStreamState.DoesNotExist<StreamPosition>();

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
        var expectedKinds = new short[requestCount];
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
                eventDataBytes[eventIndex] = data.IsBytes ? ToArray(data.Bytes) : null;
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

    private static byte[] ToArray(ReadOnlyMemory<byte> bytes)
    {
        // Avoid a copy when the memory is a whole array.
        return MemoryMarshal.TryGetArray(bytes, out var segment) &&
               segment.Offset == 0 &&
               segment.Array is { } wholeArray &&
               wholeArray.Length == segment.Count
            ? wholeArray
            : bytes.ToArray();
    }
}
