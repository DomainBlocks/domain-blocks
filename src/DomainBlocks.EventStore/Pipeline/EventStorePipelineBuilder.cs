using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;

namespace DomainBlocks.EventStore.Pipeline;

/// <summary>
/// Configures the domain-facing stages composed around an event store: metadata contribution on append and read
/// transforms on reads and subscriptions. See <see cref="EventStorePipelineExtensions"/>.
/// </summary>
public sealed class EventStorePipelineBuilder<TEvent> where TEvent : notnull
{
    private readonly List<IMetadataContributor<TEvent>> _contributors = [];
    private readonly List<IReadEventTransform<TEvent>> _transforms = [];
    private bool _allowDroppingEvents;

    /// <summary>
    /// Adds contributors that run, in order, for every appended event. Entries supplied explicitly on an
    /// <see cref="AppendableEvent{TPayload}"/> override contributed entries with the same key.
    /// </summary>
    public EventStorePipelineBuilder<TEvent> ContributeMetadata(
        params IEnumerable<IMetadataContributor<TEvent>> contributors)
    {
        _contributors.AddRange(contributors);
        return this;
    }

    /// <summary>
    /// Adds read transforms. At most one transform may be registered per source event type.
    /// </summary>
    public EventStorePipelineBuilder<TEvent> Transform(params IEnumerable<IReadEventTransform<TEvent>> transforms)
    {
        _transforms.AddRange(transforms);
        return this;
    }

    /// <summary>
    /// Permits a transform to return no events, dropping the source event. Off by default: a dropped event hides its
    /// stream position from consumers that track the last observed position, such as an event-sourced state store,
    /// so "ignore this event" normally belongs in the consumer rather than in a transform.
    /// </summary>
    public EventStorePipelineBuilder<TEvent> AllowDroppingEvents()
    {
        _allowDroppingEvents = true;
        return this;
    }

    internal IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> Build<TStreamId, TStreamPos, TLogPos>(
        IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> inner)
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        if (_contributors.Count == 0 && _transforms.Count == 0)
            return inner;

        return new EventStorePipeline<TEvent, TStreamId, TStreamPos, TLogPos>(
            inner,
            _contributors,
            _transforms,
            _allowDroppingEvents);
    }
}