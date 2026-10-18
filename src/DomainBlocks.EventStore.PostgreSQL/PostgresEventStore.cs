using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Subscriptions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class PostgresEventStore
{
    internal static PostgresEventStore<TEvent> Create<TEvent>(
        NpgsqlDataSource dataSource,
        IEventCodec<TEvent, PostgresEventData, string> eventCodec,
        PostgresEventStoreOptions options,
        PostgresEventStoreAdminOptions adminOptions,
        ILogger? logger,
        bool ownsDataSource)
        where TEvent : notnull
    {
        var names = new SchemaObjectNames(options.Schema);
        var appender = new BatchingAppender(dataSource, names, options, logger);

        var reader = new EventLogReader<TEvent>(
            dataSource,
            new EventLogSql(names),
            options.ReadPageSize,
            eventCodec);

        var subscriptions =
            new CatchUpSubscriptionFactory<EventLogRow<TEvent>, TEvent, string, StreamPosition, LogPosition>(
                () => new PostgresSubscriptionSource<TEvent>(dataSource, reader, names, eventCodec),
                options.Tail,
                logger);

        return new PostgresEventStore<TEvent>(
            dataSource,
            ownsDataSource,
            options,
            adminOptions,
            appender,
            reader,
            subscriptions,
            eventCodec);
    }
}

/// <summary>
/// Represents an event store backed by PostgreSQL. Create one with <see cref="PostgresEventStoreBuilder{TEvent}"/>.
/// </summary>
/// <remarks>
/// Disposing the store disposes its data source only if the builder created it.
/// </remarks>
public sealed class PostgresEventStore<TEvent> : IEventStore<TEvent, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _ownsDataSource;
    private readonly PostgresEventStoreOptions _options;
    private readonly PostgresEventStoreAdminOptions _adminOptions;
    private readonly IAppender _appender;
    private readonly EventLogReader<TEvent> _reader;
    private readonly CatchUpSubscriptionFactory<EventLogRow<TEvent>, TEvent, string, StreamPosition, LogPosition>
        _subscriptions;
    private readonly IEventCodec<TEvent, PostgresEventData, string> _eventCodec;

    internal PostgresEventStore(
        NpgsqlDataSource dataSource,
        bool ownsDataSource,
        PostgresEventStoreOptions options,
        PostgresEventStoreAdminOptions adminOptions,
        IAppender appender,
        EventLogReader<TEvent> reader,
        CatchUpSubscriptionFactory<EventLogRow<TEvent>, TEvent, string, StreamPosition, LogPosition> subscriptions,
        IEventCodec<TEvent, PostgresEventData, string> eventCodec)
    {
        _dataSource = dataSource;
        _ownsDataSource = ownsDataSource;
        _options = options;
        _adminOptions = adminOptions;
        _appender = appender;
        _reader = reader;
        _subscriptions = subscriptions;
        _eventCodec = eventCodec;
    }

    /// <summary>
    /// Creates the schema, types, tables, functions, and publication the store uses, if they do not exist. Idempotent
    /// and safe to call concurrently from several processes.
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
        // Time-ordered, so new commit IDs sort to the end of the commit ID index instead of landing on random pages.
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
        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward ? ReadOrigin.Start : ReadOrigin.End;
            options ??= ReadAllOptions.Default;

            if (direction.ProducesEmptyReadFrom(origin))
                yield break;

            var firstKeyExclusive = EventLogSql.FirstKeyExclusive(direction, origin);

            var events = _reader.ReadAllAsync(
                direction,
                firstKeyExclusive,
                options.MaxCount,
                options.IncludeMetadata,
                options.Filter,
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

        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward ? ReadOrigin.Start : ReadOrigin.End;
            options ??= ReadStreamOptions.Default;

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
                options.Filter,
                cancellationToken);

            await foreach (var e in events.ConfigureAwait(false))
            {
                isEmpty = false;
                yield return e;
            }

            // An empty result may only mean that the origin is past the end of the stream, so check whether the stream
            // exists.
            if (isEmpty)
                await ThrowIfStreamNotFoundAsync(streamId, options, cancellationToken).ConfigureAwait(false);
        }
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> SubscribeToAll(
        SubscriptionOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        return _subscriptions.SubscribeToAll(origin, options);
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);

        return _subscriptions.SubscribeToStream(streamId, origin, options);
    }

    public async ValueTask DisposeAsync()
    {
        await _appender.DisposeAsync().ConfigureAwait(false);
        await _subscriptions.DisposeAsync().ConfigureAwait(false);

        if (_ownsDataSource)
            await _dataSource.DisposeAsync().ConfigureAwait(false);
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

    // A NUL is rejected for each caller here, so one bad payload cannot fault a whole batch of unrelated appends.
    private static void ThrowIfContainsNul(string value, string paramName)
    {
        if (value.Contains('\0') || value.Contains("\\u0000", StringComparison.Ordinal))
            throw new ArgumentException("PostgreSQL text values cannot contain NUL characters.", paramName);
    }
}