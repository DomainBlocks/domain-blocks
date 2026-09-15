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
    /// Reads are unaffected. With no contributors the store itself is returned. Disposing the result
    /// disposes the store.
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

        if (eventStore is EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos> decorated)
        {
            return new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
                decorated.Inner,
                [.. decorated.Contributors, .. contributors],
                decorated.Transforms,
                decorated.HasDroppedEventPlaceholder,
                decorated.DroppedEventPlaceholder);
        }

        return new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
            eventStore,
            contributors,
            [],
            hasDroppedEventPlaceholder: false,
            droppedEventPlaceholder: default!);
    }

    /// <summary>
    /// Returns a store that applies the given transforms to events read through <c>ReadStream</c>, <c>ReadAll</c>
    /// and both subscriptions. At most one transform may be registered per source event type. Appends are
    /// unaffected. With no transforms the store itself is returned. Disposing the result
    /// disposes the store.
    /// </summary>
    /// <remarks>
    /// A transform that returns no events throws, because a dropped event would hide its stream position from
    /// consumers that track the last observed position, such as an event-sourced state store. Use the overload with
    /// <c>droppedEventPlaceholder</c> to retire events instead.
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
        ArgumentNullException.ThrowIfNull(transforms);

        return WithReadTransforms(eventStore, transforms, hasDroppedEventPlaceholder: false, default!);
    }

    /// <summary>
    /// Returns a store that applies the given transforms to events read through <c>ReadStream</c>, <c>ReadAll</c>
    /// and both subscriptions, and emits <paramref name="droppedEventPlaceholder"/> in place of any event a
    /// transform drops by returning no events. The placeholder carries the dropped event's context, so consumers
    /// still observe its position; they need only ignore this one type. For stores whose event type is
    /// <see cref="object"/>, <see cref="DroppedEvent.Instance"/> can serve as the placeholder.
    /// </summary>
    /// <param name="eventStore">The store to decorate.</param>
    /// <param name="transforms">The transforms to apply; at most one per source event type.</param>
    /// <param name="droppedEventPlaceholder">
    /// The event emitted in place of a dropped one. Must not itself have a transform registered.
    /// </param>
    public static IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> WithReadTransforms<
        TEvent, TStreamId, TStreamPos, TLogPos>(
        this IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
        IEnumerable<IReadEventTransform<TEvent>> transforms,
        TEvent droppedEventPlaceholder)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        ArgumentNullException.ThrowIfNull(transforms);
        ArgumentNullException.ThrowIfNull(droppedEventPlaceholder);

        return WithReadTransforms(eventStore, transforms, hasDroppedEventPlaceholder: true, droppedEventPlaceholder);
    }

    private static IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> WithReadTransforms<
        TEvent, TStreamId, TStreamPos, TLogPos>(
        IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
        IEnumerable<IReadEventTransform<TEvent>> transforms,
        bool hasDroppedEventPlaceholder,
        TEvent droppedEventPlaceholder)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        var added = transforms.ToArray();

        if (added.Length == 0 && !hasDroppedEventPlaceholder)
            return eventStore;

        if (eventStore is EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos> decorated)
        {
            // A later placeholder replaces an earlier one; otherwise the existing one is kept.
            var keepExisting = !hasDroppedEventPlaceholder && decorated.HasDroppedEventPlaceholder;

            return new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
                decorated.Inner,
                decorated.Contributors,
                [.. decorated.Transforms, .. added],
                keepExisting || hasDroppedEventPlaceholder,
                keepExisting ? decorated.DroppedEventPlaceholder : droppedEventPlaceholder);
        }

        return new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
            eventStore,
            [],
            added,
            hasDroppedEventPlaceholder,
            droppedEventPlaceholder);
    }
}