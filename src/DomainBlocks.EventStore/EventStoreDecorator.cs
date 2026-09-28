using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using DomainBlocks.Core;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;

namespace DomainBlocks.EventStore;

internal sealed class EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos> :
    IEventStore<TEvent, TStreamId, TStreamPos, TLogPos>,
    IEventFilterExplainer
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    // Guards against transform cycles (A -> B -> A) that the same-type check cannot see.
    private const int MaxTransformDepth = 32;

    private readonly IMetadataContributor<TEvent>[] _metadataContributors;
    private readonly FrozenDictionary<Type, IReadEventTransform<TEvent>> _readTransforms;
    private readonly EventFilter _transformed;

    public EventStoreDecorator(
        IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> inner,
        IEnumerable<IMetadataContributor<TEvent>> metadataContributors,
        IEnumerable<IReadEventTransform<TEvent>> readTransforms,
        Optional<TEvent> ignoredEventSentinel)
    {
        _metadataContributors = [.. metadataContributors];

        var transformsByType = new Dictionary<Type, IReadEventTransform<TEvent>>();

        foreach (var transform in readTransforms)
        {
            if (!transformsByType.TryAdd(transform.SourceEventType, transform))
            {
                throw new ArgumentException(
                    $"More than one read event transform is registered for source type " +
                    $"'{transform.SourceEventType.Name}'.",
                    nameof(readTransforms));
            }
        }

        _readTransforms = transformsByType.ToFrozenDictionary();
        _transformed = EventFilter.AnyOf(_readTransforms.Keys.Select(x => new EventTypeFilter(x, null, null)));

        if (ignoredEventSentinel.HasValue && _readTransforms.ContainsKey(ignoredEventSentinel.Value.GetType()))
        {
            throw new ArgumentException(
                $"The ignored event sentinel type '{ignoredEventSentinel.Value.GetType().Name}' must not have a " +
                "read event transform registered.",
                nameof(ignoredEventSentinel));
        }

        Inner = inner;
        IgnoredEventSentinel = ignoredEventSentinel;
    }

    public IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> Inner { get; }

    public IReadOnlyList<IMetadataContributor<TEvent>> MetadataContributors => _metadataContributors;

    public IReadOnlyList<IReadEventTransform<TEvent>> ReadTransforms => _readTransforms.Values;

    public Optional<TEvent> IgnoredEventSentinel { get; }

    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) =>
        Inner.EnsureInitializedAsync(cancellationToken);

    public Task AppendAsync(
        TStreamId streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<TStreamPos>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        return _metadataContributors.Length == 0
            ? Inner.AppendAsync(streamId, events, expectedState, commitId, options, cancellationToken)
            : AppendWithMetadataAsync(streamId, events, expectedState, commitId, options, cancellationToken);
    }

    private async Task AppendWithMetadataAsync(
        TStreamId streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<TStreamPos>? expectedState,
        Guid? commitId,
        AppendOptions? options,
        CancellationToken cancellationToken)
    {
        // The store has consumed every event by the time the append operation completes, so the buffer can go back to
        // the pool.
        using var buffer = new MetadataBuffer();

        await Inner
            .AppendAsync(
                streamId,
                ContributeMetadata(events, buffer),
                expectedState,
                commitId,
                options,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TLogPos>? origin = null,
        ReadAllOptions? options = null)
    {
        if (_readTransforms.Count == 0)
            return Inner.ReadAll(direction, origin, options);

        options ??= ReadAllOptions.Default;

        if (!TryWiden(options.Filter, out var storeFilter, out var readsMetadata))
            return TransformAsync(Inner.ReadAll(direction, origin, options));

        // The store cannot count the events that the filter selects, as it does not see what they turn into.
        var storeOptions = options with
        {
            Filter = storeFilter,
            MaxCount = null,
            IncludeMetadata = options.IncludeMetadata || readsMetadata
        };

        return TransformAsync(
            Inner.ReadAll(direction, origin, storeOptions),
            options.Filter,
            options.MaxCount,
            options.IncludeMetadata);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadStream(
        TStreamId streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TStreamPos>? origin = null,
        ReadStreamOptions? options = null)
    {
        if (_readTransforms.Count == 0)
            return Inner.ReadStream(streamId, direction, origin, options);

        options ??= ReadStreamOptions.Default;

        if (!TryWiden(options.Filter, out var storeFilter, out var readsMetadata))
            return TransformAsync(Inner.ReadStream(streamId, direction, origin, options));

        var storeOptions = options with
        {
            Filter = storeFilter,
            MaxCount = null,
            IncludeMetadata = options.IncludeMetadata || readsMetadata
        };

        return TransformAsync(
            Inner.ReadStream(streamId, direction, origin, storeOptions),
            options.Filter,
            options.MaxCount,
            options.IncludeMetadata);
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToAll(
        SubscriptionOrigin<TLogPos>? origin = null,
        SubscriptionOptions? options = null)
    {
        if (_readTransforms.Count == 0)
            return Inner.SubscribeToAll(origin, options);

        options ??= SubscriptionOptions.Default;

        var outputFilter = TryWiden(options.Filter, out var storeFilter, out _) ? options.Filter : null;
        var messages = Inner.SubscribeToAll(origin, options with { Filter = storeFilter });

        return TransformSubscriptionAsync(
            messages,
            outputFilter,
            options.CheckpointInterval,
            x => SubscriptionMessage.LogCheckpoint<TEvent, TStreamId, TStreamPos, TLogPos>(x.LogPosition));
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToStream(
        TStreamId streamId,
        SubscriptionOrigin<TStreamPos>? origin = null,
        SubscriptionOptions? options = null)
    {
        if (_readTransforms.Count == 0)
            return Inner.SubscribeToStream(streamId, origin, options);

        options ??= SubscriptionOptions.Default;

        var outputFilter = TryWiden(options.Filter, out var storeFilter, out _) ? options.Filter : null;
        var messages = Inner.SubscribeToStream(streamId, origin, options with { Filter = storeFilter });

        return TransformSubscriptionAsync(
            messages,
            outputFilter,
            options.CheckpointInterval,
            x => SubscriptionMessage.StreamCheckpoint<TEvent, TStreamId, TStreamPos, TLogPos>(x.StreamPosition));
    }

    // What the store is asked, which with read transforms is more than the filter selects.
    public EventFilterPlan ExplainFilter(EventFilter filter, FilterPushdownMode pushdownMode = FilterPushdownMode.Prefer)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (_readTransforms.Count > 0)
            TryWiden(filter, out filter, out _);

        return Inner.ExplainFilter(filter, pushdownMode);
    }

    public ValueTask DisposeAsync() => Inner.DisposeAsync();

    /// <summary>
    /// A filter is about the events that a caller is given. A transform changes an event's type and nothing else that a
    /// filter goes by, so a filter that says nothing of types is left to the store as it is. Of one that does, the store
    /// is asked for more: wherever the filter says something of a type, it also lets through every event that a
    /// transform applies to. What those turn into is then tested against the filter as it was written.
    /// </summary>
    private bool TryWiden(EventFilter filter, out EventFilter storeFilter, out bool readsMetadata)
    {
        var leaves = filter.GetLeafNodes().ToArray();

        if (!leaves.Any(x => x is EventTypeFilter))
        {
            storeFilter = filter;
            readsMetadata = false;
            return false;
        }

        // Under a negation, letting more through takes matching less.
        storeFilter = filter.Rewrite((leaf, isNegated) => leaf switch
        {
            EventTypeFilter when isNegated => leaf & !_transformed,
            EventTypeFilter => leaf | _transformed,
            _ => leaf
        });

        readsMetadata = leaves.Any(x => x is MetadataExistsFilter or MetadataValueFilter);
        return true;
    }

    private IEnumerable<AppendableEvent<TEvent>> ContributeMetadata(
        IEnumerable<AppendableEvent<TEvent>> events,
        MetadataBuffer buffer)
    {
        foreach (var @event in events)
            yield return AppendableEvent.Create(@event.Payload, ContributeMetadata(@event, buffer));
    }

    private ReadOnlyMemory<KeyValuePair<string, string>> ContributeMetadata(
        in AppendableEvent<TEvent> @event,
        MetadataBuffer buffer)
    {
        buffer.BeginEvent();

        var writer = new MetadataWriter(buffer);

        foreach (var contributor in _metadataContributors)
            contributor.Contribute(@event.Payload, writer);

        foreach (var (key, value) in @event.Metadata)
            buffer.Set(key, value);

        return buffer.EndEvent();
    }

    // With an output filter, every event is tested against it: what a transform makes of an event, and an event that
    // no transform applies to, which may be here only because the store was asked for all that is read as the type of
    // a transform, and a transform applies to events of its own type alone. The store was then not asked to count, so
    // maxCount counts the stored events that anything is returned of. And it may have been asked for metadata that the
    // caller did not want, which is left out again.
    private async IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> TransformAsync(
        IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> events,
        EventFilter? outputFilter = null,
        int? maxCount = null,
        bool includeMetadata = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>? output = null;
        var subject = outputFilter is null ? null : new FilterableReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>();
        var remaining = maxCount;

        if (remaining <= 0)
            yield break;

        await foreach (var @event in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var isSelected = false;

            if (!_readTransforms.TryGetValue(@event.Payload.GetType(), out var transform))
            {
                subject?.Set(@event);

                if (subject is not null && !outputFilter!.Matches(subject))
                    continue;

                isSelected = true;
                yield return includeMetadata ? @event : WithoutMetadata(@event);
            }
            else
            {
                output ??= [];
                output.Clear();
                Expand(@event, transform, output, depth: 0);

                foreach (var derived in output)
                {
                    subject?.Set(derived);

                    if (subject is not null && !outputFilter!.Matches(subject))
                        continue;

                    isSelected = true;
                    yield return includeMetadata ? derived : WithoutMetadata(derived);
                }
            }

            if (isSelected && remaining is not null && --remaining == 0)
                yield break;
        }
    }

    private static ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> WithoutMetadata(
        ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event)
    {
        var context = @event.Context;

        if (context.Metadata.Count == 0)
            return @event;

        return ReadEvent.Create(
            @event.Payload,
            ReadEventContext.Create(
                context.StreamId,
                context.EventName,
                FrozenDictionary<string, string>.Empty,
                context.CreatedAt,
                context.StreamPosition,
                context.LogPosition));
    }

    // With an output filter, every event is tested against it, as in a read. The store takes an event that it
    // delivers for one that the subscriber is given, so of an event that the filter leaves nothing of, it is for this
    // to say how far the subscription has looked. It says so at once, then at most once in an interval, and
    // otherwise before the store next says that it has caught up or fallen behind. An event or a checkpoint of the
    // store says more.
    private async IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>>
        TransformSubscriptionAsync(
            IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> messages,
            EventFilter? outputFilter,
            TimeSpan checkpointInterval,
            Func<ReadEventContext<TStreamId, TStreamPos, TLogPos>,
                SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> toCheckpoint,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>? output = null;
        var subject = outputFilter is null ? null : new FilterableReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>();
        ReadEventContext<TStreamId, TStreamPos, TLogPos>? passedOver = null;
        var nextCheckpointAt = Environment.TickCount64;

        await foreach (var message in messages.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (message.Event is not { } e)
            {
                if (passedOver is { } context && !message.IsCheckpoint)
                    yield return toCheckpoint(context);

                passedOver = null;
                yield return message;
                continue;
            }

            var isSelected = false;

            if (!_readTransforms.TryGetValue(e.Payload.GetType(), out var transform))
            {
                subject?.Set(e);

                if (subject is null || outputFilter!.Matches(subject))
                {
                    isSelected = true;
                    yield return message;
                }
            }
            else
            {
                output ??= [];
                output.Clear();
                Expand(e, transform, output, depth: 0);

                foreach (var derived in output)
                {
                    subject?.Set(derived);

                    if (subject is not null && !outputFilter!.Matches(subject))
                        continue;

                    isSelected = true;
                    yield return SubscriptionMessage.Event(derived);
                }
            }

            passedOver = isSelected ? null : e.Context;

            if (isSelected || Environment.TickCount64 < nextCheckpointAt)
                continue;

            nextCheckpointAt = Environment.TickCount64 + (long)Math.Ceiling(checkpointInterval.TotalMilliseconds);
            passedOver = null;
            yield return toCheckpoint(e.Context);
        }
    }

    /// <summary>
    /// Applies <paramref name="transform"/> to <paramref name="event"/> and then, depth-first, applies any transform
    /// that matches an event it produces to preserve event order. Derived events inherit the source event's context. If
    /// a transform produces no events, the configured ignored event sentinel is emitted in its place so that the source
    /// event's position is still observed. An exception is thrown if no sentinel is configured.
    /// </summary>
    private void Expand(
        ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event,
        IReadEventTransform<TEvent> transform,
        List<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> output,
        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local - incremented in recursive call
        int depth)
    {
        if (depth >= MaxTransformDepth)
        {
            throw new InvalidOperationException(
                $"Read event transforms exceeded a depth of {MaxTransformDepth} starting from " +
                $"'{@event.Payload.GetType().Name}'. Check for a cycle between transforms.");
        }

        var sourceType = @event.Payload.GetType();
        var context = @event.Context;
        var produced = false;

        foreach (var derivedPayload in transform.Apply(@event.Payload, in context))
        {
            produced = true;

            if (derivedPayload.GetType() == sourceType)
            {
                throw new InvalidOperationException(
                    $"The read event transform for '{sourceType.Name}' produced an event of the same type, which " +
                    "would be transformed again indefinitely.");
            }

            var derived = ReadEvent.Create(derivedPayload, context);

            if (_readTransforms.TryGetValue(derivedPayload.GetType(), out var next))
                Expand(derived, next, output, depth + 1);
            else
                output.Add(derived);
        }

        if (produced)
            return;

        if (!IgnoredEventSentinel.HasValue)
        {
            throw new InvalidOperationException(
                $"The read event transform for '{sourceType.Name}' produced no events, " +
                "but no ignored-event sentinel is configured. Call UseIgnoredEventSentinel to configure one.");
        }

        output.Add(ReadEvent.Create(IgnoredEventSentinel.Value, context));
    }
}