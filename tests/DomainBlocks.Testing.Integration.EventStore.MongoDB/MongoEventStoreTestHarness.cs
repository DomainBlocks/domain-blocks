using DomainBlocks.EventStore;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using MongoDB.Bson;

namespace DomainBlocks.Testing.Integration.EventStore.MongoDB;

/// <summary>
/// Binds a suite to the MongoDB event store: one database per fixture on the replica set started by
/// <see cref="MongoTestEnvironment"/>. The database is named after the fixture unless <paramref name="configure"/>
/// sets one. Stores are built on the borrowed-client path of <see cref="MongoEventStoreBuilder{TEvent}"/>.
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

    /// <summary>
    /// A builder over the shared client, this harness's options and a logger named for the test, for callers that
    /// need to configure more than <see cref="CreateEventStore"/> offers.
    /// </summary>
    public MongoEventStoreBuilder<object> CreateBuilder(string loggerNameSuffix = "")
    {
        return new MongoEventStoreBuilder<object>()
            .UseClient(MongoTestEnvironment.MongoClient)
            .UseOptions(Options)
            .UseLogger(MongoTestEnvironment.LoggerFactory.CreateLogger($"MongoEventStore{loggerNameSuffix}"));
    }

    public IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        var builder = CreateBuilder(loggerNameSuffix)
            .UseEventTypeMap(eventTypeMap)
            .AddContractMappers([.. contractMappers ?? []]);

        // BSON is the builder's default; the other formats adapt a byte serializer onto a BSON binary value.
        if (EventSerializerFor(eventFormat ?? EventFormat.Bson) is { } serializer)
            builder.UseEventSerializer(serializer);

        return builder.Build();
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

    private static IObjectSerializer<BsonValue>? EventSerializerFor(EventFormat format) => format switch
    {
        EventFormat.Bson => null,
        EventFormat.Protobuf => new ProtobufBytesObjectSerializer().AsBsonValueSerializer(),
        EventFormat.Json => new JsonUtf8BytesObjectSerializer().AsBsonValueSerializer(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };
}