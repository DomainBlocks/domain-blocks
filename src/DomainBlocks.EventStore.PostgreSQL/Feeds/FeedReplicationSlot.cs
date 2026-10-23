using System.Data;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.Replication;
using NpgsqlTypes;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// A permanent pgoutput replication slot that one feed streams from, and the feed's progress on it. The feed's sessions
/// stream from the slot one after another, and each session starts where the previous one stopped. Disposing the slot
/// drops it.
/// </summary>
internal sealed class FeedReplicationSlot : IAsyncDisposable
{
    // A failover slot sends a commit only once the standbys whose physical slots synchronized_standby_slots lists have
    // it. Standbys with sync_replication_slots keep a copy of it, which the feed resumes from after a failover.
    private const string CreateSql = "SELECT pg_create_logical_replication_slot($1, 'pgoutput', failover => true)";

    private const string StateSql =
        "SELECT invalidation_reason IS NULL, active_pid FROM pg_replication_slots WHERE slot_name = $1";

    private const string TerminateWalSenderSql =
        "SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity WHERE pid = $1 AND backend_type = 'walsender'";

    // A walsender that hasn't yet noticed that its client has gone holds the slot until wal_sender_timeout. A slot that
    // can't be dropped in time is left to a later cleanup.
    private static readonly TimeSpan DropTimeout = TimeSpan.FromSeconds(10);

    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger? _logger;

    private FeedReplicationSlot(string name, string connectionString, NpgsqlDataSource dataSource, ILogger? logger)
    {
        Name = name;
        _connectionString = connectionString;
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <summary>
    /// Gets the name of the slot.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the end of the last transaction that the feed delivered in full from the slot, which the next session
    /// starts after. It is <see langword="null"/> until the feed delivers a transaction, and the next session then
    /// starts where the slot does.
    /// </summary>
    public NpgsqlLogSequenceNumber? ResumeLsn { get; private set; }

    /// <summary>
    /// Gets the position of the last row that the feed delivered from the slot, or -1 if it has delivered none.
    /// </summary>
    public long LastDeliveredPosition { get; private set; } = -1;

    /// <summary>
    /// Creates a slot with the given name. The slot is created and checked through <paramref name="dataSource"/>, and
    /// dropped through a replication connection to <paramref name="connectionString"/>, which must reach the same
    /// database.
    /// </summary>
    /// <remarks>
    /// The method returns only once queries can see every row that the slot won't stream.
    /// </remarks>
    public static async Task<FeedReplicationSlot> CreateAsync(
        string name,
        string connectionString,
        NpgsqlDataSource dataSource,
        SchemaObjectNames names,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (var create = new NpgsqlCommand(CreateSql, connection))
        {
            create.Parameters.Add(new NpgsqlParameter<string> { TypedValue = name });
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        logger?.ReplicationSlotCreated(name);

        var slot = new FeedReplicationSlot(name, connectionString, dataSource, logger);

        try
        {
            await WaitForInFlightAppendAsync(connection, names, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await slot.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return slot;
    }

    /// <summary>
    /// Records that the feed delivered the row at the given position. If a later session receives the row again, as
    /// part of a transaction that the feed delivered only in part, the session skips it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="position"/> is not after <see cref="LastDeliveredPosition"/>.
    /// </exception>
    public void RecordDeliveredRow(long position)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(position, LastDeliveredPosition);
        LastDeliveredPosition = position;
    }

    /// <summary>
    /// Records that the feed delivered every row of the transaction that ends at the given LSN, so that the next
    /// session starts after it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="endLsn"/> is not after <see cref="ResumeLsn"/>.
    /// </exception>
    public void RecordDeliveredTransaction(NpgsqlLogSequenceNumber endLsn)
    {
        if (ResumeLsn is { } resumeLsn)
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endLsn, resumeLsn);

        ResumeLsn = endLsn;
    }

    /// <summary>
    /// Prepares the slot for the feed's next session.
    /// </summary>
    /// <remarks>
    /// After a session loses its connection, its walsender holds the slot until it notices that the client has gone.
    /// Only the feed's own sessions stream from the slot, so the method ends that walsender rather than wait for it.
    /// Any other process, such as one that is dropping the slot, holds it only while its statement runs. The method
    /// leaves that process to finish and returns <see cref="PrepareForSessionResult.InUse"/>. It returns the same while
    /// a walsender that it can't end holds the slot.
    /// </remarks>
    public async Task<PrepareForSessionResult> PrepareForSessionAsync(CancellationToken cancellationToken)
    {
        var (isValid, activePid) = await ReadStateAsync(cancellationToken).ConfigureAwait(false);

        if (isValid && activePid is { } pid)
        {
            await TerminateIfWalSenderAsync(pid, cancellationToken).ConfigureAwait(false);
            (isValid, activePid) = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!isValid)
            return PrepareForSessionResult.Lost;

        return activePid is null ? PrepareForSessionResult.Ready : PrepareForSessionResult.InUse;
    }

    /// <summary>
    /// Returns whether the slot has been dropped or invalidated.
    /// </summary>
    public async Task<bool> IsLostAsync(CancellationToken cancellationToken)
    {
        var (isValid, _) = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        return !isValid;
    }

    /// <summary>
    /// Drops the slot, waiting up to 10 seconds for a walsender to release it. A slot that can't be dropped is left to
    /// the cleanup that stores run before they create a slot.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(DropTimeout);

        try
        {
            var connection = new LogicalReplicationConnection(_connectionString);

            await using (connection.ConfigureAwait(false))
            {
                await connection.Open(timeout.Token).ConfigureAwait(false);
                await connection.DropReplicationSlot(Name, wait: true, timeout.Token).ConfigureAwait(false);
            }

            _logger?.ReplicationSlotDropped(Name);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedObject)
        {
            // The slot has already been dropped.
        }
        catch (Exception ex)
        {
            _logger?.ReplicationSlotDropFailed(ex, Name);
        }
    }

    // A missing slot is not valid either.
    private async Task<(bool IsValid, int? ActivePid)> ReadStateAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(StateSql);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = Name });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || !reader.GetBoolean(0))
            return (false, null);

        return (true, reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }

    // Ends the backend with the given process ID if it is a walsender, and waits up to five seconds for it to exit.
    // Ending a walsender of another role needs pg_signal_backend.
    private async Task TerminateIfWalSenderAsync(int processId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(TerminateWalSenderSql);
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = processId });

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            // The walsender ends on its own once it notices that its client has gone.
            _logger?.WalSenderTerminationDenied(ex, processId, Name);
        }
    }

    // A transaction can commit just before the slot's consistent point and still be invisible to queries, while the
    // slot won't stream it. Appends hold the sequence row until they are visible, so taking the row waits for any such
    // append. Once the first session opens, queries see every row that the slot won't stream.
    private static async Task WaitForInFlightAppendAsync(
        NpgsqlConnection connection,
        SchemaObjectNames names,
        CancellationToken cancellationToken)
    {
        // Only READ COMMITTED takes the row once the append ends. A stricter level fails with a serialization error.
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        await using (var command = new NpgsqlCommand(new EventLogSql(names).ShareSequenceRow, connection))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}