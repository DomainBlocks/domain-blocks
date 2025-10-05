using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.KurrentDB;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.NATS;
using KurrentDB.Client;
using MongoDB.Driver;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DomainBlocks.EventStore.Benchmarks;

[SimpleJob(RunStrategy.Monitoring, iterationCount: 100)]
[MemoryDiagnoser]
public class EventStoreBenchmarks
{
    private const int EventCount = 1_000;
    private static readonly Random Random = new();
    private static int _nextSequence;

    private NatsClient _natsClient = null!;
    private UncommittedEvent<byte[]>[] _events = null!;
    private UncommittedEvent<ReadOnlyMemory<byte>>[] _eventsWithReadOnlyMemoryPayload = null!;
    private NatsEventStore _natsEventStore = null!;
    private MongoEventStore<DefaultEventDocument<byte[]>, byte[]> _mongoEventStore = null!;
    private KurrentDbEventStore _kurrentDbEventStore = null!;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _events = [.. Enumerable.Range(1, EventCount).Select(i => CreateEvent($"event{i}"))];

        _eventsWithReadOnlyMemoryPayload =
            [.. _events.Select(x => new UncommittedEvent<ReadOnlyMemory<byte>>(x.Header, x.Payload))];

        await SetupNatsEventStore();
        await SetupMongoEventStore();
        SetupKurrentDbEventStore();
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

    //[Benchmark]
    public async Task AppendToStream_NatsProtoBatchMessage()
    {
        await _natsEventStore.AppendToStreamAsync($"stream{GetNextSequence()}", _events);
    }

    [Benchmark]
    public async Task AppendToStream_Mongo()
    {
        await _mongoEventStore.AppendToStreamAsync($"stream{GetNextSequence()}", _events);
    }

    [Benchmark]
    public async Task AppendToStream_KurrentDb()
    {
        await _kurrentDbEventStore.AppendToStreamAsync($"stream{GetNextSequence()}", _eventsWithReadOnlyMemoryPayload);
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
            AllowAtomicPublish = true
        };

        await jsContext.CreateStreamAsync(streamConfig);

        _natsEventStore = new NatsEventStore(_natsClient);
    }

    private async Task SetupMongoEventStore()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var options = MongoEventStoreOptions.CreateDefault<byte[]>();
        await MongoEventStoreAdmin.EnsureIndexesAsync(database, options);

        _mongoEventStore = MongoEventStore.Create(database, options);
    }

    private void SetupKurrentDbEventStore()
    {
        var settings = KurrentDBClientSettings.Create(
            "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false");

        var client = new KurrentDBClient(settings);

        _kurrentDbEventStore = new KurrentDbEventStore(client);
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