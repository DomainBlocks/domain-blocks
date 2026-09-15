using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.SystemTextJson;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The one place to create a PostgreSQL event store. Events are serialized as JSON into <c>jsonb</c> unless a
/// serializer is given; metadata as JSON text.
/// </summary>
/// <example>
/// <code>
/// await using var store = new PostgresEventStoreBuilder&lt;IDomainEvent&gt;()
///     .UseConnectionString(connectionString)
///     .ConfigureOptions(o => o.Schema = "orders")
///     .MapEvents(EventTypeMapping.ReadWrite&lt;OrderPlaced&gt;())
///     .UseLoggerFactory(loggerFactory)
///     .Build();
///
/// await store.EnsureInitializedAsync();
/// </code>
/// </example>
public sealed class PostgresEventStoreBuilder<TEvent> where TEvent : notnull
{
    private readonly EventCodecBuilder<TEvent, PostgresEventData, string> _codec = new();
    private readonly List<IMetadataContributor<TEvent>> _contributors = [];
    private readonly List<IReadEventTransform<TEvent>> _transforms = [];
    private bool _hasDroppedEventPlaceholder;
    private TEvent _droppedEventPlaceholder = default!;
    private NpgsqlDataSource? _dataSource;
    private string? _connectionString;
    private Action<NpgsqlDataSourceBuilder>? _configureDataSource;
    private PostgresEventStoreOptions _options = new();
    private ILoggerFactory? _loggerFactory;
    private ILogger? _logger;
    private bool _built;

    /// <summary>
    /// Uses an existing data source, which the caller owns and must have built with
    /// <see cref="NpgsqlDataSourceBuilderExtensions.UsePostgresEventStore"/> for the store's schema. This is the path
    /// for a data source registered in a container.
    /// </summary>
    public PostgresEventStoreBuilder<TEvent> UseDataSource(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        if (_connectionString is not null)
            throw new InvalidOperationException("Call either UseDataSource or UseConnectionString, not both.");

        _dataSource = dataSource;
        return this;
    }

    /// <summary>
    /// Creates a data source from the connection string at <see cref="Build"/>, with the store's type mappings
    /// applied, and gives it to the store to own and dispose. The replication connection uses the same connection
    /// string unless <see cref="PostgresReplicationOptions.ConnectionString"/> is set. A builder on this path builds
    /// one store.
    /// </summary>
    public PostgresEventStoreBuilder<TEvent> UseConnectionString(
        string connectionString,
        Action<NpgsqlDataSourceBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (_dataSource is not null)
            throw new InvalidOperationException("Call either UseDataSource or UseConnectionString, not both.");

        _connectionString = connectionString;
        _configureDataSource = configure;
        return this;
    }

    /// <summary>
    /// Replaces the options object. Use <see cref="ConfigureOptions"/> to adjust the current one.
    /// </summary>
    public PostgresEventStoreBuilder<TEvent> UseOptions(PostgresEventStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        return this;
    }

    public PostgresEventStoreBuilder<TEvent> ConfigureOptions(Action<PostgresEventStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_options);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.MapEvents"/>
    public PostgresEventStoreBuilder<TEvent> MapEvents(params EventTypeMapping[] mappings)
    {
        _codec.MapEvents(mappings);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.MapEvent{T}"/>
    public PostgresEventStoreBuilder<TEvent> MapEvent<T>(string? name = null) where T : TEvent
    {
        _codec.MapEvent<T>(name);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.UseEventTypeMap"/>
    public PostgresEventStoreBuilder<TEvent> UseEventTypeMap(EventTypeMap typeMap)
    {
        _codec.UseEventTypeMap(typeMap);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.AddContractMappers"/>
    public PostgresEventStoreBuilder<TEvent> AddContractMappers(params IEventContractMapper<TEvent>[] mappers)
    {
        _codec.AddContractMappers(mappers);
        return this;
    }

    /// <summary>
    /// Replaces the default JSON event serializer. Adapt a string or byte serializer with
    /// <see cref="ObjectSerializerExtensions.AsPostgresEventDataSerializer(IObjectSerializer{string})"/> and its
    /// overloads.
    /// </summary>
    public PostgresEventStoreBuilder<TEvent> UseEventSerializer(IObjectSerializer<PostgresEventData> serializer)
    {
        _codec.UseEventSerializer(serializer);
        return this;
    }

    /// <summary>
    /// Replaces the default JSON metadata serializer.
    /// </summary>
    public PostgresEventStoreBuilder<TEvent> UseMetadataSerializer(IMetadataSerializer<string> serializer)
    {
        _codec.UseMetadataSerializer(serializer);
        return this;
    }

    /// <inheritdoc cref="EventCodecBuilder{TEvent, TEventData, TMetadata}.UseCodec"/>
    public PostgresEventStoreBuilder<TEvent> UseCodec(IEventCodec<TEvent, PostgresEventData, string> codec)
    {
        _codec.UseCodec(codec);
        return this;
    }

    /// <inheritdoc cref="EventStoreExtensions.WithMetadataContributors{TEvent, TStreamId, TStreamPos, TLogPos}"/>
    public PostgresEventStoreBuilder<TEvent> AddMetadataContributors(params IMetadataContributor<TEvent>[] contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);

        _contributors.AddRange(contributors);
        return this;
    }

    /// <inheritdoc cref="EventStoreExtensions.WithReadTransforms{TEvent, TStreamId, TStreamPos, TLogPos}(IEventStore{TEvent, TStreamId, TStreamPos, TLogPos}, IReadEventTransform{TEvent}[])"/>
    public PostgresEventStoreBuilder<TEvent> AddReadTransforms(params IReadEventTransform<TEvent>[] transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);

        _transforms.AddRange(transforms);
        return this;
    }

    /// <summary>
    /// Emits <paramref name="placeholder"/>, with the dropped event's context, in place of any event a read transform
    /// drops. Without one, a transform that returns no events throws.
    /// </summary>
    public PostgresEventStoreBuilder<TEvent> UseDroppedEventPlaceholder(TEvent placeholder)
    {
        ArgumentNullException.ThrowIfNull(placeholder);

        _hasDroppedEventPlaceholder = true;
        _droppedEventPlaceholder = placeholder;
        return this;
    }

    /// <summary>
    /// Creates the store's logger from the factory under the store's category. <see cref="UseLogger"/> wins.
    /// </summary>
    public PostgresEventStoreBuilder<TEvent> UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        return this;
    }

    public PostgresEventStoreBuilder<TEvent> UseLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        return this;
    }

    /// <summary>
    /// Builds the store. Nothing here touches the database; call
    /// <see cref="PostgresEventStore{TEvent}.EnsureInitializedAsync"/> on the result to create the schema.
    /// </summary>
    public PostgresEventStore<TEvent> Build()
    {
        if (_dataSource is null && _connectionString is null)
        {
            throw new InvalidOperationException(
                "No connection is configured. Call UseDataSource(...) or UseConnectionString(...).");
        }

        if (_built && _connectionString is not null)
        {
            throw new InvalidOperationException(
                "Build has already been called. A builder given a connection string creates a data source the store " +
                "owns, so it builds one store; use another builder, or UseDataSource, for another.");
        }

        var codec = _codec.Build(
            static () => new JsonObjectSerializer().AsPostgresEventDataSerializer(),
            static () => new JsonMetadataSerializer());

        var logger = _logger ?? _loggerFactory?.CreateLogger(typeof(PostgresEventStore).FullName!);

        var (dataSource, ownsDataSource) = _dataSource is not null
            ? (_dataSource, false)
            : (CreateDataSource(), true);

        _built = true;

        try
        {
            var core = PostgresEventStoreCore.Create(
                dataSource,
                codec,
                _options,
                logger,
                replicationConnectionStringFallback: ownsDataSource ? _connectionString : null);

            var inner = core.WithMetadataContributors([.. _contributors]);

            inner = _hasDroppedEventPlaceholder
                ? inner.WithReadTransforms(_transforms, _droppedEventPlaceholder)
                : inner.WithReadTransforms([.. _transforms]);

            return new PostgresEventStore<TEvent>(inner, dataSource, ownsDataSource, _options);
        }
        catch
        {
            if (ownsDataSource)
                dataSource.Dispose();

            throw;
        }
    }

    private NpgsqlDataSource CreateDataSource()
    {
        var builder = new NpgsqlDataSourceBuilder(_connectionString);
        _configureDataSource?.Invoke(builder);
        builder.UsePostgresEventStore(_options);
        return builder.Build();
    }
}