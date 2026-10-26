using DomainBlocks.EventStore.Codecs;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Opens one feed's logical replication sessions on a <see cref="FeedReplicationSlot"/>, which it creates for the
/// first session and drops when it is disposed. Each later session resumes on the slot where the previous one stopped,
/// so a lost connection costs no reset. A dropped or invalidated slot is replaced, and the feed resets.
/// </summary>
/// <remarks>
/// <para>
/// Slots are created and checked through <c>dataSource</c>, which must reach the same database as
/// <c>connectionString</c>.
/// </para>
/// <para>
/// Before it creates a slot, and every <see cref="PostgresReplicationOptions.InactiveSlotTimeout"/> while the feed
/// runs, the factory drops the slots that stores left behind, through an <see cref="InactiveSlotScavenger"/>.
/// </para>
/// </remarks>
internal sealed class ReplicationEventLogSessionFactory<TEvent>(
    string connectionString,
    NpgsqlDataSource dataSource,
    SlotNameGenerator slotNameGenerator,
    SchemaObjectNames names,
    PostgresReplicationOptions options,
    IEventDecoder<TEvent, PostgresEventData, string> decoder,
    ILogger? logger) :
    IEventLogSessionFactory<EventLogRow<TEvent>>
    where TEvent : notnull
{
    private const string StandbySlotsUnsetSql =
        "SELECT current_setting('synchronous_standby_names') <> '' " +
        "AND current_setting('synchronized_standby_slots') = ''";

    private readonly PgOutputReplicationOptions _pgOutputOptions = new(
        names.Publication,
        PgOutputProtocolVersion.V1,
        binary: options.UseBinaryProtocol,
        streamingMode: PgOutputStreamingMode.Off);

    private readonly InactiveSlotScavenger _scavenger = new(
        dataSource,
        slotNameGenerator.NamePattern,
        options.InactiveSlotTimeout,
        logger);

    private FeedReplicationSlot? _slot;
    private bool _hasOpenedSessionOnSlot;

    public async Task<IEventLogSession<EventLogRow<TEvent>>> OpenAsync(CancellationToken cancellationToken)
    {
        ThrowIfMultipleHosts();
        await CheckServerAsync(cancellationToken).ConfigureAwait(false);

        // A long-lived feed may never create another slot, so the scavenger also runs on a timer.
        _scavenger.EnsureStarted();

        if (_slot is not null)
        {
            switch (await _slot.PrepareForSessionAsync(cancellationToken).ConfigureAwait(false))
            {
                case PrepareForSessionResult.Lost:
                    logger?.ReplicationSlotLost(_slot.Name);
                    await _slot.DisposeAsync().ConfigureAwait(false);
                    _slot = null;
                    break;

                case PrepareForSessionResult.InUse:
                    throw new ReplicationSlotInUseException(_slot.Name);
            }
        }

        if (_slot is null)
        {
            _slot = await CreateSlotAsync(cancellationToken).ConfigureAwait(false);
            _hasOpenedSessionOnSlot = false;
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
        var isResumed = _hasOpenedSessionOnSlot;
        _hasOpenedSessionOnSlot = true;

        return new ReplicationEventLogSession<TEvent>(connection, _slot, isResumed, _pgOutputOptions, names, decoder);
    }

    public async ValueTask DisposeAsync()
    {
        await _scavenger.DisposeAsync().ConfigureAwait(false);

        if (_slot is null)
            return;

        await _slot.DisposeAsync().ConfigureAwait(false);
        _slot = null;
    }

    private async Task<FeedReplicationSlot> CreateSlotAsync(CancellationToken cancellationToken)
    {
        await _scavenger.DropInactiveSlotsAsync(cancellationToken).ConfigureAwait(false);

        return await FeedReplicationSlot
            .CreateAsync(slotNameGenerator.Next(), connectionString, dataSource, names, logger, cancellationToken)
            .ConfigureAwait(false);
    }

    // Npgsql refuses replication connections with multiple hosts, and a multi-host data source fails rather than waits
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

        // PostgreSQL 17 added failover slots, and inactive_since, which the scavenger reads.
        if (connection.PostgreSqlVersion.Major < 17)
        {
            throw new NotSupportedException(
                $"Live subscriptions require PostgreSQL 17 or later, but the server is {connection.ServerVersion}.");
        }

        // With synchronous standbys, an append is acknowledged once they have it, so a failover keeps it. Unless the
        // slot waits for them too, the feed can deliver an event before they have it, and a failover then loses it.
        await using var command = new NpgsqlCommand(StandbySlotsUnsetSql, connection);

        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true)
            logger?.ReplicationSlotNotSynchronized();
    }
}