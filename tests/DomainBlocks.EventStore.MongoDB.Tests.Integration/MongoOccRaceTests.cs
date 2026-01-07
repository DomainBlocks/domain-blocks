using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoOccRaceTests
{
    private const string StreamId = "occ-test-stream";

    private MongoClient _mongoClient = null!;
    private IMongoCollection<EventDocument> _collection = null!;
    private MongoEventStoreClient<IDomainEvent, EventDocument> _client = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        var codecOptions = new EventCodecOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = new EventTypeMapBuilder().MapType<TestEvent>().Build(),
            EventSerializer = new BsonDocumentSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        };

        var options = new MongoEventStoreClientOptions<IDomainEvent, EventDocument>
        {
            Collection = new EventCollectionOptions<EventDocument>
            {
                CollectionNamespace = new CollectionNamespace("test-occ", "events"),
                DocumentSchema = EventDocumentSchema.Default
            },
            DocumentCodec = EventDocumentCodec.Create(EventCodec.Create(codecOptions))
        };

        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _collection = _mongoClient.GetCollection<EventDocument>(options.Collection.CollectionNamespace);
        _client = new MongoEventStoreClient<IDomainEvent, EventDocument>(_collection, options);

        await MongoEventStoreAdmin.EnsureIndexesAsync(_collection, options.Collection);
    }

    [Test]
    public async Task AppendToStreamAsync_is_atomic_under_concurrency()
    {
        const int attempts = 100;

        const int slowEventCount = 10;
        const int slowEventSize = 1024 * 1024;

        const int fastEventCount = 1;
        const int fastEventSize = 0;
        const int minFastDelayMillis = 45;
        const int maxFastDelayMillis = 55;

        var slowEvents = CreateTestEvents("slow", slowEventCount, slowEventSize);
        var fastEvents = CreateTestEvents("fast", fastEventCount, fastEventSize);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            await _collection.DeleteManyAsync(x => x.StreamId == StreamId);

            var barrier = new Barrier(2);
            var attempt1 = attempt;

            var slowTask = Task.Run(async () =>
            {
                barrier.SignalAndWait();

                await _client.AppendToStreamAsync2($"Attempt {attempt1}: slow", StreamId, slowEvents);
            });

            var fastTask = Task.Run(async () =>
            {
                barrier.SignalAndWait();

                var delay = Random.Shared.Next(minFastDelayMillis, maxFastDelayMillis);
                Console.WriteLine($"Attempt {attempt1}: slow delay {delay}ms");
                await Task.Delay(delay);

                await _client.AppendToStreamAsync2($"Attempt {attempt1}: fast", StreamId, fastEvents);
            });

            try
            {
                await Task.WhenAll(slowTask, fastTask);
            }
            catch (Exception ex)
            {
                if (slowTask.IsFaulted)
                    Console.WriteLine($"Attempt {attempt}: slow append failed");

                if (fastTask.IsFaulted)
                    Console.WriteLine($"Attempt {attempt}: fast append failed");
            }

            var wrappedReadEvents = await _client.ReadStreamAsync(StreamId).ToArrayAsync();

            // Assert versions are contiguous
            var versions = wrappedReadEvents.Select(x => (int)x.Context.StreamVersion.Value).ToArray();
            for (var i = 0; i < versions.Length; i++)
                versions[i].ShouldBe(i, $"attempt={attempt}");

            var readEvents = wrappedReadEvents.Select(x => x.Event).Cast<TestEvent>().ToArray();

            var readSlowEvents = readEvents.Where(x => x.BatchId == "slow").ToArray();
            if (readSlowEvents.Length > 0)
                readSlowEvents.Length.ShouldBe(slowEvents.Length, $"attempt={attempt}");

            var readFastEvents = readEvents.Where(x => x.BatchId == "fast").ToArray();
            if (readFastEvents.Length > 0)
                readFastEvents.Length.ShouldBe(fastEvents.Length, $"attempt={attempt}");

            Console.WriteLine();
        }
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mongoClient.Dispose();
    }

    private static AppendEvent<IDomainEvent>[] CreateTestEvents(string batchId, int eventCount, int size)
    {
        return Enumerable
            .Range(0, eventCount)
            .Select(i =>
            {
                var buffer = new byte[size];
                Random.Shared.NextBytes(buffer);

                return new TestEvent
                {
                    BatchId = batchId,
                    Data = buffer
                };
            })
            .Select(e => AppendEvent.Create<IDomainEvent>(e))
            .ToArray();
    }

    private interface IDomainEvent;

    public class TestEvent : IDomainEvent
    {
        public required string BatchId { get; init; }
        public required byte[] Data { get; init; }
    }
}