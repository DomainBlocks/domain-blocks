using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Reads events in pages so that a consumer-paced enumeration never pins a pooled connection for its whole duration.
/// Each row is read into one row object that the read keeps for all of them, which a filter is tested against, and
/// which is decoded only if the filter selects it.
/// </summary>
internal sealed class EventLogReader<TEvent>(
    NpgsqlDataSource dataSource,
    EventLogSql sql,
    int batchSize,
    IEventDecoder<TEvent, PostgresEventData, string> decoder)
    where TEvent : notnull
{
    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadStreamAsync(
        string streamId,
        ReadDirection direction,
        long firstKeyExclusive,
        long? maxCount,
        bool includeMetadata,
        EventFilterPlan filterPlan,
        CancellationToken cancellationToken)
    {
        return ReadPagesAsync(
            sql.ReadStream(direction, includeMetadata || filterPlan.IsMetadataRequired),
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            firstKeyExclusive,
            static row => row.StreamPosition,
            maxCount,
            includeMetadata,
            filterPlan,
            cancellationToken);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAllAsync(
        ReadDirection direction,
        long firstKeyExclusive,
        long? maxCount,
        bool includeMetadata,
        EventFilterPlan filterPlan,
        CancellationToken cancellationToken)
    {
        return ReadPagesAsync(
            sql.ReadAll(direction, includeMetadata || filterPlan.IsMetadataRequired),
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            firstKeyExclusive,
            static row => row.Position,
            maxCount,
            includeMetadata,
            filterPlan,
            cancellationToken);
    }

    /// <summary>
    /// Reads events of the whole log after one position and up to another, inclusive. Used for subscription
    /// catch-up, where the upper bound is the high-water mark read after attaching to the live feed.
    /// </summary>
    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCatchUpAllAsync(
        long afterExclusive,
        long highWaterMark,
        EventFilterPlan filterPlan,
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
            includeMetadata: true,
            filterPlan,
            cancellationToken);
    }

    /// <summary>
    /// Reads events of one stream after a stream position, bounded by a global high-water mark.
    /// </summary>
    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCatchUpStreamAsync(
        string streamId,
        long afterExclusive,
        long highWaterMark,
        EventFilterPlan filterPlan,
        CancellationToken cancellationToken)
    {
        return ReadPagesAsync(
            sql.ReadCatchUpStream,
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<long> { TypedValue = highWaterMark });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            afterExclusive,
            static row => row.StreamPosition,
            null,
            includeMetadata: true,
            filterPlan,
            cancellationToken);
    }

    public async Task<long?> GetMaxPositionAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql.MaxPosition);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return result is DBNull or null ? null : (long)result;
    }

    /// <summary>
    /// How far a stream had got by a position in the log, or <see langword="null"/> if it had no events by then.
    /// </summary>
    public async Task<long?> GetMaxStreamPositionAsync(
        string streamId,
        long highWaterMark,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql.MaxStreamPosition);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = highWaterMark });

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return result is DBNull or null ? null : (long)result;
    }

    public async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql.StreamExists);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadPagesAsync(
        string pageSql,
        Action<NpgsqlParameterCollection, long, int> bindPage,
        long firstKeyExclusive,
        Func<EventLogRow<TEvent>, long> keyOf,
        long? maxCount,
        bool includeMetadata,
        EventFilterPlan filterPlan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Nothing to ask the database for.
        if (filterPlan.Pushdown is NoEventsFilter)
            yield break;

        var key = firstKeyExclusive;
        var remaining = maxCount ?? long.MaxValue;
        var remainder = filterPlan.Residual is AllEventsFilter ? null : filterPlan.Residual;
        PostgresFilterSql? filterSql = null;

        // One row for the whole read, set again for each row that comes back.
        var row = new EventLogRow<TEvent>(decoder, includeMetadata);

        while (remaining > 0)
        {
            // The database cannot count the events that the remainder selects, so whole pages are asked for.
            var limit = remainder is null ? (int)Math.Min(batchSize, remaining) : batchSize;
            var fetched = 0;

            await using (var command = dataSource.CreateCommand())
            {
                bindPage(command.Parameters, key, limit);

                // The condition is written once for the read, with its parameters numbered after those of the page.
                if (filterSql is null && filterPlan.Pushdown is not AllEventsFilter)
                {
                    filterSql = PostgresFilterTranslator.Translate(filterPlan.Pushdown, command.Parameters.Count + 1);
                    pageSql = EventLogSql.WithCondition(pageSql, filterSql.Text);
                }

                filterSql?.AddParametersTo(command.Parameters);
                command.CommandText = pageSql;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                while (remaining > 0 && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    ReadRow(reader, row);
                    key = keyOf(row);
                    fetched++;

                    if (remainder is not null && !remainder.Matches(row))
                        continue;

                    remaining--;
                    yield return row.DecodedEvent;
                }
            }

            if (fetched < limit)
                yield break;
        }
    }

    // Columns are read in ascending ordinal order so that CommandBehavior.SequentialAccess could be enabled later.
    private static void ReadRow(NpgsqlDataReader reader, EventLogRow<TEvent> row)
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

        row.Set(position, streamId, streamPosition, eventName, eventData, metadata, createdAt);
    }
}