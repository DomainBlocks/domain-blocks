using DomainBlocks.EventStore.Codecs;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Opens one feed's logical replication sessions on a <see cref="FeedReplicationSlot"/>, which it creates for the
/// first session and drops when it is disposed. Each later session resumes on the slot where the previous one stopped,
/// so a lost connection costs no reset. A dropped or invalidated slot does cost one, because the factory replaces it
/// with a new slot.
/// </summary>
/// <remarks>
/// <para>
/// Slots are created and checked through <c>dataSource</c>, which must reach the same database as
/// <c>connectionString</c>.
/// </para>
/// <para>
/// Before it creates a slot, and every <see cref="PostgresReplicationOptions.InactiveSlotTimeout"/> while the feed
/// runs, the factory drops the slots that stores left behind, through an <see cref="InactiveSlotCleaner"/>.
/// </para>
/// </remarks>
internal sealed class ReplicationEventLogSessionFactory<TEvent> : IEventLogSessionFactory<EventLogRow<TEvent>>
    where TEvent : notnull
{
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly SlotNameGenerator _slotNameGenerator;
    private readonly SchemaObjectNames _names;
    private readonly PostgresReplicationOptions _options;
    private readonly PgOutputReplicationOptions _pgOutputOptions;
    private readonly IEventDecoder<TEvent, PostgresEventData, string> _decoder;
    private readonly ILogger? _logger;
    private readonly InactiveSlotCleaner _cleaner;
    private FeedReplicationSlot? _slot;
    private bool _hasOpenedSessionOnSlot;

    public ReplicationEventLogSessionFactory(
        string connectionString,
        NpgsqlDataSource dataSource,
        SlotNameGenerator slotNameGenerator,
        SchemaObjectNames names,
        PostgresReplicationOptions options,
        IEventDecoder<TEvent, PostgresEventData, string> decoder,
        ILogger? logger)
    {
        _connectionString = connectionString;
        _dataSource = dataSource;
        _slotNameGenerator = slotNameGenerator;
        _names = names;
        _options = options;
        _decoder = decoder;
        _logger = logger;

        _pgOutputOptions = new PgOutputReplicationOptions(
            names.Publication,
            PgOutputProtocolVersion.V1,
            binary: options.UseBinaryProtocol,
            streamingMode: PgOutputStreamingMode.Off);

        _cleaner = new InactiveSlotCleaner(
            dataSource,
            slotNameGenerator.NamePattern,
            options.InactiveSlotTimeout,
            logger);
    }

    public async Task<IEventLogSession<EventLogRow<TEvent>>> OpenAsync(CancellationToken cancellationToken)
    {
        // A long-lived feed may never create another slot, so the cleanup also runs on a timer.
        _cleaner.EnsureStarted();

        if (_slot is not null && !await _slot.TryPrepareForSessionAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger?.ReplicationSlotLost(_slot.Name);
            await _slot.DisposeAsync().ConfigureAwait(false);
            _slot = null;
        }

        if (_slot is null)
        {
            _slot = await CreateSlotAsync(cancellationToken).ConfigureAwait(false);
            _hasOpenedSessionOnSlot = false;
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
        var isResumed = _hasOpenedSessionOnSlot;
        _hasOpenedSessionOnSlot = true;

        return new ReplicationEventLogSession<TEvent>(connection, _slot, isResumed, _pgOutputOptions, _names, _decoder);
    }

    public async ValueTask DisposeAsync()
    {
        await _cleaner.DisposeAsync().ConfigureAwait(false);

        if (_slot is null)
            return;

        await _slot.DisposeAsync().ConfigureAwait(false);
        _slot = null;
    }

    private async Task<FeedReplicationSlot> CreateSlotAsync(CancellationToken cancellationToken)
    {
        await CheckServerVersionAsync(cancellationToken).ConfigureAwait(false);
        await _cleaner.DropInactiveSlotsAsync(cancellationToken).ConfigureAwait(false);

        return await FeedReplicationSlot
            .CreateAsync(_slotNameGenerator.Next(), _connectionString, _dataSource, _names, _logger, cancellationToken)
            .ConfigureAwait(false);
    }

    // The cleanup reads inactive_since, which PostgreSQL 17 added.
    private async Task CheckServerVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (connection.PostgreSqlVersion.Major < 17)
        {
            throw new NotSupportedException(
                $"Live subscriptions require PostgreSQL 17 or later, but the server is {connection.ServerVersion}.");
        }
    }
}