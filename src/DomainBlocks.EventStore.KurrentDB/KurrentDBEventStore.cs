using DomainBlocks.EventStore.Codecs;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.KurrentDB;

// Inside the namespace so that it shadows DomainBlocks.EventStore.StreamPosition from the parent namespace.
using StreamPosition = global::KurrentDB.Client.StreamPosition;

public static class KurrentDBEventStore
{
    /// <summary>
    /// Creates an event store over an existing client, which the caller owns; the store only borrows it. For the
    /// common case, prefer <see cref="KurrentDBEventStoreBuilder{TEvent}"/>.
    /// </summary>
    public static KurrentDBEventStore<TEvent> Create<TEvent>(
        KurrentDBClient client,
        IEventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventCodec)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(eventCodec);

        return new KurrentDBEventStore<TEvent>(new KurrentDBEventStoreCore<TEvent>(client, eventCodec), ownedClient: null);
    }
}

/// <summary>
/// A KurrentDB event store. The event store operations are those of <see cref="IEventStore{TEvent, TStreamId,
/// TStreamPos, TLogPos}"/>; the metadata contributors and read transforms configured through the builder are already
/// applied. Disposing the store releases the client only when the store created it.
/// </summary>
public sealed class KurrentDBEventStore<TEvent> : IEventStore<TEvent, string, StreamPosition, Position>
    where TEvent : notnull
{
    private readonly IEventStore<TEvent, string, StreamPosition, Position> _inner;
    private readonly IAsyncDisposable? _ownedClient;

    internal KurrentDBEventStore(IEventStore<TEvent, string, StreamPosition, Position> inner, IAsyncDisposable? ownedClient)
    {
        _inner = inner;
        _ownedClient = ownedClient;
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

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<Position>? origin = null,
        ReadAllOptions? options = null)
    {
        return _inner.ReadAll(direction, origin, options);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        return _inner.ReadStream(streamId, direction, origin, options);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionOrigin<Position>? origin = null,
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

        if (_ownedClient is not null)
            await _ownedClient.DisposeAsync().ConfigureAwait(false);
    }
}