using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public abstract class EventStoreBuilder<TEvent, TEventData, TMetadata, TBuilder>
    where TEvent : notnull
    where TEventData : notnull
    where TBuilder : EventStoreBuilder<TEvent, TEventData, TMetadata, TBuilder>
{
    private EventCodecBuilder<TEvent, TEventData, TMetadata>? _codecBuilder;
    private IEventCodec<TEvent, TEventData, TMetadata>? _codec;
    private readonly List<IMetadataContributor<TEvent>> _metadataContributors = [];
    private readonly List<IReadEventTransform<TEvent>> _readTransforms = [];

    private TBuilder Self => (TBuilder)this;

    public TBuilder ConfigureCodec(Action<EventCodecBuilder<TEvent, TEventData, TMetadata>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _codecBuilder ??= new EventCodecBuilder<TEvent, TEventData, TMetadata>();
        _codec = null;
        configure(_codecBuilder);
        return Self;
    }

    public TBuilder UseCodec(IEventCodec<TEvent, TEventData, TMetadata> codec)
    {
        _codecBuilder = null;
        _codec = codec;
        return Self;
    }

    public TBuilder AddMetadataContributors(params IMetadataContributor<TEvent>[] contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);

        _metadataContributors.AddRange(contributors);
        return Self;
    }

    public TBuilder AddReadTransforms(params IReadEventTransform<TEvent>[] transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);

        _readTransforms.AddRange(transforms);
        return Self;
    }

    public TBuilder AddReadTransform<TSourceEvent>(Func<TSourceEvent, IEnumerable<TEvent>> apply)
        where TSourceEvent : TEvent
    {
        return AddReadTransforms(ReadEventTransform.Create(apply));
    }

    public TBuilder AddReadTransform<TSourceEvent>(Func<TSourceEvent, ReadEventInfo, IEnumerable<TEvent>> apply)
        where TSourceEvent : TEvent
    {
        return AddReadTransforms(ReadEventTransform.Create(apply));
    }

    public TBuilder AddReadTransform<TSourceEvent>(Func<TSourceEvent, TEvent> apply) where TSourceEvent : TEvent
    {
        return AddReadTransforms(ReadEventTransform.Create(apply));
    }

    public TBuilder AddReadTransform<TSourceEvent>(Func<TSourceEvent, ReadEventInfo, TEvent> apply)
        where TSourceEvent : TEvent
    {
        return AddReadTransforms(ReadEventTransform.Create(apply));
    }

    protected IEventCodec<TEvent, TEventData, TMetadata> BuildCodec(
        Func<IObjectSerializer<TEventData>> defaultEventSerializer,
        Func<IMetadataSerializer<TMetadata>> defaultMetadataSerializer)
    {
        if (_codec != null)
            return _codec;

        return (_codecBuilder ?? new EventCodecBuilder<TEvent, TEventData, TMetadata>())
            .Build(defaultEventSerializer, defaultMetadataSerializer);
    }

    protected IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> Decorate<TStreamId, TStreamPos, TLogPos>(
        IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> store)
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return store
            .WithMetadataContributors([.. _metadataContributors])
            .WithReadTransforms([.. _readTransforms]);
    }
}