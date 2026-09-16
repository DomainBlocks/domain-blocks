using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.MongoDB.Bson;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// The one place to create a MongoDB event store. Events and metadata are stored as BSON documents unless a
/// serializer is given. The server must be a replica set, which change streams and transactions require.
/// </summary>
/// <example>
/// <code>
/// await using var store = new MongoEventStoreBuilder&lt;IDomainEvent&gt;()
///     .UseClient(mongoClient)
///     .ConfigureOptions(o => o.DatabaseName = "orders")
///     .MapEvents(EventTypeMapping.ReadWrite&lt;OrderPlaced&gt;())
///     .UseLoggerFactory(loggerFactory)
///     .Build();
///
/// await store.EnsureInitializedAsync();
/// </code>
/// </example>
public sealed class MongoEventStoreBuilder<TEvent> where TEvent : notnull
{
    private readonly EventCodecBuilder<TEvent, BsonValue, BsonValue> _codec = new();
    private readonly List<IMetadataContributor<TEvent>> _contributors = [];
    private readonly List<IReadEventTransform<TEvent>> _transforms = [];
    private bool _hasDroppedEventPlaceholder;
    private TEvent _droppedEventPlaceholder = default!;
    private IMongoClient? _client;
    private string? _connectionString;
    private MongoEventStoreOptions _options = new();
    private ILoggerFactory? _loggerFactory;
    private ILogger? _logger;
    private bool _built;

    /// <summary>
    /// Uses an existing client, which the caller owns. This is the path for a client registered in a container, as
    /// MongoDB recommends.
    /// </summary>
    public MongoEventStoreBuilder<TEvent> UseClient(IMongoClient client)
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
    public MongoEventStoreBuilder<TEvent> UseConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (_client is not null)
            throw new InvalidOperationException("Call either UseClient or UseConnectionString, not both.");

        _connectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Replaces the options object. Use <see cref="ConfigureOptions"/> to adjust the current one.
    /// </summary>
    public MongoEventStoreBuilder<TEvent> UseOptions(MongoEventStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        return this;
    }

    public MongoEventStoreBuilder<TEvent> ConfigureOptions(Action<MongoEventStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_options);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.MapEvents"/>
    public MongoEventStoreBuilder<TEvent> MapEvents(params EventTypeMapping[] mappings)
    {
        _codec.MapEvents(mappings);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.MapEvent{T}"/>
    public MongoEventStoreBuilder<TEvent> MapEvent<T>(string? name = null) where T : TEvent
    {
        _codec.MapEvent<T>(name);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.UseEventTypeMap"/>
    public MongoEventStoreBuilder<TEvent> UseEventTypeMap(EventTypeMap typeMap)
    {
        _codec.UseEventTypeMap(typeMap);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.AddContractMappers"/>
    public MongoEventStoreBuilder<TEvent> AddContractMappers(params IEventContractMapper<TEvent>[] mappers)
    {
        _codec.AddContractMappers(mappers);
        return this;
    }

    /// <summary>
    /// Replaces the default BSON document event serializer. Adapt a string or byte serializer with
    /// <see cref="ObjectSerializerExtensions.AsBsonValueSerializer(IObjectSerializer{string})"/> and its overload.
    /// </summary>
    public MongoEventStoreBuilder<TEvent> UseEventSerializer(IObjectSerializer<BsonValue> serializer)
    {
        _codec.UseEventSerializer(serializer);
        return this;
    }

    /// <summary>
    /// Replaces the default BSON document metadata serializer.
    /// </summary>
    public MongoEventStoreBuilder<TEvent> UseMetadataSerializer(IMetadataSerializer<BsonValue> serializer)
    {
        _codec.UseMetadataSerializer(serializer);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.UseCodec"/>
    public MongoEventStoreBuilder<TEvent> UseCodec(IEventCodec<TEvent, BsonValue, BsonValue> codec)
    {
        _codec.UseCodec(codec);
        return this;
    }

    /// <inheritdoc cref="EventStoreExtensions.WithMetadataContributors{TEvent, TStreamId, TStreamPos, TLogPos}"/>
    public MongoEventStoreBuilder<TEvent> AddMetadataContributors(params IMetadataContributor<TEvent>[] contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);

        _contributors.AddRange(contributors);
        return this;
    }

    /// <inheritdoc cref="EventStoreExtensions.WithReadTransforms{TEvent, TStreamId, TStreamPos, TLogPos}(IEventStore{TEvent, TStreamId, TStreamPos, TLogPos}, IReadEventTransform{TEvent}[])"/>
    public MongoEventStoreBuilder<TEvent> AddReadTransforms(params IReadEventTransform<TEvent>[] transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);

        _transforms.AddRange(transforms);
        return this;
    }

    /// <summary>
    /// Emits <paramref name="placeholder"/>, with the dropped event's context, in place of any event a read transform
    /// drops. Without one, a transform that returns no events throws.
    /// </summary>
    public MongoEventStoreBuilder<TEvent> UseDroppedEventPlaceholder(TEvent placeholder)
    {
        ArgumentNullException.ThrowIfNull(placeholder);

        _hasDroppedEventPlaceholder = true;
        _droppedEventPlaceholder = placeholder;
        return this;
    }

    /// <summary>
    /// Creates the store's logger from the factory under the store's category. <see cref="UseLogger"/> wins.
    /// </summary>
    public MongoEventStoreBuilder<TEvent> UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        return this;
    }

    public MongoEventStoreBuilder<TEvent> UseLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        return this;
    }

    /// <summary>
    /// Builds the store, with the configured metadata contributors and read transforms applied. Nothing here touches
    /// the database; call <see cref="IEventStore{TEvent, TStreamId, TStreamPos, TLogPos}.EnsureInitializedAsync"/>
    /// on the result to create the indexes.
    /// </summary>
    public IEventStore<TEvent, string, StreamPosition, LogPosition> Build()
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
            static () => new BsonDocumentObjectSerializer(),
            static () => new BsonDocumentMetadataSerializer());

        var logger = _logger ?? _loggerFactory?.CreateLogger(typeof(MongoEventStore).FullName!);

        MongoClient? ownedClient = null;
        var client = _client ?? (ownedClient = new MongoClient(_connectionString));

        _built = true;

        try
        {
            var store = MongoEventStore.Create(client, codec, _options, logger, ownedClient);

            var decorated = store.WithMetadataContributors([.. _contributors]);

            return _hasDroppedEventPlaceholder
                ? decorated.WithReadTransforms(_transforms, _droppedEventPlaceholder)
                : decorated.WithReadTransforms([.. _transforms]);
        }
        catch
        {
            ownedClient?.Dispose();
            throw;
        }
    }
}