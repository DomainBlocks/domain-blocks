using Npgsql;
using NpgsqlTypes;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Calls the append_events function directly, so that its contract can be tested independently of the event store.
/// </summary>
public sealed class AppendFunctionClient(NpgsqlDataSource dataSource, string schema)
{
    public static Request Any(string streamId, params Event[] events) =>
        new(streamId, AppendProtocol.ExpectedAny, null, Guid.NewGuid(), events);

    public static Request DoesNotExist(string streamId, params Event[] events) =>
        new(streamId, AppendProtocol.ExpectedDoesNotExist, null, Guid.NewGuid(), events);

    public static Request Exists(string streamId, params Event[] events) =>
        new(streamId, AppendProtocol.ExpectedExists, null, Guid.NewGuid(), events);

    public static Request AtVersion(string streamId, long version, params Event[] events) =>
        new(streamId, AppendProtocol.ExpectedAtVersion, version, Guid.NewGuid(), events);

    public static Event JsonEvent(string name = "test", string json = "{\"v\":1}") => Event.WithJson(name, json);

    public Task<IReadOnlyList<Result>> AppendAsync(Request[] requests, CancellationToken cancellationToken = default)
    {
        return AppendAsync(null, requests, cancellationToken);
    }

    public async Task<IReadOnlyList<Result>> AppendAsync(
        NpgsqlConnection? connection,
        Request[] requests,
        CancellationToken cancellationToken = default)
    {
        var events = requests.SelectMany(x => x.Events).ToArray();

        var sql = $"SELECT request_index, status::text, observed_version, first_position, last_position " +
                  $"FROM {schema}.append_events($1, $2::{schema}.expected_state_kind[], $3, $4, $5, $6, $7, $8, $9)";

        await using var command = connection is null
            ? dataSource.CreateCommand(sql)
            : new NpgsqlCommand(sql, connection);

        command.Parameters.Add(new NpgsqlParameter<string[]>
        {
            NpgsqlDbType = NpgsqlDbType.Text.AsArray(),
            TypedValue = [.. requests.Select(x => x.StreamId)]
        });

        command.Parameters.Add(new NpgsqlParameter<string[]>
        {
            NpgsqlDbType = NpgsqlDbType.Text.AsArray(),
            TypedValue = [.. requests.Select(x => x.ExpectedKind)]
        });

        command.Parameters.Add(new NpgsqlParameter<long?[]>
        {
            NpgsqlDbType = NpgsqlDbType.Bigint.AsArray(),
            TypedValue = [.. requests.Select(x => x.ExpectedVersion)]
        });

        command.Parameters.Add(new NpgsqlParameter<Guid[]>
        {
            NpgsqlDbType = NpgsqlDbType.Uuid.AsArray(),
            TypedValue = [.. requests.Select(x => x.CommitId)]
        });

        command.Parameters.Add(new NpgsqlParameter<int[]>
        {
            NpgsqlDbType = NpgsqlDbType.Integer.AsArray(),
            TypedValue = [.. requests.Select(x => x.Events.Length)]
        });

        command.Parameters.Add(new NpgsqlParameter<string[]>
        {
            NpgsqlDbType = NpgsqlDbType.Text.AsArray(),
            TypedValue = [.. events.Select(x => x.Name)]
        });

        command.Parameters.Add(new NpgsqlParameter<string?[]>
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb.AsArray(),
            TypedValue = [.. events.Select(x => x.Json)]
        });

        command.Parameters.Add(new NpgsqlParameter<byte[]?[]>
        {
            NpgsqlDbType = NpgsqlDbType.Bytea.AsArray(),
            TypedValue = [.. events.Select(x => x.Bytes)]
        });

        command.Parameters.Add(new NpgsqlParameter<string?[]>
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb.AsArray(),
            TypedValue = [.. events.Select(x => x.Metadata)]
        });

        var results = new List<Result>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new Result(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4)));
        }

        return results;
    }

    public async Task<IReadOnlyList<Row>> ReadRowsAsync(
        string? streamId = null,
        CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT position, stream_id, stream_position, commit_id, commit_index, event_name, " +
                  $"event_data::text, event_data_bytes, metadata::text, created_at " +
                  $"FROM {schema}.event_log " +
                  (streamId is null ? "" : "WHERE stream_id = $1 ") +
                  "ORDER BY position";

        await using var command = dataSource.CreateCommand(sql);

        if (streamId is not null)
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        var rows = new List<Row>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Row(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetGuid(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<byte[]>(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetFieldValue<DateTime>(9)));
        }

        return rows;
    }

    public async Task<long> GetSequenceNextAsync(CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT next FROM {schema}.sequences WHERE name = 'event_log'");

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public sealed record Request(
        string StreamId,
        string ExpectedKind,
        long? ExpectedVersion,
        Guid CommitId,
        params Event[] Events);

    public sealed record Event(string Name, string? Json = null, byte[]? Bytes = null, string? Metadata = null)
    {
        public static Event WithJson(string name, string json, string? metadata = null) =>
            new(name, json, null, metadata);

        public static Event WithBytes(string name, byte[] bytes, string? metadata = null) =>
            new(name, null, bytes, metadata);
    }

    public sealed record Result(
        int RequestIndex,
        string Status,
        long? ObservedVersion,
        long? FirstPosition,
        long? LastPosition);

    public sealed record Row(
        long Position,
        string StreamId,
        long StreamPosition,
        Guid CommitId,
        int CommitIndex,
        string EventName,
        string? EventData,
        byte[]? EventDataBytes,
        string? Metadata,
        DateTime CreatedAt);
}