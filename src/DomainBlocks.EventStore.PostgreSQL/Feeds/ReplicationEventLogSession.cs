using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using Microsoft.Extensions.Logging;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using Npgsql.Replication.PgOutput.Messages;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

internal static class ReplicationEventLogSession
{
    public static Task<IEventLogSession<EventLogRow<TEvent>>> OpenAsync<TEvent>(
        string connectionString,
        string slotName,
        SchemaObjectNames names,
        PostgresReplicationOptions options,
        IEventDecoder<TEvent, PostgresEventData, string> decoder,
        ILogger? logger,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        return ReplicationEventLogSession<TEvent>.OpenAsync(
            connectionString,
            slotName,
            names,
            options,
            decoder,
            logger,
            cancellationToken);
    }
}

/// <summary>
/// A logical replication session over a temporary pgoutput slot. It yields committed inserts in commit order, which is
/// also position order because appends serialize on the sequence row. Each yield is the one
/// <see cref="EventLogRow{TEvent}"/> that the session reuses, still undecoded, and it is valid only until the next row
/// is read.
/// </summary>
/// <remarks>
/// <para>
/// Creating the slot sets a consistent point, and only transactions that commit after it are streamed. That is why the
/// feed resets its observers when it reconnects.
/// </para>
/// <para>
/// The server drops the temporary slot when the connection closes, even if the process crashes, so no WAL is retained
/// for a consumer that never returns.
/// </para>
/// </remarks>
internal sealed class ReplicationEventLogSession<TEvent> : IEventLogSession<EventLogRow<TEvent>>
    where TEvent : notnull
{
    private readonly LogicalReplicationConnection _connection;
    private readonly PgOutputReplicationSlot _slot;
    private readonly PgOutputReplicationOptions _pgOutputOptions;
    private readonly SchemaObjectNames _names;
    private readonly EventLogRow<TEvent> _row;

    private ReplicationEventLogSession(
        LogicalReplicationConnection connection,
        PgOutputReplicationSlot slot,
        PgOutputReplicationOptions pgOutputOptions,
        SchemaObjectNames names,
        IEventDecoder<TEvent, PostgresEventData, string> decoder)
    {
        _connection = connection;
        _slot = slot;
        _pgOutputOptions = pgOutputOptions;
        _names = names;
        _row = new EventLogRow<TEvent>(decoder);
    }

    public string Description => $"slot {_slot.Name}";

    public static async Task<IEventLogSession<EventLogRow<TEvent>>> OpenAsync(
        string connectionString,
        string slotName,
        SchemaObjectNames names,
        PostgresReplicationOptions options,
        IEventDecoder<TEvent, PostgresEventData, string> decoder,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var connection = new LogicalReplicationConnection(connectionString)
        {
            WalReceiverTimeout = options.WalReceiverTimeout,
            WalReceiverStatusInterval = options.WalReceiverStatusInterval
        };

        try
        {
            await connection.Open(cancellationToken).ConfigureAwait(false);

            // This blocks until a consistent point exists, that is, until every transaction now in progress has ended.
            var slot = await connection
                .CreatePgOutputReplicationSlot(
                    slotName,
                    temporarySlot: true,
                    slotSnapshotInitMode: LogicalSlotSnapshotInitMode.NoExport,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            logger?.ReplicationSlotCreated(slotName, connection.ProcessID);

            var pgOutputOptions = new PgOutputReplicationOptions(
                names.Publication,
                PgOutputProtocolVersion.V1,
                binary: options.UseBinaryProtocol,
                streamingMode: PgOutputStreamingMode.Off);

            return new ReplicationEventLogSession<TEvent>(connection, slot, pgOutputOptions, names, decoder);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async IAsyncEnumerable<EventLogRow<TEvent>> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = _connection.StartReplication(_slot, _pgOutputOptions, cancellationToken);

        ColumnMap? columnMap = null;
        uint relationId = 0;

        // Npgsql reuses message instances and row tuples are forward-only, so each message is fully consumed before the
        // next one is read.
        await foreach (var message in messages.ConfigureAwait(false))
        {
            switch (message)
            {
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

                    yield return await ReadRowAsync(insert.NewRow, columnMap, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case CommitMessage commit:
                    // Everything up to here has been handed to observers, so the server may release the WAL.
                    _connection.SetReplicationStatus(commit.WalEnd);
                    break;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        // Closing the session drops the temporary slot on the server.
        return _connection.DisposeAsync();
    }

    private bool IsEventLog(RelationMessage relation)
    {
        return relation.Namespace == _names.Schema && relation.RelationName == SchemaObjectNames.EventLogTableName;
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