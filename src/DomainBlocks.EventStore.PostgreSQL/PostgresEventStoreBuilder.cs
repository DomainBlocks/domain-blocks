using DomainBlocks.Serialization.SystemTextJson;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

public sealed class PostgresEventStoreBuilder<TEvent> :
    EventStoreBuilder<TEvent, PostgresEventData, string, PostgresEventStoreBuilder<TEvent>>
    where TEvent : notnull
{
    private NpgsqlDataSource? _dataSource;
    private string? _connectionString;
    private Action<NpgsqlDataSourceBuilder>? _configureDataSource;
    private PostgresEventStoreOptions _options = new();
    private PostgresEventStoreAdminOptions _adminOptions = new();
    private ILoggerFactory? _loggerFactory;
    private ILogger? _logger;

    public PostgresEventStoreBuilder<TEvent> UseDataSource(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        _dataSource = dataSource;
        _connectionString = null;
        _configureDataSource = null;
        return this;
    }

    public PostgresEventStoreBuilder<TEvent> UseConnectionString(
        string connectionString,
        Action<NpgsqlDataSourceBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _dataSource = null;
        _connectionString = connectionString;
        _configureDataSource = configure;
        return this;
    }

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

    public PostgresEventStoreBuilder<TEvent> UseAdminOptions(PostgresEventStoreAdminOptions adminOptions)
    {
        ArgumentNullException.ThrowIfNull(adminOptions);

        _adminOptions = adminOptions;
        return this;
    }

    public PostgresEventStoreBuilder<TEvent> ConfigureAdminOptions(Action<PostgresEventStoreAdminOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_adminOptions);
        return this;
    }

    public PostgresEventStoreBuilder<TEvent> UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = null;
        return this;
    }

    public PostgresEventStoreBuilder<TEvent> UseLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _loggerFactory = null;
        _logger = logger;
        return this;
    }

    public IEventStore<TEvent, string, StreamPosition, LogPosition> Build()
    {
        if (_dataSource is null && _connectionString is null)
        {
            throw new InvalidOperationException(
                "No connection is configured. Call UseDataSource or UseConnectionString.");
        }

        var codec = BuildCodec(
            static () => new JsonObjectSerializer().AsPostgresEventDataSerializer(),
            static () => new JsonMetadataSerializer());

        var logger = _logger ?? _loggerFactory?.CreateLogger(typeof(PostgresEventStore).FullName!);

        var (dataSource, isDataSourceOwned) = _dataSource is not null
            ? (_dataSource, false)
            : (CreateDataSource(), true);

        try
        {
            var store = PostgresEventStore.Create(
                dataSource,
                codec,
                _options,
                _adminOptions,
                logger,
                isDataSourceOwned,
                replicationConnectionStringFallback: isDataSourceOwned ? _connectionString : null);

            return Decorate(store);
        }
        catch
        {
            if (isDataSourceOwned)
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