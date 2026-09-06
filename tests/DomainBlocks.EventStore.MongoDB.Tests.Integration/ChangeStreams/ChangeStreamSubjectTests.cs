using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.ChangeStreams;

public class ChangeStreamSubjectTests
{
    private const int TestTimeoutMillis = 30 * 1000;

    private MongoClient _mongoClient = null!;
    private IMongoCollection<BsonDocument> _collection = null!;

    [SetUp]
    public void SetUp()
    {
        var mongoClient = new MongoClient(TestMongoConnectionStrings.Default);
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
    public async Task Connect_WhenResultsAreAvailable_NotifiesAttachedObservers(CancellationToken ct)
    {
        const int insertCount = 10;

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .Match(x => x.OperationType == ChangeStreamOperationType.Insert);

        using var loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ChangeStreamSubjectTests>();

        var subject = ChangeStreamSubject.Create(_collection.WatchAsync, pipeline, x => x.ResumeToken, logger: logger);

        var observer1 = new TestChangeStreamObserver(expectedCount: insertCount);
        var observer2 = new TestChangeStreamObserver(expectedCount: insertCount);
        using var attachment1 = subject.Attach(observer1);
        using var attachment2 = subject.Attach(observer2);
        await using var connection = await subject.ConnectAsync(ct);

        var insertedDocs = Enumerable
            .Range(1, insertCount)
            .Select(x => new BsonDocument { { "item", x } })
            .ToArray();

        await _collection.InsertManyAsync(insertedDocs, cancellationToken: ct);

        await Task.WhenAll(observer1.Completion, observer2.Completion).WaitAsync(ct);

        observer1.ObservedDocuments.ShouldBe(insertedDocs);
        observer2.ObservedDocuments.ShouldBe(insertedDocs);
    }

    private sealed class TestChangeStreamObserver(int expectedCount) :
        IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<BsonDocument> ObservedDocuments { get; } = [];

        public Task Completion => _tcs.Task;

        public ValueTask OnNextAsync(
            ChangeStreamDocument<BsonDocument> change,
            CancellationToken cancellationToken = default)
        {
            ObservedDocuments.Add(change.FullDocument);

            if (ObservedDocuments.Count == expectedCount)
                _tcs.SetResult();

            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}