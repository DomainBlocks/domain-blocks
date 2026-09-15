using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;

namespace DomainBlocks.EventStore;

public static class EventStoreExtensions
{
    extension<TEvent, TStreamId, TStreamPos, TLogPos>(
        IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        public Task AppendAsync(
            TStreamId streamId,
            IEnumerable<TEvent> events,
            ExpectedStreamState<TStreamPos>? expectedState = null,
            Guid? commitId = null,
            AppendOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return eventStore.AppendAsync(
                streamId,
                events.Select(x => AppendableEvent.Create(x)),
                expectedState,
                commitId,
                options,
                cancellationToken);
        }
    }

    // The hooks below are classic extension methods rather than members of the extension block above: with a
    // contravariant element type such as IMetadataContributor<in TEvent>, the compiler's nullable analysis reports a
    // false CS8620 for every expanded params argument inside an extension block.

    /// <summary>
    /// Returns a store that runs the given contributors, in order, for every appended event. Entries supplied
    /// explicitly on an <see cref="AppendableEvent{TPayload}"/> override contributed entries with the same key.
    /// Reads are unaffected. With no contributors the store itself is returned. The result forwards
    /// <see cref="IAsyncDisposable"/> to the store.
    /// </summary>
    public static IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> WithMetadataContributors<
        TEvent, TStreamId, TStreamPos, TLogPos>(
        this IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
        params IMetadataContributor<TEvent>[] contributors)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        ArgumentNullException.ThrowIfNull(contributors);

        if (contributors.Length == 0)
            return eventStore;

        return eventStore is EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos> decorated
            ? new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
                decorated.Inner,
                [.. decorated.Contributors, .. contributors],
                decorated.Transforms,
                decorated.AllowDroppingEvents)
            : new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(eventStore, contributors, [], false);
    }

    /// <summary>
    /// Returns a store that applies the given transforms to events read through <c>ReadStream</c>, <c>ReadAll</c>
    /// and both subscriptions. At most one transform may be registered per source event type. Appends are
    /// unaffected. With no transforms the store itself is returned. The result forwards
    /// <see cref="IAsyncDisposable"/> to the store.
    /// </summary>
    /// <remarks>
    /// A transform that returns no events throws, because a dropped event hides its stream position from consumers
    /// that track the last observed position, such as an event-sourced state store. Use the overload with
    /// <c>allowDroppingEvents</c> to permit it.
    /// </remarks>
    public static IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> WithReadTransforms<
        TEvent, TStreamId, TStreamPos, TLogPos>(
        this IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
        params IReadEventTransform<TEvent>[] transforms)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return eventStore.WithReadTransforms(transforms, allowDroppingEvents: false);
    }

    /// <summary>
    /// Returns a store that applies the given transforms to events read through <c>ReadStream</c>, <c>ReadAll</c>
    /// and both subscriptions, optionally permitting a transform to drop its source event.
    /// </summary>
    /// <param name="eventStore">The store to decorate.</param>
    /// <param name="transforms">The transforms to apply; at most one per source event type.</param>
    /// <param name="allowDroppingEvents">
    /// Whether a transform may return no events, dropping the source event. A dropped event hides its stream
    /// position from consumers that track the last observed position, such as an event-sourced state store, so
    /// "ignore this event" normally belongs in the consumer rather than in a transform.
    /// </param>
    public static IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> WithReadTransforms<
        TEvent, TStreamId, TStreamPos, TLogPos>(
        this IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
        IEnumerable<IReadEventTransform<TEvent>> transforms,
        bool allowDroppingEvents)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        ArgumentNullException.ThrowIfNull(transforms);

        var added = transforms.ToArray();

        if (added.Length == 0)
            return eventStore;

        return eventStore is EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos> decorated
            ? new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
                decorated.Inner,
                decorated.Contributors,
                [.. decorated.Transforms, .. added],
                decorated.AllowDroppingEvents || allowDroppingEvents)
            : new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(eventStore, [], added, allowDroppingEvents);
    }
}