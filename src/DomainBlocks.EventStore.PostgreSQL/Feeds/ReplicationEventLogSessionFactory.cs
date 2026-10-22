using System.Data;
using DomainBlocks.EventStore.Codecs;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using NpgsqlTypes;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Opens one feed's logical replication sessions over a permanent pgoutput slot, which it creates for the first session
/// and drops when it is disposed. Each later session resumes where the previous one stopped, so a lost connection costs
/// no reset. A dropped or invalidated slot does: the next session gets a new slot.
/// </summary>
/// <remarks>
/// <para>
/// Slots are created and checked through <c>dataSource</c>, which must reach the same database as
/// <c>connectionString</c>.
/// </para>
/// <para>
/// A process that stops without dropping its slot leaves it behind, holding WAL. Before it creates a slot, the factory
/// drops the inactive slots with the configured prefix that are invalidated or have been inactive for longer than
/// <see cref="PostgresReplicationOptions.InactiveSlotTimeout"/>. A live feed's slot is inactive only while the feed
/// reconnects. If a slot is dropped anyway, its feed creates a new one and resets.
/// </para>
/// </remarks>
internal sealed class ReplicationEventLogSessionFactory<TEvent> : IEventLogSessionFactory<EventLogRow<TEvent>>
    where TEvent : notnull
{
    private const string CreateSlotSql = "SELECT pg_create_logical_replication_slot($1, 'pgoutput')";
    private const string DropSlotSql = "SELECT pg_drop_replication_slot($1)";
    private const string TerminateBackendSql = "SELECT pg_terminate_backend($1, 5000)";

    private const string SlotStateSql =
        "SELECT invalidation_reason IS NULL, active_pid FROM pg_replication_slots WHERE slot_name = $1";

    // The slots in this database with the prefix ($1) that are invalidated or inactive for longer than $2.
    private const string SelectInactiveSlotsSql =
        """
        SELECT slot_name
        FROM pg_replication_slots
        WHERE database = current_database()
          AND slot_type = 'logical'
          AND NOT active
          AND left(slot_name, length($1) + 1) = $1 || '_'
          AND (invalidation_reason IS NOT NULL OR inactive_since < now() - $2)
        """;

    // A walsender that hasn't yet noticed that its client has gone holds the slot until wal_sender_timeout. A slot that
    // can't be dropped in time is left to a later cleanup.
    private static readonly TimeSpan DropSlotTimeout = TimeSpan.FromSeconds(10);

    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly SlotNameGenerator _slotNames;
    private readonly SchemaObjectNames _names;
    private readonly PostgresReplicationOptions _options;
    private readonly PgOutputReplicationOptions _pgOutputOptions;
    private readonly IEventDecoder<TEvent, PostgresEventData, string> _decoder;
    private readonly ILogger? _logger;
    private string? _slotName;
    private bool _hasOpenedSession;

    public ReplicationEventLogSessionFactory(
        string connectionString,
        NpgsqlDataSource dataSource,
        SlotNameGenerator slotNames,
        SchemaObjectNames names,
        PostgresReplicationOptions options,
        IEventDecoder<TEvent, PostgresEventData, string> decoder,
        ILogger? logger)
    {
        _connectionString = connectionString;
        _dataSource = dataSource;
        _slotNames = slotNames;
        _names = names;
        _options = options;
        _decoder = decoder;
        _logger = logger;

        _pgOutputOptions = new PgOutputReplicationOptions(
            names.Publication,
            PgOutputProtocolVersion.V1,
            binary: options.UseBinaryProtocol,
            streamingMode: PgOutputStreamingMode.Off);
    }

    /// <summary>
    /// The end of the last transaction that a session delivered in full, which the next session starts after, or
    /// <see langword="null"/> to start where the slot does.
    /// </summary>
    public NpgsqlLogSequenceNumber? ResumeLsn { get; set; }

    /// <summary>
    /// The position of the last row that a session delivered.
    /// </summary>
    public long LastPosition { get; set; } = -1;

    public async Task<IEventLogSession<EventLogRow<TEvent>>> OpenAsync(CancellationToken cancellationToken)
    {
        if (_slotName is not null)
            await PrepareToResumeAsync(_slotName, cancellationToken).ConfigureAwait(false);

        if (_slotName is null)
        {
            _slotName = await CreateSlotAsync(cancellationToken).ConfigureAwait(false);
            _hasOpenedSession = false;
            ResumeLsn = null;
        }

        var connection = new LogicalReplicationConnection(_connectionString)
        {
            WalReceiverTimeout = _options.WalReceiverTimeout,
            WalReceiverStatusInterval = _options.WalReceiverStatusInterval
        };

        try
        {
            await connection.Open(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        // The first session on a slot starts where the slot does, and a later one resumes the session before it.
        var isResumed = _hasOpenedSession;
        _hasOpenedSession = true;

        return new ReplicationEventLogSession<TEvent>(
            connection,
            this,
            new PgOutputReplicationSlot(_slotName),
            isResumed,
            _pgOutputOptions,
            _names,
            _decoder);
    }

    public async ValueTask DisposeAsync()
    {
        if (_slotName is not { } slotName)
            return;

        _slotName = null;
        await TryDropSlotAsync(slotName).ConfigureAwait(false);
    }

    /// <summary>
    /// Called when START_REPLICATION fails with an error that a dropped or invalidated slot causes. Other failures
    /// share those error codes, such as a missing publication, so the slot itself is checked. If it is lost, the next
    /// session gets a new slot.
    /// </summary>
    /// <exception cref="ReplicationSlotLostException">The slot was dropped or invalidated.</exception>
    public async Task ThrowIfSlotLostAsync(
        string slotName,
        PostgresException exception,
        CancellationToken cancellationToken)
    {
        var (isValid, _) = await ReadSlotStateAsync(slotName, cancellationToken).ConfigureAwait(false);

        if (isValid)
            return;

        ForgetSlot(slotName);
        throw new ReplicationSlotLostException(slotName, exception);
    }

    // The slot can be lost while no session streams from it. The walsender of a session whose connection was lost holds
    // the slot until it notices, and that walsender is this feed's, so it is ended rather than waited for.
    private async Task PrepareToResumeAsync(string slotName, CancellationToken cancellationToken)
    {
        var (isValid, activePid) = await ReadSlotStateAsync(slotName, cancellationToken).ConfigureAwait(false);

        if (isValid && activePid is { } pid)
        {
            await TryTerminateBackendAsync(pid, cancellationToken).ConfigureAwait(false);
            (isValid, activePid) = await ReadSlotStateAsync(slotName, cancellationToken).ConfigureAwait(false);
        }

        if (!isValid)
            ForgetSlot(slotName);
        else if (activePid is { } stillActivePid)
            throw new ReplicationSlotInUseException(slotName, stillActivePid);
    }

    // A missing slot is not valid either.
    private async Task<(bool IsValid, int? ActivePid)> ReadSlotStateAsync(
        string slotName,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(SlotStateSql);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || !reader.GetBoolean(0))
            return (false, null);

        return (true, reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }

    // It waits up to five seconds for the backend to exit. Ending a backend of another role needs pg_signal_backend.
    private async Task TryTerminateBackendAsync(int pid, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(TerminateBackendSql);
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = pid });

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            // The walsender ends on its own once it notices that its client has gone.
        }
    }

    private void ForgetSlot(string slotName)
    {
        _slotName = null;
        _logger?.ReplicationSlotLost(slotName);
    }

    private async Task<string> CreateSlotAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // The cleanup reads inactive_since, which PostgreSQL 17 added.
        if (connection.PostgreSqlVersion.Major < 17)
        {
            throw new NotSupportedException(
                $"Live subscriptions require PostgreSQL 17 or later, but the server is {connection.ServerVersion}.");
        }

        await DropInactiveSlotsAsync(connection, cancellationToken).ConfigureAwait(false);

        var slotName = _slotNames.Next();

        await using (var create = new NpgsqlCommand(CreateSlotSql, connection))
        {
            create.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger?.ReplicationSlotCreated(slotName);

        try
        {
            await WaitForAppendInFlightAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryDropSlotAsync(slotName).ConfigureAwait(false);
            throw;
        }

        return slotName;
    }

    private async Task DropInactiveSlotsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var slotNames = new List<string>();

        await using (var select = new NpgsqlCommand(SelectInactiveSlotsSql, connection))
        {
            select.Parameters.Add(new NpgsqlParameter<string> { TypedValue = _options.SlotNamePrefix });
            select.Parameters.Add(new NpgsqlParameter<TimeSpan> { TypedValue = _options.InactiveSlotTimeout });

            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                slotNames.Add(reader.GetString(0));
        }

        foreach (var slotName in slotNames)
        {
            await using var drop = new NpgsqlCommand(DropSlotSql, connection);
            drop.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });

            try
            {
                await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                _logger?.InactiveReplicationSlotDropped(slotName);
            }
            catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedObject
                                                   or PostgresErrorCodes.ObjectInUse)
            {
                // Another feed dropped it first, or its own feed reconnected.
            }
        }
    }

    // A transaction can commit just before the slot's consistent point and still be invisible to queries, while the
    // slot won't stream it. Appends hold the sequence row until they are visible, so taking the row waits for any such
    // append. Once the first session opens, queries see every row that the slot won't stream.
    private async Task WaitForAppendInFlightAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        // Only READ COMMITTED takes the row once the append ends. A stricter level fails with a serialization error.
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        await using (var command = new NpgsqlCommand(new EventLogSql(_names).ShareSequenceRow, connection))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // A slot that can't be dropped here is left to a later cleanup.
    private async Task TryDropSlotAsync(string slotName)
    {
        using var timeout = new CancellationTokenSource(DropSlotTimeout);

        try
        {
            var connection = new LogicalReplicationConnection(_connectionString);

            await using (connection.ConfigureAwait(false))
            {
                await connection.Open(timeout.Token).ConfigureAwait(false);
                await connection.DropReplicationSlot(slotName, wait: true, timeout.Token).ConfigureAwait(false);
            }

            _logger?.ReplicationSlotDropped(slotName);
        }
        catch (Exception ex)
        {
            _logger?.ReplicationSlotDropFailed(ex, slotName);
        }
    }
}