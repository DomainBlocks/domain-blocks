using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Drops the replication slots that stores left behind. These are the inactive slots in the database whose names match
/// <c>slotNamePattern</c>, and that are invalidated or have been inactive for longer than <c>inactiveSlotTimeout</c>.
/// </summary>
/// <remarks>
/// A process that stops without dropping its slot leaves it behind, holding WAL. A live feed's slot is inactive only
/// while the feed reconnects. If the slot is dropped anyway, the feed creates a new one and resets.
/// </remarks>
internal sealed class InactiveSlotScavenger(
    NpgsqlDataSource dataSource,
    string slotNamePattern,
    TimeSpan inactiveSlotTimeout,
    ILogger? logger) :
    IAsyncDisposable
{
    private const string DropSlotSql = "SELECT pg_drop_replication_slot($1)";

    // The slots in this database whose names match the pattern ($1) and that are invalidated or inactive for longer
    // than $2.
    private const string SelectInactiveSlotsSql =
        """
        SELECT slot_name
        FROM pg_replication_slots
        WHERE database = current_database()
          AND slot_type = 'logical'
          AND NOT active
          AND slot_name ~ $1
          AND (invalidation_reason IS NOT NULL OR inactive_since < now() - $2)
        """;

    private readonly CancellationTokenSource _stop = new();
    private Task? _periodicCleanup;

    /// <summary>
    /// Starts dropping inactive slots every <c>inactiveSlotTimeout</c> until the scavenger is disposed. It does nothing
    /// if the scavenger has already started.
    /// </summary>
    public void EnsureStarted() => _periodicCleanup ??= DropInactiveSlotsPeriodicallyAsync(_stop.Token);

    /// <summary>
    /// Drops the inactive slots once, through a connection that it takes from the pool only while it runs.
    /// </summary>
    public async Task DropInactiveSlotsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var slotNames = new List<string>();

        await using (var select = new NpgsqlCommand(SelectInactiveSlotsSql, connection))
        {
            select.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotNamePattern });
            select.Parameters.Add(new NpgsqlParameter<TimeSpan> { TypedValue = inactiveSlotTimeout });

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
                logger?.InactiveReplicationSlotDropSkipped(ex, slotName);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);

        if (_periodicCleanup is not null)
            await _periodicCleanup.ConfigureAwait(false);

        _stop.Dispose();
    }

    private async Task DropInactiveSlotsPeriodicallyAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(inactiveSlotTimeout);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await DropInactiveSlotsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger?.InactiveReplicationSlotCleanupFailed(ex, inactiveSlotTimeout);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The scavenger has been disposed.
        }
    }
}