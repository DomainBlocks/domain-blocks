using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using DomainBlocks.Core;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;

namespace DomainBlocks.EventStore;

internal sealed class EventStoreDecorator<TEvent, TStreamId, TStreamPos, TLogPos> :
    IEventStore<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    // Guards against transform cycles (A -> B -> A) that the same-type check cannot see.
    private const int MaxTransformDepth = 32;

    private readonly IMetadataContributor<TEvent>[] _metadataContributors;
    private readonly FrozenDictionary<Type, IReadEventTransform<TEvent>> _readTransforms;

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
        var events = Inner.ReadAll(direction, origin, options);
        return _readTransforms.Count == 0 ? events : TransformAsync(events);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadStream(
        TStreamId streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TStreamPos>? origin = null,
        ReadStreamOptions? options = null)
    {
        var events = Inner.ReadStream(streamId, direction, origin, options);
        return _readTransforms.Count == 0 ? events : TransformAsync(events);
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToAll(
        SubscriptionOrigin<TLogPos>? origin = null,
        SubscriptionOptions? options = null)
    {
        var messages = Inner.SubscribeToAll(origin, options);
        return _readTransforms.Count == 0 ? messages : TransformSubscriptionAsync(messages);
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToStream(
        TStreamId streamId,
        SubscriptionOrigin<TStreamPos>? origin = null,
        SubscriptionOptions? options = null)
    {
        var messages = Inner.SubscribeToStream(streamId, origin, options);
        return _readTransforms.Count == 0 ? messages : TransformSubscriptionAsync(messages);
    }

    public ValueTask DisposeAsync() => Inner.DisposeAsync();

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

    private async IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> TransformAsync(
        IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>? output = null;

        await foreach (var @event in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!_readTransforms.TryGetValue(@event.Payload.GetType(), out var transform))
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

    private async IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>>
        TransformSubscriptionAsync(
            IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>? output = null;

        await foreach (var message in messages.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (message.Event is not { } e || !_readTransforms.TryGetValue(e.Payload.GetType(), out var transform))
            {
                yield return message;
                continue;
            }

            output ??= [];
            output.Clear();
            Expand(e, transform, output, depth: 0);

            foreach (var derived in output)
                yield return SubscriptionMessage.Event(derived);
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