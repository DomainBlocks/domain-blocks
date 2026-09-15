using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.SystemTextJson;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.KurrentDB;

/// <summary>
/// The one place to create a KurrentDB event store. Events and metadata are serialized as UTF-8 JSON unless a
/// serializer is given.
/// </summary>
/// <example>
/// <code>
/// await using var store = new KurrentDBEventStoreBuilder&lt;IDomainEvent&gt;()
///     .UseClient(client)
///     .MapEvents(EventTypeMapping.ReadWrite&lt;OrderPlaced&gt;())
///     .Build();
/// </code>
/// </example>
public sealed class KurrentDBEventStoreBuilder<TEvent> where TEvent : notnull
{
    private readonly EventCodecBuilder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> _codec = new();
    private readonly List<IMetadataContributor<TEvent>> _contributors = [];
    private readonly List<IReadEventTransform<TEvent>> _transforms = [];
    private bool _hasDroppedEventPlaceholder;
    private TEvent _droppedEventPlaceholder = default!;
    private KurrentDBClient? _client;
    private string? _connectionString;
    private bool _built;

    /// <summary>
    /// Uses an existing client, which the caller owns. This is the path for a client registered in a container.
    /// </summary>
    public KurrentDBEventStoreBuilder<TEvent> UseClient(KurrentDBClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (_connectionString is not null)
            throw new InvalidOperationException("Call either UseClient or UseConnectionString, not both.");

        _client = client;
        return this;
    }

    /// <summary>
    /// Creates a client from the connection string at <see cref="Build"/> and gives it to the store to own and
    /// dispose. A builder on this path builds one store.
    /// </summary>
    public KurrentDBEventStoreBuilder<TEvent> UseConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (_client is not null)
            throw new InvalidOperationException("Call either UseClient or UseConnectionString, not both.");

        _connectionString = connectionString;
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.MapEvents"/>
    public KurrentDBEventStoreBuilder<TEvent> MapEvents(params EventTypeMapping[] mappings)
    {
        _codec.MapEvents(mappings);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.MapEvent{T}"/>
    public KurrentDBEventStoreBuilder<TEvent> MapEvent<T>(string? name = null) where T : TEvent
    {
        _codec.MapEvent<T>(name);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.UseEventTypeMap"/>
    public KurrentDBEventStoreBuilder<TEvent> UseEventTypeMap(EventTypeMap typeMap)
    {
        _codec.UseEventTypeMap(typeMap);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.AddContractMappers"/>
    public KurrentDBEventStoreBuilder<TEvent> AddContractMappers(params IEventContractMapper<TEvent>[] mappers)
    {
        _codec.AddContractMappers(mappers);
        return this;
    }

    /// <summary>
    /// Replaces the default UTF-8 JSON event serializer.
    /// </summary>
    public KurrentDBEventStoreBuilder<TEvent> UseEventSerializer(IObjectSerializer<ReadOnlyMemory<byte>> serializer)
    {
        _codec.UseEventSerializer(serializer);
        return this;
    }

    /// <summary>
    /// Replaces the default UTF-8 JSON metadata serializer.
    /// </summary>
    public KurrentDBEventStoreBuilder<TEvent> UseMetadataSerializer(
        IMetadataSerializer<ReadOnlyMemory<byte>> serializer)
    {
        _codec.UseMetadataSerializer(serializer);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.UseCodec"/>
    public KurrentDBEventStoreBuilder<TEvent> UseCodec(
        IEventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> codec)
    {
        _codec.UseCodec(codec);
        return this;
    }

    /// <inheritdoc cref="EventStoreExtensions.WithMetadataContributors{TEvent, TStreamId, TStreamPos, TLogPos}"/>
    public KurrentDBEventStoreBuilder<TEvent> AddMetadataContributors(
        params IMetadataContributor<TEvent>[] contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);

        _contributors.AddRange(contributors);
        return this;
    }

    /// <inheritdoc cref="EventStoreExtensions.WithReadTransforms{TEvent, TStreamId, TStreamPos, TLogPos}(IEventStore{TEvent, TStreamId, TStreamPos, TLogPos}, IReadEventTransform{TEvent}[])"/>
    public KurrentDBEventStoreBuilder<TEvent> AddReadTransforms(params IReadEventTransform<TEvent>[] transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);

        _transforms.AddRange(transforms);
        return this;
    }

    /// <summary>
    /// Emits <paramref name="placeholder"/>, with the dropped event's context, in place of any event a read transform
    /// drops. Without one, a transform that returns no events throws.
    /// </summary>
    public KurrentDBEventStoreBuilder<TEvent> UseDroppedEventPlaceholder(TEvent placeholder)
    {
        ArgumentNullException.ThrowIfNull(placeholder);

        _hasDroppedEventPlaceholder = true;
        _droppedEventPlaceholder = placeholder;
        return this;
    }

    /// <summary>
    /// Builds the store. Nothing here touches the server.
    /// </summary>
    public KurrentDBEventStore<TEvent> Build()
    {
        if (_client is null && _connectionString is null)
        {
            throw new InvalidOperationException(
                "No connection is configured. Call UseClient(...) or UseConnectionString(...).");
        }

        if (_built && _connectionString is not null)
        {
            throw new InvalidOperationException(
                "Build has already been called. A builder given a connection string creates a client the store " +
                "owns, so it builds one store; use another builder, or UseClient, for another.");
        }

        var codec = _codec.Build(
            static () => new JsonUtf8BytesObjectSerializer(),
            static () => new JsonUtf8BytesMetadataSerializer());

        KurrentDBClient? ownedClient = null;
        var client = _client ?? (ownedClient = new KurrentDBClient(KurrentDBClientSettings.Create(_connectionString!)));

        _built = true;

        try
        {
            var core = new KurrentDBEventStoreCore<TEvent>(client, codec);

            var inner = core.WithMetadataContributors([.. _contributors]);

            inner = _hasDroppedEventPlaceholder
                ? inner.WithReadTransforms(_transforms, _droppedEventPlaceholder)
                : inner.WithReadTransforms([.. _transforms]);

            return new KurrentDBEventStore<TEvent>(inner, ownedClient);
        }
        catch
        {
            ownedClient?.Dispose();
            throw;
        }
    }
}