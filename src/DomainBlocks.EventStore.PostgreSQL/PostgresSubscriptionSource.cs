using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Subscriptions;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Reads the event log for subscriptions, and listens for the notifications that append_events sends when it appends
/// events.
/// </summary>
/// <remarks>
/// <para>
/// Every read goes to the server that the appends go to, so it sees every event that another source has read.
/// </para>
/// <para>
/// The replication barrier follows every read whose result is used as a position or handed on to subscriptions: the
/// end reads, and the tail's reads of the log. A catch-up reads only up to an end position that a barrier followed, so
/// its reads need none. The tail's rows are read a page at a time into rows that the source reuses, and handed on only
/// once the page's barrier is confirmed. A catch-up's rows are read into one row that the source reuses. Either way, a
/// row is valid only until the next is read.
/// </para>
/// </remarks>
internal sealed class PostgresSubscriptionSource<TEvent>(
    NpgsqlDataSource dataSource,
    EventLogReader<TEvent> reader,
    ReplicationBarrier barrier,
    SchemaObjectNames names,
    IEventDecoder<TEvent, PostgresEventData, string> decoder) :
    IEventSubscriptionSource<EventLogRow<TEvent>, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    private readonly EventLogRow<TEvent> _row = new(decoder);
    private readonly List<EventLogRow<TEvent>> _page = [];

    public async ValueTask<LogPosition?> ReadEndPositionAsync(CancellationToken cancellationToken)
    {
        var position = await reader.ReadMaxPositionWithBarrierAsync(barrier, cancellationToken).ConfigureAwait(false);

        return position is { } p ? LogPosition.FromInt64(p) : null;
    }

    public IAsyncEnumerable<EventLogRow<TEvent>> ReadAllAsync(
        LogPosition? fromExclusive,
        LogPosition? toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        if (toInclusive is not { } to)
            return ReadWithBarrierAsync(ToKey(fromExclusive), filter, cancellationToken);

        return reader.ReadAllRowsAsync(ToKey(fromExclusive), checked((long)to.Value), filter, _row, cancellationToken);
    }

    public async ValueTask<StreamPosition?> ReadStreamEndPositionAsync(
        string streamId,
        CancellationToken cancellationToken)
    {
        var position = await reader
            .ReadMaxStreamPositionWithBarrierAsync(streamId, barrier, cancellationToken)
            .ConfigureAwait(false);

        return position is { } p ? StreamPosition.FromInt64(p) : null;
    }

    public IAsyncEnumerable<EventLogRow<TEvent>> ReadStreamAsync(
        string streamId,
        StreamPosition? fromExclusive,
        StreamPosition toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        return reader.ReadStreamRowsAsync(
            streamId,
            ToKey(fromExclusive),
            checked((long)toInclusive.Value),
            filter,
            _row,
            cancellationToken);
    }

    public async IAsyncEnumerable<LogPosition> ListenForAppendsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            // The connection raises notifications while it waits, on the waiting thread, so they need no locking.
            var positions = new Queue<long>();

            connection.Notification += (_, e) =>
            {
                if (e.Channel == names.AppendsChannel &&
                    long.TryParse(e.Payload, NumberStyles.None, CultureInfo.InvariantCulture, out var position))
                {
                    positions.Enqueue(position);
                }
            };

            try
            {
                await using (var command = new NpgsqlCommand($"LISTEN \"{names.AppendsChannel}\"", connection))
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                while (true)
                {
                    await connection.WaitAsync(cancellationToken).ConfigureAwait(false);

                    while (positions.TryDequeue(out var position))
                        yield return LogPosition.FromInt64(position);
                }
            }
            finally
            {
                await UnlistenAsync(connection).ConfigureAwait(false);
            }
        }
    }

    public bool IsTransientError(Exception exception) => PostgresTransientErrors.IsTransient(exception);

    // Reads to the end of the log a page at a time, and hands each page on once its barrier is confirmed.
    private async IAsyncEnumerable<EventLogRow<TEvent>> ReadWithBarrierAsync(
        long startExclusive,
        EventFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var key = startExclusive;

        while (true)
        {
            var count = await reader
                .ReadRowsWithBarrierAsync(key, filter, _page, barrier, cancellationToken)
                .ConfigureAwait(false);

            for (var i = 0; i < count; i++)
                yield return _page[i];

            if (count < reader.PageSize)
                yield break;

            key = _page[count - 1].Position;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // A pooled connection would go on listening after it is returned to the pool, until its next use resets it, without
    // reading what it hears, and the server holds its notification queue back for the slowest listener. So the
    // connection stops listening before it is returned.
    private static async Task UnlistenAsync(NpgsqlConnection connection)
    {
        if (connection.State != ConnectionState.Open)
            return;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var command = new NpgsqlCommand("UNLISTEN *", connection);
            await command.ExecuteNonQueryAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException)
        {
            // This is best effort. A broken connection is discarded instead of returned to the pool.
        }
    }

    private static long ToKey<TPos>(TPos? position) where TPos : struct, IPosition<TPos>
    {
        return position is { } p ? checked((long)p.Value) : -1;
    }
}