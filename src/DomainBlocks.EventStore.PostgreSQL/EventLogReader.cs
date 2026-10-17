using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Reads events in pages, so a slow consumer never holds a pooled connection for the whole read.
/// </summary>
internal sealed class EventLogReader<TEvent>(
    NpgsqlDataSource dataSource,
    EventLogSql sql,
    int pageSize,
    IEventDecoder<TEvent, PostgresEventData, string> decoder)
    where TEvent : notnull
{
    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadStreamAsync(
        string streamId,
        ReadDirection direction,
        long firstKeyExclusive,
        long? maxCount,
        bool includeMetadata,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        var condition = Translate(filter, EventLogSql.ReadStreamParameterCount + 1);

        return ReadPagesAsync(
            sql.ReadStream(direction, includeMetadata, condition?.Sql),
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            condition,
            firstKeyExclusive,
            static e => (long)e.Context.StreamPosition.Value,
            maxCount,
            ReadEvent,
            cancellationToken);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAllAsync(
        ReadDirection direction,
        long firstKeyExclusive,
        long? maxCount,
        bool includeMetadata,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        var condition = Translate(filter, EventLogSql.ReadAllParameterCount + 1);

        return ReadPagesAsync(
            sql.ReadAll(direction, includeMetadata, condition?.Sql),
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            condition,
            firstKeyExclusive,
            static e => (long)e.Context.LogPosition.Value,
            maxCount,
            ReadEvent,
            cancellationToken);
    }

    /// <summary>
    /// Reads the rows in the event log that the filter selects, in position order, after the starting position, up to
    /// and including the ending position, or to the end of the log if there is none. Every row is read into
    /// <paramref name="row"/>, which is valid only until the next is read.
    /// </summary>
    public IAsyncEnumerable<EventLogRow<TEvent>> ReadAllRowsAsync(
        long startExclusive,
        long? endInclusive,
        EventFilter filter,
        EventLogRow<TEvent> row,
        CancellationToken cancellationToken)
    {
        if (endInclusive is not { } end)
        {
            var unbounded = Translate(filter, EventLogSql.ReadAllParameterCount + 1);

            return ReadPagesAsync(
                sql.ReadAll(ReadDirection.Forward, includeMetadata: true, unbounded?.Sql),
                (parameters, key, limit) =>
                {
                    parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                    parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
                },
                unbounded,
                startExclusive,
                static r => r.Position,
                null,
                reader => ReadRow(reader, row),
                cancellationToken);
        }

        var condition = Translate(filter, EventLogSql.CatchUpAllParameterCount + 1);

        return ReadPagesAsync(
            sql.CatchUpAll(condition?.Sql),
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<long> { TypedValue = end });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            condition,
            startExclusive,
            static r => r.Position,
            null,
            reader => ReadRow(reader, row),
            cancellationToken);
    }

    /// <summary>
    /// Reads the rows in an event stream that the filter selects, in stream position order, after the starting
    /// position, up to and including the ending position. Every row is read into <paramref name="row"/>, which is valid
    /// only until the next is read.
    /// </summary>
    public IAsyncEnumerable<EventLogRow<TEvent>> ReadStreamRowsAsync(
        string streamId,
        long startExclusive,
        long endInclusive,
        EventFilter filter,
        EventLogRow<TEvent> row,
        CancellationToken cancellationToken)
    {
        var condition = Translate(filter, EventLogSql.CatchUpStreamParameterCount + 1);

        return ReadPagesAsync(
            sql.CatchUpStream(condition?.Sql),
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<long> { TypedValue = endInclusive });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            condition,
            startExclusive,
            static r => r.StreamPosition,
            null,
            reader => ReadRow(reader, row),
            cancellationToken);
    }

    /// <summary>
    /// Gets the number of rows that one page of a read holds at most.
    /// </summary>
    public int PageSize => pageSize;

    /// <summary>
    /// Reads up to a page of the rows in the event log after the starting position that the filter selects, in position
    /// order, with the replication barrier after the read. The rows are read into <paramref name="page"/>, which grows
    /// as needed, and the number read is returned once the barrier is confirmed.
    /// </summary>
    public Task<int> ReadRowsWithBarrierAsync(
        long startExclusive,
        EventFilter filter,
        List<EventLogRow<TEvent>> page,
        ReplicationBarrier barrier,
        CancellationToken cancellationToken)
    {
        var condition = Translate(filter, EventLogSql.ReadAllParameterCount + 1);

        var read = new NpgsqlBatchCommand(sql.ReadAll(ReadDirection.Forward, includeMetadata: true, condition?.Sql));
        read.Parameters.Add(new NpgsqlParameter<long> { TypedValue = startExclusive });
        read.Parameters.Add(new NpgsqlParameter<int> { TypedValue = pageSize });
        condition?.AddParametersTo(read.Parameters);

        var after = new NpgsqlBatchCommand(sql.BarrierAfterRead);
        after.Parameters.Add(new NpgsqlParameter<long> { TypedValue = startExclusive });

        return barrier.RunAsync(
            read,
            after,
            async (reader, ct) =>
            {
                var count = 0;

                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (count == page.Count)
                        page.Add(new EventLogRow<TEvent>(decoder));

                    ReadRow(reader, page[count++]);
                }

                return count;
            },
            cancellationToken);
    }

    /// <summary>
    /// Reads the position of the last event in the log, or <see langword="null"/> if it is empty, with the replication
    /// barrier after the read.
    /// </summary>
    public Task<long?> ReadMaxPositionWithBarrierAsync(ReplicationBarrier barrier, CancellationToken cancellationToken)
    {
        return barrier.RunAsync(
            new NpgsqlBatchCommand(sql.MaxPosition),
            new NpgsqlBatchCommand(sql.BarrierAfterEnd),
            ReadMaxAsync,
            cancellationToken);
    }

    /// <summary>
    /// Reads the position of the last event in a stream, or <see langword="null"/> if it is empty, with the replication
    /// barrier after the read.
    /// </summary>
    public Task<long?> ReadMaxStreamPositionWithBarrierAsync(
        string streamId,
        ReplicationBarrier barrier,
        CancellationToken cancellationToken)
    {
        var read = new NpgsqlBatchCommand(sql.MaxStreamPosition);
        read.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        var after = new NpgsqlBatchCommand(sql.BarrierAfterStreamEnd);
        after.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        return barrier.RunAsync(read, after, ReadMaxAsync, cancellationToken);
    }

    public async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql.StreamExists);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private async IAsyncEnumerable<T> ReadPagesAsync<T>(
        string pageSql,
        Action<NpgsqlParameterCollection, long, int> bindPage,
        PostgresFilterCondition? condition,
        long firstKeyExclusive,
        Func<T, long> keyOf,
        long? maxCount,
        Func<NpgsqlDataReader, T> read,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var key = firstKeyExclusive;
        var remaining = maxCount ?? long.MaxValue;

        while (remaining > 0)
        {
            var limit = (int)Math.Min(pageSize, remaining);
            var count = 0;

            await using (var command = dataSource.CreateCommand(pageSql))
            {
                bindPage(command.Parameters, key, limit);
                condition?.AddParametersTo(command.Parameters);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var item = read(reader);
                    key = keyOf(item);
                    count++;
                    yield return item;
                }
            }

            remaining -= count;

            if (count < limit)
                yield break;
        }
    }

    // The All filter has no condition, so an unfiltered read runs the plain query.
    private static PostgresFilterCondition? Translate(EventFilter filter, int firstParameterIndex) =>
        filter is AllEventsFilter ? null : PostgresFilterTranslator.Translate(filter, firstParameterIndex);

    // Columns are read in ascending ordinal order, so CommandBehavior.SequentialAccess could be enabled later.
    private ReadEvent<TEvent, string, StreamPosition, LogPosition> ReadEvent(NpgsqlDataReader reader)
    {
        var position = reader.GetInt64(0);
        var streamId = reader.GetString(1);
        var streamPosition = reader.GetInt64(2);
        var eventName = reader.GetString(3);
        var eventData = ReadEventData(reader);
        var metadata = reader.IsDBNull(6) ? null : reader.GetString(6);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(7);

        return decoder.Decode(position, streamId, streamPosition, eventName, eventData, metadata, createdAt);
    }

    private static EventLogRow<TEvent> ReadRow(NpgsqlDataReader reader, EventLogRow<TEvent> row)
    {
        row.Set(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            ReadEventData(reader),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7));

        return row;
    }

    private static async Task<long?> ReadMaxAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && !reader.IsDBNull(0)
            ? reader.GetInt64(0)
            : null;
    }

    private static PostgresEventData ReadEventData(NpgsqlDataReader reader)
    {
        return !reader.IsDBNull(4)
            ? PostgresEventData.FromJson(reader.GetString(4))
            : PostgresEventData.FromBytes(reader.GetFieldValue<byte[]>(5));
    }
}