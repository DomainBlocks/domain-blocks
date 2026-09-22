using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class PostgresEventStore
{
    /// <remarks>
    /// <paramref name="replicationConnectionStringFallback"/> is used for the replication connection when
    /// <see cref="PostgresReplicationOptions.ConnectionString"/> is not set, before falling back to the data source's
    /// connection string, which carries no password unless security info is persisted. The builder passes the raw
    /// connection string it created the data source from.
    /// </remarks>
    internal static PostgresEventStore<TEvent> Create<TEvent>(
        NpgsqlDataSource dataSource,
        IEventCodec<TEvent, PostgresEventData, string> eventCodec,
        PostgresEventStoreOptions options,
        PostgresEventStoreAdminOptions adminOptions,
        ILogger? logger,
        bool ownsDataSource,
        string? replicationConnectionStringFallback)
        where TEvent : notnull
    {
        var names = new SchemaObjectNames(options.Schema, options.SubscriptionFilter);

        // Refused here if the server could not evaluate it, rather than when the schema is initialized.
        _ = PostgresFilterTranslator.ToPublicationRowFilter(options.SubscriptionFilter);
        var appender = new BatchingAppender(dataSource, names, options, logger);

        var reader = new EventLogReader<TEvent>(
            dataSource,
            new EventLogSql(names),
            options.ReadBatchSize,
            eventCodec);

        var feed = CreateFeed(dataSource, names, options, eventCodec, logger, replicationConnectionStringFallback);

        return new PostgresEventStore<TEvent>(
            dataSource,
            ownsDataSource,
            options,
            adminOptions,
            appender,
            reader,
            feed,
            eventCodec,
            logger);
    }

    private static RefCountedEventLogFeed<EventLogRow<TEvent>> CreateFeed<TEvent>(
        NpgsqlDataSource dataSource,
        SchemaObjectNames names,
        PostgresEventStoreOptions options,
        IEventDecoder<TEvent, PostgresEventData, string> decoder,
        ILogger? logger,
        string? replicationConnectionStringFallback)
        where TEvent : notnull
    {
        var replicationOptions = options.Replication;

        var connectionString = replicationOptions.ConnectionString
                               ?? replicationConnectionStringFallback
                               ?? dataSource.ConnectionString;

        var slotNames = new SlotNameGenerator(replicationOptions.SlotNamePrefix);

        var feedOptions = new EventLogFeedOptions
        {
            RetryDelay = replicationOptions.RetryDelay,
            MaxRetryDelay = replicationOptions.MaxRetryDelay,
            MaxRetryAttempts = replicationOptions.MaxRetryAttempts
        };

        return new RefCountedEventLogFeed<EventLogRow<TEvent>>(() =>
            new EventLogFeed<EventLogRow<TEvent>>(
                ct => ReplicationEventLogSession.OpenAsync(
                    connectionString,
                    slotNames.Next(),
                    names,
                    replicationOptions,
                    decoder,
                    logger,
                    ct),
                feedOptions,
                logger));
    }
}

/// <summary>
/// A PostgreSQL event store: appender, reader and replication feed over a data source. Created by
/// <see cref="PostgresEventStore.Create{TEvent}"/> over a data source the caller owns, or by
/// <see cref="PostgresEventStoreBuilder{TEvent}"/>, which may also create a data source for the store to own. Disposing
/// the store releases its append queue and replication feed, and the data source only when the store owns it.
/// </summary>
public sealed class PostgresEventStore<TEvent> :
    IEventStore<TEvent, string, StreamPosition, LogPosition>,
    IEventFilterExplainer
    where TEvent : notnull
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _ownsDataSource;
    private readonly PostgresEventStoreOptions _options;
    private readonly PostgresEventStoreAdminOptions _adminOptions;

    // As it was when the store was built, which is what the publication of its feed was named after. The options can
    // be set again, and a subscription that then caught up by another filter than it is sent live events by would
    // miss events without a word.
    private readonly EventFilter _subscriptionFilter;

    private readonly IAppender _appender;
    private readonly EventLogReader<TEvent> _reader;
    private readonly RefCountedEventLogFeed<EventLogRow<TEvent>> _feed;
    private readonly IEventCodec<TEvent, PostgresEventData, string> _eventCodec;
    private readonly ILogger? _logger;

    internal PostgresEventStore(
        NpgsqlDataSource dataSource,
        bool ownsDataSource,
        PostgresEventStoreOptions options,
        PostgresEventStoreAdminOptions adminOptions,
        IAppender appender,
        EventLogReader<TEvent> reader,
        RefCountedEventLogFeed<EventLogRow<TEvent>> feed,
        IEventCodec<TEvent, PostgresEventData, string> eventCodec,
        ILogger? logger)
    {
        _dataSource = dataSource;
        _ownsDataSource = ownsDataSource;
        _options = options;
        _subscriptionFilter = options.SubscriptionFilter;
        _adminOptions = adminOptions;
        _appender = appender;
        _reader = reader;
        _feed = feed;
        _eventCodec = eventCodec;
        _logger = logger;
    }

    /// <summary>
    /// Creates the schema, types, tables, append functions and, unless disabled through
    /// <see cref="PostgresEventStoreAdminOptions"/>, the publication this store uses if they do not already exist.
    /// Idempotent and safe to call concurrently from several processes, so it can run on every start-up.
    /// </summary>
    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        return PostgresEventStoreAdmin.EnsureInitializedAsync(_dataSource, _options, _adminOptions, cancellationToken);
    }

    public async Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<StreamPosition>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);
        ArgumentNullException.ThrowIfNull(events);
        ThrowIfContainsNul(streamId, nameof(streamId));

        expectedState ??= ExpectedStreamState.Any<StreamPosition>();
        // Time-ordered ids keep inserts into the commit id index append-mostly rather than scattered across it.
        commitId ??= Guid.CreateVersion7();
        options ??= AppendOptions.Default;

        var encodedEvents = _eventCodec.Encode(events).ToArray();

        if (encodedEvents.Length == 0)
            return;

        foreach (var (_, eventData, metadata) in encodedEvents)
        {
            if (eventData.IsJson)
                ThrowIfContainsNul(eventData.Json, nameof(events));

            if (metadata is not null)
                ThrowIfContainsNul(metadata, nameof(events));
        }

        var request = new AppendRequest(streamId, expectedState.Value, commitId.Value, encodedEvents);

        await _appender.AppendAsync(request, options.Timeout, cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<LogPosition>? origin = null,
        ReadAllOptions? options = null)
    {
        options ??= ReadAllOptions.Default;

        // Planned here rather than as the read is enumerated, so that a filter that is refused is refused at the call.
        var filterPlan = PlanFilter(options.Filter, options.FilterPushdownMode);

        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward ? ReadOrigin.Start : ReadOrigin.End;

            if (direction.ProducesEmptyReadFrom(origin))
                yield break;

            var firstKeyExclusive = EventLogSql.FirstKeyExclusive(direction, origin);

            var events = _reader.ReadAllAsync(
                direction,
                firstKeyExclusive,
                options.MaxCount,
                options.IncludeMetadata,
                filterPlan,
                cancellationToken);

            await foreach (var e in events.ConfigureAwait(false))
                yield return e;
        }
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);

        options ??= ReadStreamOptions.Default;
        var filterPlan = PlanFilter(options.Filter, options.FilterPushdownMode);

        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward ? ReadOrigin.Start : ReadOrigin.End;

            if (direction.ProducesEmptyReadFrom(origin))
            {
                await ThrowIfStreamNotFoundAsync(streamId, options, cancellationToken).ConfigureAwait(false);
                yield break;
            }

            var firstKeyExclusive = EventLogSql.FirstKeyExclusive(direction, origin);
            var isEmpty = true;

            var events = _reader.ReadStreamAsync(
                streamId,
                direction,
                firstKeyExclusive,
                options.MaxCount,
                options.IncludeMetadata,
                filterPlan,
                cancellationToken);

            await foreach (var e in events.ConfigureAwait(false))
            {
                isEmpty = false;
                yield return e;
            }

            // Unlike an empty edge-case read, an empty result here may simply mean the origin is beyond the end of an
            // existing stream, so the stream's existence is checked rather than assumed.
            if (isEmpty)
                await ThrowIfStreamNotFoundAsync(streamId, options, cancellationToken).ConfigureAwait(false);
        }
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> SubscribeToAll(
        SubscriptionOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        options ??= SubscriptionOptions.Default;

        // Catching up is a filtered read. Live rows are not asked of the database, so each is tested against the whole
        // filter, with its types turned into names so that a row can be passed over without being decoded.
        var filter = _subscriptionFilter & options.Filter;
        var filterPlan = PlanFilter(filter, options.FilterPushdownMode);
        var liveFilter = filter.LowerEventTypes(_eventCodec.ResolveEventNames);

        return new SubscriptionAsyncEnumerable<TEvent, LogPosition>(
            _reader,
            _feed,
            (reader, pos, hwMark, ct) => reader.ReadCatchUpAllAsync(pos, hwMark, filterPlan, ct),
            static (_, hwMark, _) => Task.FromResult<LogPosition?>(LogPosition.FromInt64(hwMark)),
            streamId: null,
            liveFilter,
            static ctx => ctx.LogPosition,
            origin,
            options,
            _logger);
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);

        options ??= SubscriptionOptions.Default;

        var filter = _subscriptionFilter & options.Filter;
        var filterPlan = PlanFilter(filter, options.FilterPushdownMode);
        var liveFilter = filter.LowerEventTypes(_eventCodec.ResolveEventNames);

        return new SubscriptionAsyncEnumerable<TEvent, StreamPosition>(
            _reader,
            _feed,
            (reader, pos, hwMark, ct) => reader.ReadCatchUpStreamAsync(streamId, pos, hwMark, filterPlan, ct),
            async (reader, hwMark, ct) =>
                await reader.GetMaxStreamPositionAsync(streamId, hwMark, ct).ConfigureAwait(false) is { } position
                    ? StreamPosition.FromInt64(position)
                    : null,
            streamId,
            liveFilter,
            static ctx => ctx.StreamPosition,
            origin,
            options,
            _logger);
    }

    public EventFilterPlan ExplainFilter(EventFilter filter, FilterPushdownMode pushdownMode = FilterPushdownMode.Prefer)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return PlanFilter(filter, pushdownMode);
    }

    public async ValueTask DisposeAsync()
    {
        await _appender.DisposeAsync().ConfigureAwait(false);
        await _feed.DisposeAsync().ConfigureAwait(false);

        if (_ownsDataSource)
            await _dataSource.DisposeAsync().ConfigureAwait(false);
    }

    private EventFilterPlan PlanFilter(EventFilter filter, FilterPushdownMode pushdownMode)
    {
        var plan = EventFilterPlan.Create(
            filter,
            pushdownMode,
            _eventCodec.ResolveEventNames,
            PostgresFilterTranslator.CanPush,
            _eventCodec.ResolveStoredPath);

        // The filters are given as they are, so that they are only written out if the message is.
        if (plan != EventFilterPlan.Unfiltered)
            _logger?.FilterPlanned(filter, plan.Pushdown, plan.Residual);

        return plan;
    }

    private async Task ThrowIfStreamNotFoundAsync(
        string streamId,
        ReadStreamOptions options,
        CancellationToken cancellationToken)
    {
        if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
            !await _reader.StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
        {
            throw new StreamNotFoundException(streamId);
        }
    }

    /// <summary>
    /// PostgreSQL text and jsonb values cannot contain NUL. Rejecting it here, per caller, keeps one bad payload from
    /// faulting a whole batch of unrelated appends.
    /// </summary>
    private static void ThrowIfContainsNul(string value, string paramName)
    {
        if (value.Contains('\0') || value.Contains("\\u0000", StringComparison.Ordinal))
            throw new ArgumentException("PostgreSQL text values cannot contain NUL characters.", paramName);
    }
}