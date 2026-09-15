using DomainBlocks.EventStore.Codecs;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class PostgresEventStore
{
    /// <summary>
    /// Creates an event store over an existing data source, which must have been built with
    /// <see cref="NpgsqlDataSourceBuilderExtensions.UsePostgresEventStore"/> for the same schema. The caller owns the
    /// data source's lifetime; the store only borrows connections from it. The schema must have been initialized,
    /// either with <see cref="PostgresEventStoreAdmin.EnsureInitializedAsync"/> or through the returned store's
    /// <see cref="PostgresEventStore{TEvent}.EnsureInitializedAsync"/>. For the common case, prefer
    /// <see cref="PostgresEventStoreBuilder{TEvent}"/>.
    /// </summary>
    public static PostgresEventStore<TEvent> Create<TEvent>(
        NpgsqlDataSource dataSource,
        IEventCodec<TEvent, PostgresEventData, string> eventCodec,
        PostgresEventStoreOptions? options = null,
        ILogger? logger = null)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(eventCodec);

        options ??= new PostgresEventStoreOptions();

        var core = PostgresEventStoreCore.Create(dataSource, eventCodec, options, logger);

        return new PostgresEventStore<TEvent>(core, dataSource, ownsDataSource: false, options);
    }
}

/// <summary>
/// A PostgreSQL event store. The event store operations are those of <see cref="IEventStore{TEvent, TStreamId,
/// TStreamPos, TLogPos}"/>; the metadata contributors and read transforms configured through the builder are already
/// applied. Disposing the store releases its append queue and replication feed, and the data source only when the
/// store created it.
/// </summary>
public sealed class PostgresEventStore<TEvent> : IEventStore<TEvent, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    private readonly IEventStore<TEvent, string, StreamPosition, LogPosition> _inner;
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _ownsDataSource;
    private readonly PostgresEventStoreOptions _options;

    internal PostgresEventStore(
        IEventStore<TEvent, string, StreamPosition, LogPosition> inner,
        NpgsqlDataSource dataSource,
        bool ownsDataSource,
        PostgresEventStoreOptions options)
    {
        _inner = inner;
        _dataSource = dataSource;
        _ownsDataSource = ownsDataSource;
        _options = options;
    }

    /// <summary>
    /// Creates the schema, types, tables, append functions and publication this store uses if they do not already
    /// exist. Idempotent and safe to call concurrently from several processes, so it can run on every start-up.
    /// </summary>
    public Task EnsureInitializedAsync(
        PostgresEventStoreAdminOptions? adminOptions = null,
        CancellationToken cancellationToken = default)
    {
        return PostgresEventStoreAdmin.EnsureInitializedAsync(_dataSource, _options, adminOptions, cancellationToken);
    }

    public Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<StreamPosition>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.AppendAsync(streamId, events, expectedState, commitId, options, cancellationToken);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<LogPosition>? origin = null,
        ReadAllOptions? options = null)
    {
        return _inner.ReadAll(direction, origin, options);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        return _inner.ReadStream(streamId, direction, origin, options);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        return _inner.SubscribeToAll(origin, options);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        return _inner.SubscribeToStream(streamId, origin, options);
    }

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);

        if (_ownsDataSource)
            await _dataSource.DisposeAsync().ConfigureAwait(false);
    }
}