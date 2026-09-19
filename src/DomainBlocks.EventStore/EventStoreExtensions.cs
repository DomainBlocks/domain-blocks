using DomainBlocks.Core;
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

    public static IEventStore<TEvent, TStreamId, TStreamPos, TLogPos>
        WithMetadataContributors<TEvent, TStreamId, TStreamPos, TLogPos>(
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
                [.. decorated.MetadataContributors, .. contributors],
                decorated.ReadTransforms,
                decorated.IgnoredEventSentinel);
        }

        return new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
            eventStore,
            contributors,
            [],
            Optional.None<TEvent>());
    }

    public static IEventStore<TEvent, TStreamId, TStreamPos, TLogPos>
        WithReadTransforms<TEvent, TStreamId, TStreamPos, TLogPos>(
            this IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
            params IReadEventTransform<TEvent>[] transforms)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        ArgumentNullException.ThrowIfNull(transforms);

        var added = transforms.ToArray();

        if (added.Length == 0)
            return eventStore;

        if (eventStore is EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos> decorated)
        {
            return new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
                decorated.Inner,
                decorated.MetadataContributors,
                [.. decorated.ReadTransforms, .. added],
                decorated.IgnoredEventSentinel);
        }

        return new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
            eventStore,
            [],
            added,
            Optional.None<TEvent>());
    }

    public static IEventStore<TEvent, TStreamId, TStreamPos, TLogPos>
        UseIgnoredEventSentinel<TEvent, TStreamId, TStreamPos, TLogPos>(
            this IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
            TEvent sentinel)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        if (eventStore is EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos> decorated)
        {
            return new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
                decorated.Inner,
                decorated.MetadataContributors,
                decorated.ReadTransforms,
                sentinel);
        }

        return new EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos>(
            eventStore,
            [],
            [],
            sentinel);
    }
}