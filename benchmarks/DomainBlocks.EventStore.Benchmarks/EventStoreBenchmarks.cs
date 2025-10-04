using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.NATS;
using MongoDB.Driver;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DomainBlocks.EventStore.Benchmarks;

[SimpleJob(RunStrategy.Monitoring, iterationCount: 20)]
[MemoryDiagnoser]
public class EventStoreBenchmarks
{
    private const int EventCount = 1000;
    private static readonly Random Random = new();
    private static int _nextSequence;

    private NatsClient _natsClient = null!;
    private UncommittedEvent<byte[]>[] _events = null!;
    private NatsEventStore _natsEventStore = null!;
    private MongoEventStore<DefaultEventDocument<byte[]>, byte[]> _mongoEventStore = null!;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _events = [.. Enumerable.Range(1, EventCount).Select(i => CreateEvent($"event{i}"))];
        await SetupNatsEventStore();
        await SetupMongoEventStore();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await _natsClient.DisposeAsync();
    }

    [Benchmark]
    public async Task AppendToStream_NatsAtomicBatch()
    {
        await _natsEventStore.AppendToStreamWithAtomicBatchAsync($"stream{GetNextSequence()}", _events);
    }

    [Benchmark]
    public async Task AppendToStream_NatsProtoBatchMessage()
    {
        await _natsEventStore.AppendToStreamAsync($"stream{GetNextSequence()}", _events);
    }

    [Benchmark]
    public async Task AppendToStream_Mongo()
    {
        await _mongoEventStore.AppendToStreamAsync($"stream{GetNextSequence()}", _events);
    }

    private async Task SetupNatsEventStore()
    {
        _natsClient = new NatsClient();
        var jsContext = _natsClient.CreateJetStreamContext();
        await DeleteNatsStreamIfExits(jsContext, "EVENTS");

        var streamConfig = new StreamConfig
        {
            Name = "EVENTS",
            Subjects = ["events.*"],
            Storage = StreamConfigStorage.File,
            //AllowAtomicPublish = true
        };

        await jsContext.CreateStreamAsync(streamConfig);

        _natsEventStore = new NatsEventStore(_natsClient);
    }

    public async Task SetupMongoEventStore()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var options = MongoEventStoreOptions.CreateDefault<byte[]>();
        await MongoEventStoreAdmin.EnsureIndexesAsync(database, options);

        _mongoEventStore =
            (MongoEventStore<DefaultEventDocument<byte[]>, byte[]>)MongoEventStore.Create(database, options);
    }

    private static async Task DeleteNatsStreamIfExits(INatsJSContext jsContext, string stream)
    {
        try
        {
            await jsContext.DeleteStreamAsync(stream);
        }
        catch (NatsJSApiException ex) when (ex.Message == "stream not found")
        {
            // ignore
        }
    }

    private static UncommittedEvent<byte[]> CreateEvent(string name)
    {
        var length = Random.Next(256, 1025);
        var payload = new byte[length];
        RandomNumberGenerator.Fill(payload);
        return new UncommittedEvent<byte[]>(new UncommittedEventHeader(name), payload);
    }

    private static int GetNextSequence() => Interlocked.Increment(ref _nextSequence);
}