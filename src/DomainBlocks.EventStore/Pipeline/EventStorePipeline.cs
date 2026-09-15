using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;

namespace DomainBlocks.EventStore.Pipeline;

/// <summary>
/// Decorates an event store with domain-facing stages. The store underneath sees only encoded events; this type
/// contributes metadata on the way in and applies read transforms on the way out, on reads and subscriptions alike.
/// </summary>
internal sealed class EventStorePipeline<TEvent, TStreamId, TStreamPos, TLogPos> :
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
    private readonly FrozenDictionary<Type, IReadEventTransform<TEvent>> _transforms;
    private readonly bool _allowDroppingEvents;

    public EventStorePipeline(
        IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> inner,
        IEnumerable<IMetadataContributor<TEvent>> contributors,
        IEnumerable<IReadEventTransform<TEvent>> transforms,
        bool allowDroppingEvents)
    {
        _inner = inner;
        _contributors = contributors.ToArray();
        _allowDroppingEvents = allowDroppingEvents;

        var transformsByType = new Dictionary<Type, IReadEventTransform<TEvent>>();

        foreach (var transform in transforms)
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
    }

    public Task AppendAsync(
        TStreamId streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<TStreamPos>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (_contributors.Length > 0)
            events = ContributeMetadata(events);

        return _inner.AppendAsync(streamId, events, expectedState, commitId, options, cancellationToken);
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
    /// Merges contributed and explicit metadata for each event into one buffer shared by the batch, so no per-event
    /// allocation is made. Explicit entries win. Lazy, so a store that streams its input keeps streaming.
    /// </summary>
    private IEnumerable<AppendableEvent<TEvent>> ContributeMetadata(IEnumerable<AppendableEvent<TEvent>> events)
    {
        var buffer = new MetadataBuffer();

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
    /// any transform that applies to what it produced. Derived events share the source event's context.
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

        if (!produced && !_allowDroppingEvents)
        {
            throw new InvalidOperationException(
                $"Read event transform '{transform.GetType().Name}' produced no events for " +
                $"'{sourceType.Name}'. Dropping events hides their positions from consumers that track the last " +
                $"observed position; call AllowDroppingEvents() on the pipeline to permit it.");
        }
    }
}