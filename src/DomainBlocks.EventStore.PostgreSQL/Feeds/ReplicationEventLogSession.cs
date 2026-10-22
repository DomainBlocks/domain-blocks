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
internal sealed class ReplicationEventLogSession<TEvent> : IEventLogSession<EventLogRow<TEvent>>
    where TEvent : notnull
{
    private readonly LogicalReplicationConnection _connection;
    private readonly ReplicationEventLogSessionFactory<TEvent> _factory;
    private readonly PgOutputReplicationSlot _slot;
    private readonly PgOutputReplicationOptions _pgOutputOptions;
    private readonly SchemaObjectNames _names;
    private readonly EventLogRow<TEvent> _row;

    public ReplicationEventLogSession(
        LogicalReplicationConnection connection,
        ReplicationEventLogSessionFactory<TEvent> factory,
        PgOutputReplicationSlot slot,
        bool isResumed,
        PgOutputReplicationOptions pgOutputOptions,
        SchemaObjectNames names,
        IEventDecoder<TEvent, PostgresEventData, string> decoder)
    {
        _connection = connection;
        _factory = factory;
        _slot = slot;
        IsResumed = isResumed;
        _pgOutputOptions = pgOutputOptions;
        _names = names;
        _row = new EventLogRow<TEvent>(decoder);
    }

    public string Description => $"slot {_slot.Name}";

    public bool IsResumed { get; }

    public async IAsyncEnumerable<EventLogRow<TEvent>> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = _connection
            .StartReplication(_slot, _pgOutputOptions, cancellationToken, _factory.ResumeLsn)
            .GetAsyncEnumerator(cancellationToken);

        await using (messages.ConfigureAwait(false))
        {
            ColumnMap? columnMap = null;
            uint relationId = 0;

            // Npgsql reuses message instances and row tuples are forward-only, so each message is fully consumed before
            // the next one is read.
            while (await MoveNextAsync(messages, cancellationToken).ConfigureAwait(false))
            {
                switch (messages.Current)
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

                        var row = await ReadRowAsync(insert.NewRow, columnMap, cancellationToken).ConfigureAwait(false);

                        // A session resumes after the last transaction delivered in full, so it can repeat the rows of
                        // a transaction that the previous session delivered in part.
                        if (row.Position <= _factory.LastPosition)
                            break;

                        yield return row;

                        // The feed has handed the row to its observers.
                        _factory.LastPosition = row.Position;
                        break;

                    case CommitMessage commit:
                        // Everything up to here has been handed to observers, so the server may release the WAL, and a
                        // later session starts after this transaction.
                        _factory.ResumeLsn = commit.WalEnd;
                        _connection.SetReplicationStatus(commit.WalEnd);
                        break;
                }
            }
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    // A dropped or invalidated slot fails START_REPLICATION, which the factory turns into a lost slot.
    private async ValueTask<bool> MoveNextAsync(
        IAsyncEnumerator<PgOutputReplicationMessage> messages,
        CancellationToken cancellationToken)
    {
        try
        {
            return await messages.MoveNextAsync().ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedObject
                                               or PostgresErrorCodes.ObjectNotInPrerequisiteState)
        {
            await _factory.ThrowIfSlotLostAsync(_slot.Name, ex, cancellationToken).ConfigureAwait(false);
            throw;
        }
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