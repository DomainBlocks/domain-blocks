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
    /// Reads the rows in the event log that the filter selects, in position order, after the starting position. The
    /// read ends at the ending position, inclusive, or at the end of the log if there is no ending position. Every row
    /// is read into <paramref name="row"/>, which is valid only until the next row is read.
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
            // Without an ending position, it runs the query that reads the whole log.
            var readAllCondition = Translate(filter, EventLogSql.ReadAllParameterCount + 1);

            return ReadPagesAsync(
                sql.ReadAll(ReadDirection.Forward, includeMetadata: true, readAllCondition?.Sql),
                (parameters, key, limit) =>
                {
                    parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                    parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
                },
                readAllCondition,
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
    /// position. The read ends at the ending position, inclusive. Every row is read into <paramref name="row"/>, which
    /// is valid only until the next row is read.
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
        CancellationToken cancellationToken)
    {
        var condition = Translate(filter, EventLogSql.ReadAllParameterCount + 1);

        var read = new NpgsqlBatchCommand(sql.ReadAll(ReadDirection.Forward, includeMetadata: true, condition?.Sql));
        read.Parameters.Add(new NpgsqlParameter<long> { TypedValue = startExclusive });
        read.Parameters.Add(new NpgsqlParameter<int> { TypedValue = pageSize });
        condition?.AddParametersTo(read.Parameters);

        var barrier = new NpgsqlBatchCommand(sql.BarrierAfterRead);
        barrier.Parameters.Add(new NpgsqlParameter<long> { TypedValue = startExclusive });

        return ReadWithBarrierAsync(
            read,
            barrier,
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
    public Task<long?> ReadMaxPositionWithBarrierAsync(CancellationToken cancellationToken)
    {
        return ReadWithBarrierAsync(
            new NpgsqlBatchCommand(sql.MaxPosition),
            new NpgsqlBatchCommand(sql.BarrierAfterEnd),
            ReadMaxPositionAsync,
            cancellationToken);
    }

    /// <summary>
    /// Reads the position of the last event in a stream, or <see langword="null"/> if it is empty, with the replication
    /// barrier after the read.
    /// </summary>
    public Task<long?> ReadMaxStreamPositionWithBarrierAsync(string streamId, CancellationToken cancellationToken)
    {
        var read = new NpgsqlBatchCommand(sql.MaxStreamPosition);
        read.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        var barrier = new NpgsqlBatchCommand(sql.BarrierAfterStreamEnd);
        barrier.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        return ReadWithBarrierAsync(read, barrier, ReadMaxPositionAsync, cancellationToken);
    }

    public async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql.StreamExists);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    // Runs the read with the replication barrier after it, in one round trip, so that subscriptions are only given
    // events that a failover cannot undo. The batch is one transaction, and its commit waits for the synchronous
    // standbys, so it covers every commit that the read saw. Rows can reach the client before the commit, so the read's
    // result is used only once the batch completes. A canceled wait completes with only a warning, so that warning
    // means that the barrier was not confirmed. Without synchronous standbys the barrier does nothing: every append
    // commits with synchronous_commit on, so it is flushed before a read can see it.
    private async Task<T> ReadWithBarrierAsync<T>(
        NpgsqlBatchCommand read,
        NpgsqlBatchCommand barrier,
        Func<NpgsqlDataReader, CancellationToken, Task<T>> readResult,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        PostgresNotice? warning = null;
        T result;

        await using (connection.ConfigureAwait(false))
        {
            connection.Notice += OnNotice;

            try
            {
                await using var batch = new NpgsqlBatch(connection);
                batch.BatchCommands.Add(read);
                batch.BatchCommands.Add(barrier);

                var reader = await batch.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                await using (reader.ConfigureAwait(false))
                {
                    result = await readResult(reader, cancellationToken).ConfigureAwait(false);

                    // The batch completes once its commit has, so closing the reader waits for the standbys.
                    await reader.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                connection.Notice -= OnNotice;
            }
        }

        // A canceled wait completes as if it had been confirmed, so a canceled caller must not use the result.
        cancellationToken.ThrowIfCancellationRequested();

        if (warning is not null)
            throw new CommitNotConfirmedException($"The replication barrier was not confirmed: {warning.MessageText}");

        return result;

        void OnNotice(object? sender, NpgsqlNoticeEventArgs e)
        {
            if (CommitNotices.IsUnconfirmedCommit(e.Notice))
                warning ??= e.Notice;
        }
    }

    // Reads a page at a time. Each page starts after a key, which is the log or stream position of the last row read,
    // in the direction of the read, so the first page starts after firstKeyExclusive.
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

    private static async Task<long?> ReadMaxPositionAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
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