using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Reads event log rows in pages so that a consumer-paced enumeration never pins a pooled connection for its whole
/// duration.
/// </summary>
internal sealed class EventLogReader(NpgsqlDataSource dataSource, EventLogSql sql, int batchSize)
{
    public IAsyncEnumerable<EventLogRow> ReadStreamAsync(
        string streamId,
        ReadDirection direction,
        long firstKeyExclusive,
        long? maxCount,
        bool includeMetadata,
        CancellationToken cancellationToken)
    {
        return ReadPagesAsync(
            sql.ReadStream(direction, includeMetadata),
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            firstKeyExclusive,
            static row => row.StreamPosition,
            maxCount,
            cancellationToken);
    }

    public IAsyncEnumerable<EventLogRow> ReadAllAsync(
        ReadDirection direction,
        long firstKeyExclusive,
        long? maxCount,
        bool includeMetadata,
        CancellationToken cancellationToken)
    {
        return ReadPagesAsync(
            sql.ReadAll(direction, includeMetadata),
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            firstKeyExclusive,
            static row => row.Position,
            maxCount,
            cancellationToken);
    }

    /// <summary>
    /// Reads rows of the whole log after one position and up to another, inclusive. Used for subscription catch-up,
    /// where the upper bound is the high-water mark read after attaching to the live feed.
    /// </summary>
    public IAsyncEnumerable<EventLogRow> ReadCatchUpAllAsync(
        long afterExclusive,
        long highWaterMark,
        CancellationToken cancellationToken)
    {
        return ReadPagesAsync(
            sql.ReadCatchUpAll,
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<long> { TypedValue = highWaterMark });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            afterExclusive,
            static row => row.Position,
            null,
            cancellationToken);
    }

    public async Task<long?> GetMaxPositionAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql.MaxPosition);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return result is DBNull or null ? null : (long)result;
    }

    public async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql.StreamExists);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private async IAsyncEnumerable<EventLogRow> ReadPagesAsync(
        string pageSql,
        Action<NpgsqlParameterCollection, long, int> bindPage,
        long firstKeyExclusive,
        Func<EventLogRow, long> keyOf,
        long? maxCount,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var key = firstKeyExclusive;
        var remaining = maxCount ?? long.MaxValue;

        while (remaining > 0)
        {
            var limit = (int)Math.Min(batchSize, remaining);
            var count = 0;

            await using (var command = dataSource.CreateCommand(pageSql))
            {
                bindPage(command.Parameters, key, limit);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var row = ReadRow(reader);
                    key = keyOf(row);
                    count++;
                    yield return row;
                }
            }

            remaining -= count;

            if (count < limit)
                yield break;
        }
    }

    // Columns are read in ascending ordinal order so that CommandBehavior.SequentialAccess could be enabled later.
    private static EventLogRow ReadRow(NpgsqlDataReader reader)
    {
        var position = reader.GetInt64(0);
        var streamId = reader.GetString(1);
        var streamPosition = reader.GetInt64(2);
        var eventName = reader.GetString(3);

        var eventData = !reader.IsDBNull(4)
            ? PostgresEventData.FromJson(reader.GetString(4))
            : PostgresEventData.FromBytes(reader.GetFieldValue<byte[]>(5));

        var metadata = reader.IsDBNull(6) ? null : reader.GetString(6);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(7);

        return new EventLogRow(position, streamId, streamPosition, eventName, eventData, metadata, createdAt);
    }
}
