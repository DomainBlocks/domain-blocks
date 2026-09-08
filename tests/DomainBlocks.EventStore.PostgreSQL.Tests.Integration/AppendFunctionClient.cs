using Npgsql;
using NpgsqlTypes;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Calls the append_events function directly, so that its contract can be tested independently of the event store.
/// </summary>
internal sealed class AppendFunctionClient(NpgsqlDataSource dataSource, string schema)
{
    public sealed record Request(
        string StreamId,
        short ExpectedKind,
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
        short Status,
        short ObservedKind,
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

    public static Request Any(string streamId, params Event[] events) =>
        new(streamId, AppendProtocol.ExpectedAny, null, Guid.NewGuid(), events);

    public static Request DoesNotExist(string streamId, params Event[] events) =>
        new(streamId, AppendProtocol.ExpectedDoesNotExist, null, Guid.NewGuid(), events);

    public static Request Exists(string streamId, params Event[] events) =>
        new(streamId, AppendProtocol.ExpectedExists, null, Guid.NewGuid(), events);

    public static Request AtVersion(string streamId, long version, params Event[] events) =>
        new(streamId, AppendProtocol.ExpectedAtVersion, version, Guid.NewGuid(), events);

    public static Event JsonEvent(string name = "test", string json = "{\"v\":1}") => Event.WithJson(name, json);

    public Task<IReadOnlyList<Result>> AppendAsync(params Request[] requests)
    {
        return AppendAsync(null, requests);
    }

    public async Task<IReadOnlyList<Result>> AppendAsync(NpgsqlConnection? connection, params Request[] requests)
    {
        var events = requests.SelectMany(x => x.Events).ToArray();

        var sql = $"SELECT request_index, status, observed_kind, observed_version, first_position, last_position " +
                  $"FROM {schema}.append_events($1, $2, $3, $4, $5, $6, $7, $8, $9)";

        await using var command = connection is null
            ? dataSource.CreateCommand(sql)
            : new NpgsqlCommand(sql, connection);

        command.Parameters.Add(new NpgsqlParameter<string[]>
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
            TypedValue = requests.Select(x => x.StreamId).ToArray()
        });

        command.Parameters.Add(new NpgsqlParameter<short[]>
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Smallint,
            TypedValue = requests.Select(x => x.ExpectedKind).ToArray()
        });

        command.Parameters.Add(new NpgsqlParameter<long?[]>
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bigint,
            TypedValue = requests.Select(x => x.ExpectedVersion).ToArray()
        });

        command.Parameters.Add(new NpgsqlParameter<Guid[]>
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid,
            TypedValue = requests.Select(x => x.CommitId).ToArray()
        });

        command.Parameters.Add(new NpgsqlParameter<int[]>
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer,
            TypedValue = requests.Select(x => x.Events.Length).ToArray()
        });

        command.Parameters.Add(new NpgsqlParameter<string[]>
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
            TypedValue = events.Select(x => x.Name).ToArray()
        });

        command.Parameters.Add(new NpgsqlParameter<string?[]>
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb,
            TypedValue = events.Select(x => x.Json).ToArray()
        });

        command.Parameters.Add(new NpgsqlParameter<byte[]?[]>
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bytea,
            TypedValue = events.Select(x => x.Bytes).ToArray()
        });

        command.Parameters.Add(new NpgsqlParameter<string?[]>
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb,
            TypedValue = events.Select(x => x.Metadata).ToArray()
        });

        var results = new List<Result>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new Result(
                reader.GetInt32(0),
                reader.GetInt16(1),
                reader.GetInt16(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5)));
        }

        return results;
    }

    public async Task<IReadOnlyList<Row>> ReadRowsAsync(string? streamId = null)
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

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
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

    public async Task<long> GetSequenceNextAsync()
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT next FROM {schema}.sequences WHERE name = 'event_log'");

        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async Task ResetAsync()
    {
        await using var command = dataSource.CreateCommand(
            $"TRUNCATE {schema}.event_log; UPDATE {schema}.sequences SET next = 0 WHERE name = 'event_log'");

        await command.ExecuteNonQueryAsync();
    }
}
