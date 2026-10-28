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
    private const string IsInRecoverySql = "SELECT pg_is_in_recovery()";

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
            cancellationToken);
    }

    /// <summary>
    /// Reads events from the global event log whose positions are greater than the starting position and less than or
    /// equal to the ending position, in ascending position order.
    /// </summary>
    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCatchUpAllAsync(
        long startExclusive,
        long endInclusive,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        var condition = Translate(filter, EventLogSql.CatchUpAllParameterCount + 1);

        return ReadPagesAsync(
            sql.CatchUpAll(condition?.Sql),
            (parameters, key, limit) =>
            {
                parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                parameters.Add(new NpgsqlParameter<long> { TypedValue = endInclusive });
                parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
            },
            condition,
            startExclusive,
            static e => (long)e.Context.LogPosition.Value,
            null,
            cancellationToken);
    }

    /// <summary>
    /// Reads events from an event stream whose positions are greater than the starting position and less than or equal
    /// to the ending position, in ascending position order.
    /// </summary>
    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCatchUpStreamAsync(
        string streamId,
        long startExclusive,
        long endInclusive,
        EventFilter filter,
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
            static e => (long)e.Context.StreamPosition.Value,
            null,
            cancellationToken);
    }

    public async Task<long?> GetMaxPositionAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql.MaxPosition);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return result is DBNull or null ? null : (long)result;
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
        var query = new NpgsqlBatchCommand(sql.MaxStreamPosition);
        query.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        var barrier = new NpgsqlBatchCommand(sql.BarrierAfterStreamEnd);
        barrier.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        return ReadWithBarrierAsync(query, barrier, ReadMaxPositionAsync, cancellationToken);
    }

    public async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql.StreamExists);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    // Runs the query and then the replication barrier in one round trip, so a failover can't undo what the query saw.
    // The batch is one transaction, and its commit waits for the synchronous standbys, so it covers every commit that
    // the query saw. Rows can reach the client before the commit, so the query's result is used only once the batch
    // completes. A canceled wait completes with only a warning, so any warning means the barrier was not confirmed.
    // On a server in recovery the barrier can't write WAL, and its error says only that something failed internally,
    // so the batch first reads whether the server is in recovery.
    private async Task<T> ReadWithBarrierAsync<T>(
        NpgsqlBatchCommand query,
        NpgsqlBatchCommand barrier,
        Func<NpgsqlDataReader, CancellationToken, Task<T>> resultReader,
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
                batch.BatchCommands.Add(query);
                batch.BatchCommands.Add(new NpgsqlBatchCommand(IsInRecoverySql));
                batch.BatchCommands.Add(barrier);

                var reader = await batch.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                await using (reader.ConfigureAwait(false))
                {
                    result = await resultReader(reader, cancellationToken).ConfigureAwait(false);

                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                    await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    var isInRecovery = reader.GetBoolean(0);

                    // A failed barrier closes the connection, which then has no host.
                    var host = connection.Host;

                    try
                    {
                        // The batch completes once its commit has, so closing the reader waits for the standbys.
                        await reader.CloseAsync().ConfigureAwait(false);
                    }
                    catch (PostgresException ex) when (isInRecovery)
                    {
                        throw new ServerInRecoveryException(host, ex);
                    }
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
            throw new ReplicationBarrierException($"The replication barrier was not confirmed: {warning.MessageText}");

        return result;

        void OnNotice(object? sender, NpgsqlNoticeEventArgs e)
        {
            if (e.Notice.InvariantSeverity == "WARNING")
                warning ??= e.Notice;
        }
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadPagesAsync(
        string pageSql,
        Action<NpgsqlParameterCollection, long, int> bindPage,
        PostgresFilterCondition? condition,
        long firstKeyExclusive,
        Func<ReadEvent<TEvent, string, StreamPosition, LogPosition>, long> keyOf,
        long? maxCount,
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
                    var readEvent = ReadEvent(reader);
                    key = keyOf(readEvent);
                    count++;
                    yield return readEvent;
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

        var eventData = !reader.IsDBNull(4)
            ? PostgresEventData.FromJson(reader.GetString(4))
            : PostgresEventData.FromBytes(reader.GetFieldValue<byte[]>(5));

        var metadata = reader.IsDBNull(6) ? null : reader.GetString(6);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(7);

        return decoder.Decode(position, streamId, streamPosition, eventName, eventData, metadata, createdAt);
    }

    private static async Task<long?> ReadMaxPositionAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && !reader.IsDBNull(0)
            ? reader.GetInt64(0)
            : null;
    }
}