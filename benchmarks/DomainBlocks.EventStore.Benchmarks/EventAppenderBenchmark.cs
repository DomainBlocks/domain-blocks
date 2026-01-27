using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Appender;
using DomainBlocks.EventStore.MongoDB.Client;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Schema = DomainBlocks.EventStore.MongoDB.Appender.Schema;

namespace DomainBlocks.EventStore.Benchmarks;

[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, invocationCount: 1)]
[MemoryDiagnoser]
public class EventAppenderBenchmark
{
    private const string MongoConnectionString = "mongodb://mongo1:27017,mongo2:27018,mongo3:27019/?replicaSet=rs0";
    private const int TotalOps = 1000;

    private IMongoClient _mongoClient = null!;
    private EventAppender _eventAppender = null!;
    private List<Schema.EventDocument> _events = null!;

    [Params(1)]
    public int EventCount { get; set; }

    [Params(8)]
    public int Producers { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _mongoClient = new MongoClient(MongoConnectionString);

        _eventAppender = new EventAppender(
            _mongoClient,
            new OptionsWrapper<EventAppenderOptions>(new EventAppenderOptions()),
            new NullLogger<EventAppender>());

        _eventAppender.Start();

        _events = CreateEvents(EventCount);

        await MongoEventStoreAdmin.EnsureIndexesAsync(_mongoClient, EventStoreCollectionOptions.Default);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await _eventAppender.StopAsync();
        _mongoClient.Dispose();
    }

    [Benchmark(OperationsPerInvoke = TotalOps)]
    public async Task AppendEvents_Concurrent()
    {
        var tasks = new Task[Producers];

        for (var p = 0; p < Producers; p++)
        {
            var producer = p;

            tasks[p] = Task.Run(async () =>
            {
                for (var i = producer + 1; i <= TotalOps; i += Producers)
                {
                    var streamId = $"bench-{i}";
                    await _eventAppender.AppendToStreamAsync(streamId, _events, AppendToStreamOptions.Default);
                }
            });
        }

        await Task.WhenAll(tasks);
    }

    private static List<Schema.EventDocument> CreateEvents(int count)
    {
        var appendEvents = Enumerable
            .Range(0, count)
            .Select(i => new AppendEvent<IDomainEvent>(
                new TestEvent
                {
                    Value1 = $"value1-{i}",
                    Value2 = $"value2-{i}"
                },
                [KeyValuePair.Create("Value1", $"value1-{i}")]));

        var encoderOptions = new EventEncoderOptions<IDomainEvent, byte[], byte[]>
        {
            TypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>()).Appends,
            EventSerializer = new RawBsonObjectSerde(),
            MetadataSerializer = new RawBsonMetadataSerde()
        };

        var encoder = EventEncoder.Create(encoderOptions);
        var documents = new List<Schema.EventDocument>(count);

        foreach (var (eventName, eventData, metadata) in encoder.Encode(appendEvents))
        {
            documents.Add(new Schema.EventDocument
            {
                EventName = eventName,
                EventData = new RawBsonDocument(eventData),
                Metadata = new RawBsonDocument(metadata)
            });
        }

        return documents;
    }

    private interface IDomainEvent;

    private sealed class TestEvent : IDomainEvent
    {
        public required string Value1 { get; init; }
        public required string Value2 { get; init; }
    }
}