using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.PostgreSQL;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.SystemTextJson;
using Npgsql;

namespace DomainBlocks.Testing.Integration.EventStore.PostgreSQL;

public sealed class PostgresEventStoreTestHarness(Action<PostgresEventStoreOptions>? configure = null) :
    IEventStoreTestHarness<StreamPosition, LogPosition>
{
    public PostgresEventStoreOptions Options { get; private set; } = null!;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public StoreCapabilities Capabilities => StoreCapabilities.IdempotentAppends;

    public IReadOnlyList<EventFormat> SupportedFormats { get; } = [EventFormat.Json, EventFormat.Protobuf];

    public async Task InitializeAsync(string name)
    {
        var options = new PostgresEventStoreOptions { Schema = name };
        configure?.Invoke(options);

        DataSource = PostgresTestEnvironment.CreateDataSource(builder => builder.UsePostgresEventStore(options));

        await PostgresEventStoreAdmin.EnsureInitializedAsync(DataSource, options);
        Options = options;
    }

    public async Task ResetAsync()
    {
        await using var command = DataSource.CreateCommand(
            $"TRUNCATE {Options.Schema}.event_log; " +
            $"UPDATE {Options.Schema}.sequences SET next = 0 WHERE name = 'event_log'");

        await command.ExecuteNonQueryAsync();
    }

    public async Task DropAsync()
    {
        await PostgresEventStoreAdmin.DropAsync(DataSource, Options);
        await DataSource.DisposeAsync();
    }

    public PostgresEventStoreBuilder<object> CreateBuilder(string loggerNameSuffix = "")
    {
        return new PostgresEventStoreBuilder<object>()
            .UseDataSource(DataSource)
            .UseOptions(Options)
            .UseLogger(PostgresTestEnvironment.LoggerFactory.CreateLogger($"PostgresEventStore{loggerNameSuffix}"));
    }

    public IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "",
        IEnumerable<string>? ignoredEventNames = null)
    {
        var builder = CreateBuilder(loggerNameSuffix)
            .ConfigureCodec(x => x
                .UseEventTypeMap(eventTypeMap)
                .UseEventSerializer(EventSerializerFor(eventFormat ?? EventFormat.Json))
                .AddContractMappers([.. contractMappers ?? []]));

        if (ignoredEventNames is not null)
            builder.IgnoreEvents([.. ignoredEventNames]).UseIgnoredEventSentinel(IgnoredEvent.Instance);

        return builder.Build();
    }

    public IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        IEventCodec<object, PostgresEventData, string> eventCodec,
        PostgresEventStoreOptions? options = null,
        string loggerNameSuffix = "")
    {
        return CreateBuilder(loggerNameSuffix)
            .UseCodec(eventCodec)
            .UseOptions(options ?? Options)
            .Build();
    }

    public StreamPosition CreateStreamPosition(ulong value) => new(value);

    public LogPosition CreateLogPosition(ulong value) => new(value);

    public Task<string?> DescribeAsync() => DescribeAsync(Options);

    public static async Task<string?> DescribeAsync(PostgresEventStoreOptions options)
    {
        var version = await ShowAsync("server_version");
        var walWriterDelay = await ShowAsync("wal_writer_delay");
        var synchronousCommit = await ShowAsync("synchronous_commit");
        var fsync = await ShowAsync("fsync");
        var sharedBuffers = await ShowAsync("shared_buffers");

        return
            $"PostgresEventStore: schema {options.Schema}, append batch size {options.AppendBatchSize}, " +
            $"append queue capacity {options.AppendQueueCapacity}, batching delay {options.AppendBatchingDelay} " +
            $"(min count {options.AppendBatchingDelayMinCount}); " +
            $"server {version}: wal_writer_delay {walWriterDelay}, synchronous_commit {synchronousCommit}, " +
            $"fsync {fsync}, shared_buffers {sharedBuffers}";
    }

    private static IObjectSerializer<PostgresEventData> EventSerializerFor(EventFormat format) => format switch
    {
        EventFormat.Json => new JsonObjectSerializer().AsPostgresEventDataSerializer(),
        EventFormat.Protobuf => ((IObjectSerializer<byte[]>)new ProtobufBytesObjectSerializer())
            .AsPostgresEventDataSerializer(),
        EventFormat.Bson => throw new NotSupportedException("BSON is not supported by the PostgreSQL event store."),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };

    private static async Task<string> ShowAsync(string setting)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand($"SHOW {setting}");
        return (await command.ExecuteScalarAsync())?.ToString() ?? "?";
    }
}