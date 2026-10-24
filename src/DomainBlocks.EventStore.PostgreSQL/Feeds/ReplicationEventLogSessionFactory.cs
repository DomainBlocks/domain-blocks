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
internal sealed class ReplicationEventLogSessionFactory<TEvent>(
    string connectionString,
    NpgsqlDataSource dataSource,
    SlotNameGenerator slotNames,
    SchemaObjectNames names,
    PostgresReplicationOptions options,
    IEventDecoder<TEvent, PostgresEventData, string> decoder,
    ILogger? logger) :
    IEventLogSessionFactory<EventLogRow<TEvent>>
    where TEvent : notnull
{
    // A failover slot sends a commit only once the standbys whose physical slots synchronized_standby_slots lists have
    // it. Standbys with sync_replication_slots keep a copy of it, which the feed resumes from after a failover.
    private const string CreateSlotSql = "SELECT pg_create_logical_replication_slot($1, 'pgoutput', failover => true)";

    private const string ServerStateSql =
        "SELECT pg_is_in_recovery(), " +
        "current_setting('synchronous_standby_names') <> '' AND current_setting('synchronized_standby_slots') = ''";

    private const string DropSlotSql = "SELECT pg_drop_replication_slot($1)";

    private const string TerminateWalSenderSql =
        "SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity WHERE pid = $1 AND backend_type = 'walsender'";

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

    private readonly PgOutputReplicationOptions _pgOutputOptions = new(
        names.Publication,
        PgOutputProtocolVersion.V1,
        binary: options.UseBinaryProtocol,
        streamingMode: PgOutputStreamingMode.Off);

    private string? _slotName;
    private bool _hasOpenedSession;

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
        ThrowIfMultipleHosts();
        await CheckServerAsync(cancellationToken).ConfigureAwait(false);

        if (_slotName is not null)
            await PrepareToResumeAsync(_slotName, cancellationToken).ConfigureAwait(false);

        if (_slotName is null)
        {
            _slotName = await CreateSlotAsync(cancellationToken).ConfigureAwait(false);
            _hasOpenedSession = false;
            ResumeLsn = null;
        }

        var connection = new LogicalReplicationConnection(connectionString)
        {
            WalReceiverTimeout = options.WalReceiverTimeout,
            WalReceiverStatusInterval = options.WalReceiverStatusInterval
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
            names,
            decoder);
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
    // the slot until it notices, and that walsender is this feed's, so it is ended rather than waited for. Any other
    // backend, such as one that drops the slot, holds it only while its statement runs, so it is left to finish, and
    // the session waits for it as for a slot in use.
    private async Task PrepareToResumeAsync(string slotName, CancellationToken cancellationToken)
    {
        var (isValid, activePid) = await ReadSlotStateAsync(slotName, cancellationToken).ConfigureAwait(false);

        if (isValid && activePid is { } pid)
        {
            await TryTerminateWalSenderAsync(dataSource, pid, cancellationToken).ConfigureAwait(false);
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
        await using var command = dataSource.CreateCommand(SlotStateSql);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || !reader.GetBoolean(0))
            return (false, null);

        return (true, reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }

    /// <summary>
    /// Ends the backend with the given process ID if it is a walsender, waiting up to five seconds for it to exit.
    /// Ending one of another role needs pg_signal_backend.
    /// </summary>
    internal static async Task TryTerminateWalSenderAsync(
        NpgsqlDataSource dataSource,
        int pid,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(TerminateWalSenderSql);
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
        logger?.ReplicationSlotLost(slotName);
    }

    // Npgsql refuses replication connections with several hosts, and a multi-host data source fails rather than waits
    // while a failover leaves no primary. So the feed follows a failover only through one host that reaches the
    // primary.
    private void ThrowIfMultipleHosts()
    {
        if (HasMultipleHosts(dataSource.ConnectionString))
        {
            throw new NotSupportedException(
                "Live subscriptions require a data source with a single host. Use an endpoint that always reaches " +
                "the primary, such as a writer endpoint, a virtual IP, or a proxy.");
        }

        if (HasMultipleHosts(connectionString))
        {
            throw new NotSupportedException(
                "Live subscriptions require a replication connection string with a single host. Use an endpoint that " +
                "always reaches the primary, such as a writer endpoint, a virtual IP, or a proxy.");
        }

        static bool HasMultipleHosts(string connectionString) =>
            new NpgsqlConnectionStringBuilder(connectionString).Host?.Contains(',') == true;
    }

    // Every session checks, because a session can reach another server after a failover, and the settings can change
    // while the feed runs.
    private async Task CheckServerAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // PostgreSQL 17 added failover slots, and inactive_since, which the cleanup reads.
        if (connection.PostgreSqlVersion.Major < 17)
        {
            throw new NotSupportedException(
                $"Live subscriptions require PostgreSQL 17 or later, but the server is {connection.ServerVersion}.");
        }

        await using var command = new NpgsqlCommand(ServerStateSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        // The feed's slot lives on the primary, and a standby or an Aurora reader can neither create it nor stream from
        // it. An endpoint can reach one for a while during a failover, or while its name still resolves to the old
        // primary.
        if (reader.GetBoolean(0))
            throw new ReplicationServerInRecoveryException(connection.Host);

        // With synchronous standbys, an append is acknowledged once they have it, so a failover keeps it. Unless the
        // slot waits for them too, the feed can deliver an event before they have it, and a failover then loses it.
        if (reader.GetBoolean(1))
            logger?.ReplicationSlotNotSynchronized();
    }

    private async Task<string> CreateSlotAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await DropInactiveSlotsAsync(connection, cancellationToken).ConfigureAwait(false);

        var slotName = slotNames.Next();

        await using (var create = new NpgsqlCommand(CreateSlotSql, connection))
        {
            create.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        logger?.ReplicationSlotCreated(slotName);

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
            select.Parameters.Add(new NpgsqlParameter<string> { TypedValue = options.SlotNamePrefix });
            select.Parameters.Add(new NpgsqlParameter<TimeSpan> { TypedValue = options.InactiveSlotTimeout });

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
                logger?.InactiveReplicationSlotDropped(slotName);
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

        await using (var command = new NpgsqlCommand(new EventLogSql(names).ShareSequenceRow, connection))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // A slot that can't be dropped here is left to a later cleanup.
    private async Task TryDropSlotAsync(string slotName)
    {
        using var timeout = new CancellationTokenSource(DropSlotTimeout);

        try
        {
            var connection = new LogicalReplicationConnection(connectionString);

            await using (connection.ConfigureAwait(false))
            {
                await connection.Open(timeout.Token).ConfigureAwait(false);
                await connection.DropReplicationSlot(slotName, wait: true, timeout.Token).ConfigureAwait(false);
            }

            logger?.ReplicationSlotDropped(slotName);
        }
        catch (Exception ex)
        {
            logger?.ReplicationSlotDropFailed(ex, slotName);
        }
    }
}