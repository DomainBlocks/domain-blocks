using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Infrastructure.MongoDB.Tests.Integration.ChangeStreams;

public class ChangeStreamSubscriptionTests
{
    private const int TestTimeoutMillis = 30 * 1000;

    private MongoClient _mongoClient = null!;
    private IMongoCollection<BsonDocument> _collection = null!;

    [SetUp]
    public void SetUp()
    {
        var mongoClient = new MongoClient(MongoConnectionStrings.Default);
        var db = mongoClient.GetDatabase("domainblocks_tests");
        var collection = db.GetCollection<BsonDocument>("test_items");

        _mongoClient = mongoClient;
        _collection = collection;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _collection.Database.DropCollectionAsync("test_items");
        _mongoClient.Dispose();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ForEachAsync_WhenResultsAreAvailable_InvokesCallback(CancellationToken ct)
    {
        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .Match(x => x.OperationType == ChangeStreamOperationType.Insert);

        using var loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ChangeStreamSubscriptionTests>();

        Task forEachTask;

        await using (var subscription = _collection.SubscribeToChangeStream(pipeline, logger: logger))
        {
            await subscription.WaitUntilLiveAsync(ct);

            var insertedDocs = Enumerable
                .Range(1, 10)
                .Select(x => new BsonDocument { { "item", x } })
                .ToArray();

            await _collection.InsertManyAsync(insertedDocs, cancellationToken: ct);

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var observedDocs = new List<BsonDocument>();

            forEachTask = subscription.ForEachAsync(
                (doc, _) =>
                {
                    observedDocs.Add(doc.FullDocument);

                    if (observedDocs.Count == insertedDocs.Length)
                        tcs.SetResult();

                    return ValueTask.CompletedTask;
                },
                ct);

            await tcs.Task.WaitAsync(ct);

            observedDocs.ShouldBe(insertedDocs);
        }

        await forEachTask.WaitAsync(ct);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAllAsync_WhenResultsAreAvailable_YieldsResults(CancellationToken ct)
    {
        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .Match(x => x.OperationType == ChangeStreamOperationType.Insert);

        using var loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ChangeStreamSubscriptionTests>();

        await using var subscription = _collection.SubscribeToChangeStream(pipeline, logger: logger);

        await subscription.WaitUntilLiveAsync(ct);

        var insertedDocs = Enumerable
            .Range(1, 10)
            .Select(x => new BsonDocument { { "item", x } })
            .ToArray();

        await _collection.InsertManyAsync(insertedDocs, cancellationToken: ct);

        var observedDocs = await subscription
            .ReadAllAsync(ct)
            .Take(insertedDocs.Length)
            .Select(x => x.FullDocument)
            .ToArrayAsync(cancellationToken: ct);

        observedDocs.ShouldBe(insertedDocs);
    }
}