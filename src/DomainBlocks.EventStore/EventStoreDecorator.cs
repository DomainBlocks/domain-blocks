using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;

namespace DomainBlocks.EventStore;

/// <summary>
/// Decorates an event store with the domain-facing hooks configured through <see cref="EventStoreExtensions"/>:
/// metadata contributors on append and read transforms on reads and subscriptions. The store underneath sees only
/// encoded events. One instance carries the whole configuration; adding a hook to a decorated store rebuilds it
/// rather than stacking another layer.
/// </summary>
internal sealed class EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos> :
    IEventStore<TEvent, TStreamId, TStreamPos, TLogPos>,
    IAsyncDisposable
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    // Guards against transform cycles (A -> B -> A) that the same-type check cannot see.
    private const int MaxTransformDepth = 32;

    private readonly IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> _inner;
    private readonly IMetadataContributor<TEvent>[] _contributors;
    private readonly IReadEventTransform<TEvent>[] _transformList;
    private readonly FrozenDictionary<Type, IReadEventTransform<TEvent>> _transforms;
    private readonly bool _hasDroppedEventPlaceholder;
    private readonly TEvent _droppedEventPlaceholder;

    public EventStoreDecorator(
        IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> inner,
        IEnumerable<IMetadataContributor<TEvent>> contributors,
        IEnumerable<IReadEventTransform<TEvent>> transforms,
        bool hasDroppedEventPlaceholder,
        TEvent droppedEventPlaceholder)
    {
        _inner = inner;
        _contributors = contributors.ToArray();
        _transformList = transforms.ToArray();
        _hasDroppedEventPlaceholder = hasDroppedEventPlaceholder;
        _droppedEventPlaceholder = droppedEventPlaceholder;

        var transformsByType = new Dictionary<Type, IReadEventTransform<TEvent>>();

        foreach (var transform in _transformList)
        {
            if (!transformsByType.TryAdd(transform.SourceEventType, transform))
            {
                throw new ArgumentException(
                    $"More than one read event transform is registered for source type " +
                    $"'{transform.SourceEventType.Name}'.",
                    nameof(transforms));
            }
        }

        _transforms = transformsByType.ToFrozenDictionary();

        if (hasDroppedEventPlaceholder && _transforms.ContainsKey(droppedEventPlaceholder.GetType()))
        {
            throw new ArgumentException(
                $"The dropped event placeholder type '{droppedEventPlaceholder.GetType().Name}' must not have a " +
                "read event transform registered.",
                nameof(droppedEventPlaceholder));
        }
    }

    public IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> Inner => _inner;

    public IReadOnlyList<IMetadataContributor<TEvent>> Contributors => _contributors;

    public IReadOnlyList<IReadEventTransform<TEvent>> Transforms => _transformList;

    public bool HasDroppedEventPlaceholder => _hasDroppedEventPlaceholder;

    public TEvent DroppedEventPlaceholder => _droppedEventPlaceholder;

    public Task AppendAsync(
        TStreamId streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<TStreamPos>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        return _contributors.Length == 0
            ? _inner.AppendAsync(streamId, events, expectedState, commitId, options, cancellationToken)
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
        // The store has consumed every event by the time its append completes, so the buffer can go back to the
        // pool here. The events handed to the store are valid only for the duration of the append.
        using var buffer = new MetadataBuffer();

        await _inner
            .AppendAsync(streamId, ContributeMetadata(events, buffer), expectedState, commitId, options, cancellationToken)
            .ConfigureAwait(false);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TLogPos>? origin = null,
        ReadAllOptions? options = null)
    {
        var events = _inner.ReadAll(direction, origin, options);
        return _transforms.Count == 0 ? events : TransformAsync(events);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadStream(
        TStreamId streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TStreamPos>? origin = null,
        ReadStreamOptions? options = null)
    {
        var events = _inner.ReadStream(streamId, direction, origin, options);
        return _transforms.Count == 0 ? events : TransformAsync(events);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionOrigin<TLogPos>? origin = null,
        SubscriptionOptions? options = null)
    {
        var messages = _inner.SubscribeToAll(origin, options);
        return _transforms.Count == 0 ? messages : TransformSubscriptionAsync(messages);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        TStreamId streamId,
        SubscriptionOrigin<TStreamPos>? origin = null,
        SubscriptionOptions? options = null)
    {
        var messages = _inner.SubscribeToStream(streamId, origin, options);
        return _transforms.Count == 0 ? messages : TransformSubscriptionAsync(messages);
    }

    public async ValueTask DisposeAsync()
    {
        if (_inner is IAsyncDisposable disposable)
            await disposable.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Merges contributed and explicit metadata for each event into pooled chunks shared by the batch, so no
    /// per-event allocation is made. Explicit entries win. Lazy, so a store that streams its input keeps streaming.
    /// </summary>
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

        foreach (var contributor in _contributors)
            contributor.Contribute(@event.Payload, writer);

        foreach (var (key, value) in @event.Metadata)
            buffer.Set(key, value);

        return buffer.EndEvent();
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> TransformAsync(
        IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>? output = null;

        await foreach (var @event in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!_transforms.TryGetValue(@event.Payload.GetType(), out var transform))
            {
                yield return @event;
                continue;
            }

            output ??= [];
            output.Clear();
            Expand(@event, transform, output, depth: 0);

            foreach (var derived in output)
                yield return derived;
        }
    }

    private async IAsyncEnumerable<SubscriptionMessage> TransformSubscriptionAsync(
        IAsyncEnumerable<SubscriptionMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>? output = null;

        await foreach (var message in messages.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // Anything that is not an event, and any event with no transform, passes through by reference.
            if (message is not SubscriptionMessage.Event<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> e ||
                !_transforms.TryGetValue(e.Value.Payload.GetType(), out var transform))
            {
                yield return message;
                continue;
            }

            output ??= [];
            output.Clear();
            Expand(e.Value, transform, output, depth: 0);

            foreach (var derived in output)
                yield return SubscriptionMessage.Event.Create(derived);
        }
    }

    /// <summary>
    /// Applies <paramref name="transform"/> to <paramref name="event"/> and, depth first so that order is preserved,
    /// any transform that applies to what it produced. Derived events share the source event's context. A transform
    /// that produces nothing yields the dropped event placeholder in the source event's place, so the position is
    /// still observed, or throws if no placeholder is configured.
    /// </summary>
    private void Expand(
        ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event,
        IReadEventTransform<TEvent> transform,
        List<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> output,
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
                    $"Read event transform '{transform.GetType().Name}' produced an event of its own source type " +
                    $"'{sourceType.Name}', which would be transformed again indefinitely.");
            }

            var derived = ReadEvent.Create(derivedPayload, context);

            if (_transforms.TryGetValue(derivedPayload.GetType(), out var next))
                Expand(derived, next, output, depth + 1);
            else
                output.Add(derived);
        }

        if (produced)
            return;

        if (!_hasDroppedEventPlaceholder)
        {
            throw new InvalidOperationException(
                $"Read event transform '{transform.GetType().Name}' produced no events for '{sourceType.Name}'. " +
                "Dropping an event would hide its position from consumers that track the last observed position; " +
                "pass a droppedEventPlaceholder to WithReadTransforms to have it emitted in the event's place.");
        }

        output.Add(ReadEvent.Create(_droppedEventPlaceholder, context));
    }
}