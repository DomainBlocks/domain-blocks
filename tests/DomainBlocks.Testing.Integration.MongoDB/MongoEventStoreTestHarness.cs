using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.TypeMapping;
using MongoDB.Bson;

namespace DomainBlocks.Testing.Integration.MongoDB;

/// <summary>
/// Binds a suite to the MongoDB event store: one database per fixture on the replica set started by
/// <see cref="MongoTestEnvironment"/>. The database is named after the fixture unless <paramref name="configure"/>
/// sets one.
/// </summary>
public sealed class MongoEventStoreTestHarness(Action<MongoEventStoreOptions>? configure = null) :
    IEventStoreTestHarness<StreamPosition, LogPosition>
{
    public MongoEventStoreOptions Options { get; private set; } = null!;

    public StoreCapabilities Capabilities => StoreCapabilities.IdempotentAppends;

    public IReadOnlyList<EventFormat> SupportedFormats { get; } =
        [EventFormat.Bson, EventFormat.Json, EventFormat.Protobuf];

    public async Task InitializeAsync(string name)
    {
        var options = new MongoEventStoreOptions { DatabaseName = name };
        configure?.Invoke(options);

        await MongoEventStoreAdmin.EnsureInitializedAsync(MongoTestEnvironment.MongoClient, options);
        Options = options;
    }

    public async Task ResetAsync()
    {
        await MongoTestEnvironment.MongoClient.DropDatabaseAsync(Options.DatabaseName);
        await MongoEventStoreAdmin.EnsureInitializedAsync(MongoTestEnvironment.MongoClient, Options);
    }

    public Task DropAsync() => MongoTestEnvironment.MongoClient.DropDatabaseAsync(Options.DatabaseName);

    public IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        var eventCodec = TestMongoEventCodec.Create(eventTypeMap, eventFormat, contractMappers);

        return MongoEventStore.Create(
            MongoTestEnvironment.MongoClient,
            eventCodec,
            Options,
            MongoTestEnvironment.LoggerFactory.CreateLogger($"MongoEventStore{loggerNameSuffix}"));
    }

    public StreamPosition CreateStreamPosition(ulong value) => new(value);

    public LogPosition CreateLogPosition(ulong value) => new(value);

    public async Task<string?> DescribeAsync()
    {
        var buildInfo = await MongoTestEnvironment.MongoClient
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1));

        return
            $"MongoEventStore: database {Options.DatabaseName}, append batch size {Options.AppendBatchSize}, " +
            $"append queue capacity {Options.AppendQueueCapacity}; server {buildInfo["version"]}";
    }
}