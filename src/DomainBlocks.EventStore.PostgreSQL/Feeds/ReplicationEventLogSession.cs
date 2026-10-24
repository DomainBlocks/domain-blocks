using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using Npgsql.Replication.PgOutput.Messages;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// A logical replication session over the slot of a <see cref="ReplicationEventLogSessionFactory{TEvent}"/>. It yields
/// committed inserts in commit order, which is also position order because appends serialize on the sequence row. Each
/// yield is the one <see cref="EventLogRow{TEvent}"/> that the session reuses, still undecoded, and it is valid only
/// until the next row is read.
/// </summary>
internal sealed class ReplicationEventLogSession<TEvent>(
    LogicalReplicationConnection connection,
    ReplicationEventLogSessionFactory<TEvent> factory,
    PgOutputReplicationSlot slot,
    bool isResumed,
    PgOutputReplicationOptions pgOutputOptions,
    SchemaObjectNames names,
    IEventDecoder<TEvent, PostgresEventData, string> decoder) :
    IEventLogSession<EventLogRow<TEvent>>
    where TEvent : notnull
{
    private readonly EventLogRow<TEvent> _row = new(decoder);
    private bool _inTransaction;
    private Task<bool>? _statusTick;

    public string Description => $"slot {slot.Name}";

    public bool IsResumed { get; } = isResumed;

    public async IAsyncEnumerable<EventLogRow<TEvent>> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = connection
            .StartReplication(slot, pgOutputOptions, cancellationToken, factory.ResumeLsn)
            .GetAsyncEnumerator(cancellationToken);

        using var statusTimer = new PeriodicTimer(connection.WalReceiverStatusInterval);

        await using (messages.ConfigureAwait(false))
        {
            ColumnMap? columnMap = null;
            uint relationId = 0;

            // Npgsql reuses message instances and row tuples are forward-only, so each message is fully consumed before
            // the next one is read.
            while (await MoveNextAsync(messages, statusTimer, cancellationToken).ConfigureAwait(false))
            {
                switch (messages.Current)
                {
                    case BeginMessage:
                        _inTransaction = true;
                        break;

                    case RelationMessage relation when IsEventLog(relation):
                        columnMap = ColumnMap.From(relation);
                        relationId = relation.RelationId;
                        break;

                    case InsertMessage insert when IsEventLog(insert.Relation):
                        if (columnMap is null || insert.Relation.RelationId != relationId)
                        {
                            columnMap = ColumnMap.From(insert.Relation);
                            relationId = insert.Relation.RelationId;
                        }

                        var row = await ReadRowAsync(insert.NewRow, columnMap, cancellationToken).ConfigureAwait(false);

                        // A session resumes after the last transaction delivered in full, so it can repeat the rows of
                        // a transaction that the previous session delivered in part. append_events inserts rows in
                        // position order, so the repeated rows are those at or below the last position delivered.
                        if (row.Position <= factory.LastPosition)
                            break;

                        yield return row;

                        // The feed has handed the row to its observers.
                        factory.LastPosition = row.Position;
                        break;

                    case CommitMessage commit:
                        _inTransaction = false;

                        // Everything up to here has been handed to observers, so the server may release the WAL, and a
                        // later session starts after this transaction.
                        factory.ResumeLsn = commit.WalEnd;
                        connection.SetReplicationStatus(commit.WalEnd);
                        break;
                }
            }
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    // A dropped or invalidated slot fails START_REPLICATION, which the factory turns into a lost slot.
    private async ValueTask<bool> MoveNextAsync(
        IAsyncEnumerator<PgOutputReplicationMessage> messages,
        PeriodicTimer statusTimer,
        CancellationToken cancellationToken)
    {
        try
        {
            var next = messages.MoveNextAsync();

            return next.IsCompleted || _inTransaction
                ? await next.ConfigureAwait(false)
                : await WaitBetweenTransactionsAsync(next.AsTask(), statusTimer).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedObject
                                               or PostgresErrorCodes.ObjectNotInPrerequisiteState)
        {
            await factory.ThrowIfSlotLostAsync(slot.Name, ex, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    // For transactions that don't touch the event log, the server sends only keepalives, which Npgsql handles
    // itself, so a position reported only at event commits would make the slot hold all WAL written since the last
    // one. Between transactions, the commit record of every transaction still to come starts at or after the latest
    // received position, so the session reports that position, as PostgreSQL's own subscriber does. Npgsql sends it
    // with its next status update.
    private async Task<bool> WaitBetweenTransactionsAsync(Task<bool> next, PeriodicTimer statusTimer)
    {
        while (true)
        {
            _statusTick ??= statusTimer.WaitForNextTickAsync().AsTask();

            if (await Task.WhenAny(next, _statusTick).ConfigureAwait(false) == next)
                return await next.ConfigureAwait(false);

            _statusTick = null;
            connection.SetReplicationStatus(connection.LastReceivedLsn);
        }
    }

    private bool IsEventLog(RelationMessage relation)
    {
        return relation.Namespace == names.Schema && relation.RelationName == SchemaObjectNames.EventLogTableName;
    }

    private async ValueTask<EventLogRow<TEvent>> ReadRowAsync(
        ReplicationTuple tuple,
        ColumnMap columnMap,
        CancellationToken cancellationToken)
    {
        long position = 0;
        string? streamId = null;
        long streamPosition = 0;
        string? eventName = null;
        string? json = null;
        byte[]? bytes = null;
        string? metadata = null;
        DateTime createdAt = default;

        var index = 0;

        await foreach (var value in tuple.ConfigureAwait(false))
        {
            var column = columnMap[index++];

            if (value.IsDBNull)
                continue;

            switch (column)
            {
                case Column.Position:
                    position = await value.Get<long>(cancellationToken).ConfigureAwait(false);
                    break;

                case Column.StreamId:
                    streamId = await value.Get<string>(cancellationToken).ConfigureAwait(false);
                    break;

                case Column.StreamPosition:
                    streamPosition = await value.Get<long>(cancellationToken).ConfigureAwait(false);
                    break;

                case Column.EventName:
                    eventName = await value.Get<string>(cancellationToken).ConfigureAwait(false);
                    break;

                case Column.EventData:
                    json = await value.Get<string>(cancellationToken).ConfigureAwait(false);
                    break;

                case Column.EventDataBytes:
                    bytes = await value.Get<byte[]>(cancellationToken).ConfigureAwait(false);
                    break;

                case Column.Metadata:
                    metadata = await value.Get<string>(cancellationToken).ConfigureAwait(false);
                    break;

                case Column.CreatedAt:
                    createdAt = await value.Get<DateTime>(cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        if (streamId is null || eventName is null)
            throw new InvalidOperationException("The replicated event log row is missing required columns.");

        var eventData = json is not null
            ? PostgresEventData.FromJson(json)
            : PostgresEventData.FromBytes(bytes ?? []);

        _row.Set(
            position,
            streamId,
            streamPosition,
            eventName,
            eventData,
            metadata,
            new DateTimeOffset(DateTime.SpecifyKind(createdAt, DateTimeKind.Utc), TimeSpan.Zero));

        return _row;
    }

    private enum Column
    {
        Unknown,
        Position,
        StreamId,
        StreamPosition,
        EventName,
        EventData,
        EventDataBytes,
        Metadata,
        CreatedAt
    }

    /// <summary>
    /// Maps replicated columns by name, so the mapping survives reordered or added columns.
    /// </summary>
    private sealed class ColumnMap(Column[] columns)
    {
        public Column this[int index] => index < columns.Length ? columns[index] : Column.Unknown;

        public static ColumnMap From(RelationMessage relation)
        {
            var columns = new Column[relation.Columns.Count];

            for (var i = 0; i < columns.Length; i++)
            {
                columns[i] = relation.Columns[i].ColumnName switch
                {
                    "position" => Column.Position,
                    "stream_id" => Column.StreamId,
                    "stream_position" => Column.StreamPosition,
                    "event_name" => Column.EventName,
                    "event_data" => Column.EventData,
                    "event_data_bytes" => Column.EventDataBytes,
                    "metadata" => Column.Metadata,
                    "created_at" => Column.CreatedAt,
                    _ => Column.Unknown
                };
            }

            return new ColumnMap(columns);
        }
    }
}