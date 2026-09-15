using DomainBlocks.EventStore.Codecs;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStore
{
    /// <summary>
    /// Creates an event store over an existing client, which the caller owns; the store only borrows it. The
    /// database's indexes must have been created, either with <see cref="MongoEventStoreAdmin.EnsureInitializedAsync"/>
    /// or through the returned store's <see cref="MongoEventStore{TEvent}.EnsureInitializedAsync"/>. For the common
    /// case, prefer <see cref="MongoEventStoreBuilder{TEvent}"/>.
    /// </summary>
    public static MongoEventStore<TEvent> Create<TEvent>(
        IMongoClient mongoClient,
        IEventCodec<TEvent, BsonValue, BsonValue> eventCodec,
        MongoEventStoreOptions? options = null,
        ILogger? logger = null)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(mongoClient);
        ArgumentNullException.ThrowIfNull(eventCodec);

        options ??= new MongoEventStoreOptions();

        var core = MongoEventStoreCore.Create(mongoClient, eventCodec, options, logger);

        return new MongoEventStore<TEvent>(core, mongoClient, ownedClient: null, options);
    }
}

/// <summary>
/// A MongoDB event store. The event store operations are those of <see cref="IEventStore{TEvent, TStreamId,
/// TStreamPos, TLogPos}"/>; the metadata contributors and read transforms configured through the builder are already
/// applied. Disposing the store releases its append queue, and the client only when the store created it.
/// </summary>
public sealed class MongoEventStore<TEvent> : IEventStore<TEvent, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    private readonly IEventStore<TEvent, string, StreamPosition, LogPosition> _inner;
    private readonly IMongoClient _client;
    private readonly IDisposable? _ownedClient;
    private readonly MongoEventStoreOptions _options;

    internal MongoEventStore(
        IEventStore<TEvent, string, StreamPosition, LogPosition> inner,
        IMongoClient client,
        IDisposable? ownedClient,
        MongoEventStoreOptions options)
    {
        _inner = inner;
        _client = client;
        _ownedClient = ownedClient;
        _options = options;
    }

    /// <summary>
    /// Creates the event log's indexes if they do not already exist. Idempotent, so it can run on every start-up.
    /// </summary>
    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        return MongoEventStoreAdmin.EnsureInitializedAsync(_client, _options, cancellationToken);
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
        _ownedClient?.Dispose();
    }
}