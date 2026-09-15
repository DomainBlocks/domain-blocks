using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.PostgreSQL;
using DomainBlocks.EventStore.TypeMapping;
using Npgsql;

namespace DomainBlocks.Testing.Integration.EventStore.PostgreSQL;

/// <summary>
/// Binds a suite to the PostgreSQL event store: one schema and one data source per fixture on the server started by
/// <see cref="PostgresTestEnvironment"/>. The schema is named after the fixture unless <paramref name="configure"/>
/// sets one.
/// </summary>
public sealed class PostgresEventStoreTestHarness(Action<PostgresEventStoreOptions>? configure = null) :
    IEventStoreTestHarness<StreamPosition, LogPosition>
{
    public PostgresEventStoreOptions Options { get; private set; } = null!;

    /// <summary>
    /// The data source the fixture's stores are created over, with the type mappings of the fixture's schema. It
    /// lives from <see cref="InitializeAsync"/> to <see cref="DropAsync"/>.
    /// </summary>
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

    public IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        return CreateEventStore(
            TestPostgresEventCodec.Create(eventTypeMap, eventFormat, contractMappers),
            Options,
            loggerNameSuffix);
    }

    /// <summary>
    /// Creates a store over this harness's schema with an explicit codec and, optionally, different options such as
    /// batch sizes.
    /// </summary>
    public PostgresEventStore<object> CreateEventStore(
        EventCodec<object, PostgresEventData, string> eventCodec,
        PostgresEventStoreOptions? options = null,
        string loggerNameSuffix = "")
    {
        return PostgresEventStore.Create(
            DataSource,
            eventCodec,
            options ?? Options,
            PostgresTestEnvironment.LoggerFactory.CreateLogger($"PostgresEventStore{loggerNameSuffix}"));
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

    private static async Task<string> ShowAsync(string setting)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand($"SHOW {setting}");
        return (await command.ExecuteScalarAsync())?.ToString() ?? "?";
    }
}