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
/// Every read goes to the server that the appends go to, so it sees every event that another source has read. Rows are
/// read into one row per source, which is valid only until the next is read.
/// </remarks>
internal sealed class PostgresSubscriptionSource<TEvent>(
    NpgsqlDataSource dataSource,
    EventLogReader<TEvent> reader,
    SchemaObjectNames names,
    IEventDecoder<TEvent, PostgresEventData, string> decoder) :
    IEventSubscriptionSource<EventLogRow<TEvent>, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    private readonly EventLogRow<TEvent> _row = new(decoder);

    public async ValueTask<LogPosition?> ReadEndLogPositionAsync(CancellationToken cancellationToken)
    {
        return await reader.GetMaxPositionAsync(cancellationToken).ConfigureAwait(false) is { } position
            ? LogPosition.FromInt64(position)
            : null;
    }

    public IAsyncEnumerable<EventLogRow<TEvent>> ReadAllAsync(
        LogPosition? fromExclusive,
        LogPosition? toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        return reader.ReadAllRowsAsync(
            ToKey(fromExclusive),
            toInclusive is { } to ? checked((long)to.Value) : null,
            filter,
            _row,
            cancellationToken);
    }

    public async ValueTask<StreamPosition?> ReadEndStreamPositionAsync(
        string streamId,
        CancellationToken cancellationToken)
    {
        return await reader.GetMaxStreamPositionAsync(streamId, cancellationToken).ConfigureAwait(false) is { } position
            ? StreamPosition.FromInt64(position)
            : null;
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