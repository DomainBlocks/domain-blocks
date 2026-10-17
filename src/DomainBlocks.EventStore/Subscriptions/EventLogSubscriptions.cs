using DomainBlocks.EventStore.Filtering;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Creates the catch-up subscriptions to a store's event log, from the store's
/// <see cref="IEventSubscriptionSource{TSourceEvent,TStreamId,TStreamPos,TLogPos}"/>. A store creates one, and disposes
/// it when the store is disposed.
/// </summary>
/// <remarks>
/// All of the store's live subscriptions share one read of the log, which runs while any subscription is being
/// enumerated. Each catch-up reads through a source of its own.
/// </remarks>
/// <typeparam name="TEvent">The type of event payload.</typeparam>
/// <typeparam name="TSourceEvent">
/// An event as the store holds it, which is decoded only if a subscription selects it.
/// </typeparam>
/// <typeparam name="TStreamId">The type of stream ID.</typeparam>
/// <typeparam name="TStreamPos">The type of position in a stream.</typeparam>
/// <typeparam name="TLogPos">The type of position in the event log.</typeparam>
public sealed class EventLogSubscriptions<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos> : IAsyncDisposable
    where TEvent : notnull
    where TSourceEvent : IFilterableEvent, IReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>
    where TStreamId : notnull
    where TStreamPos : struct, IPosition<TStreamPos>
    where TLogPos : struct, IPosition<TLogPos>, IComparable<TLogPos>
{
    private readonly Func<IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos>> _sourceFactory;
    private readonly EventLogTail<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos> _tail;
    private readonly EventLogTailOptions _tailOptions;
    private readonly ILogger? _logger;

    /// <param name="sourceFactory">
    /// Creates a source for each reader: the shared read of the log each time it starts, and each catch-up.
    /// </param>
    /// <param name="options">
    /// The options for the shared read of the log. Its retry delays also apply to each catch-up's reads.
    /// </param>
    /// <param name="logger">The logger for the shared read and the subscriptions.</param>
    public EventLogSubscriptions(
        Func<IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos>> sourceFactory,
        EventLogTailOptions? options = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);

        _sourceFactory = sourceFactory;
        _tailOptions = options ?? EventLogTailOptions.Default;
        _tail = new EventLogTail<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos>(sourceFactory, options, logger);
        _logger = logger;
    }

    /// <summary>
    /// Creates a catch-up subscription to the events in every stream, which follows positions in the log and starts
    /// when it is enumerated.
    /// </summary>
    /// <param name="origin">Where the subscription starts. It starts at the end of the log by default.</param>
    /// <param name="options">The subscription's options.</param>
    public IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToAll(
        SubscriptionOrigin<TLogPos>? origin = null,
        SubscriptionOptions? options = null)
    {
        return new CatchUpSubscription<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos, TLogPos>(
            _tail,
            _sourceFactory,
            LogCatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos>.Instance,
            origin,
            options,
            _tailOptions,
            _logger);
    }

    /// <summary>
    /// Creates a catch-up subscription to the events in one stream, which follows positions in the stream and starts
    /// when it is enumerated.
    /// </summary>
    /// <param name="streamId">The stream to follow.</param>
    /// <param name="origin">Where the subscription starts. It starts at the end of the stream by default.</param>
    /// <param name="options">The subscription's options.</param>
    public IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToStream(
        TStreamId streamId,
        SubscriptionOrigin<TStreamPos>? origin = null,
        SubscriptionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        return new CatchUpSubscription<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos, TStreamPos>(
            _tail,
            _sourceFactory,
            new StreamCatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos>(streamId),
            origin,
            options,
            _tailOptions,
            _logger);
    }

    /// <summary>
    /// Stops the shared read of the log. Subscriptions that are still enumerating stop receiving live events.
    /// </summary>
    public ValueTask DisposeAsync() => _tail.DisposeAsync();
}