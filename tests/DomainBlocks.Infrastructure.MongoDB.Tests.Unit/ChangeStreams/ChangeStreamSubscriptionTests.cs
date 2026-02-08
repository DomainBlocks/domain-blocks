using System.Net;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Errors;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Infrastructure.MongoDB.Tests.Unit.ChangeStreams;

public class ChangeStreamSubscriptionTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    private static readonly Exception[] ResumableExceptions =
    [
        new TestMongoConnectionException(isNetworkException: true),
        new MongoConnectionPoolPausedException(null),
        new MongoCursorNotFoundException(CreateConnectionId(), 0, []),
        new TimeoutException(),
        CreateResumableMongoException()
    ];

    private static readonly Exception[] UnresumableExceptions =
    [
        new TestMongoConnectionException(isNetworkException: false),
        new InvalidOperationException()
    ];

    private Mock<IMongoCollection<BsonDocument>> _mockCollection = null!;

    [SetUp]
    public void SetUp()
    {
        _mockCollection = new Mock<IMongoCollection<BsonDocument>>();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitUntilLiveAsync_WhenFirstBatchIsObserved_CompletesSuccessfully(CancellationToken ct)
    {
        var batch = new ChangeStreamBatch
        {
            Items = [], // First batch can be empty
            ResumeToken = new BsonDocument("_data", "1")
        };

        SetupChangeStream([batch]);

        await using var subscription = _mockCollection.Object.SubscribeToChangeStream();

        await subscription.WaitUntilLiveAsync(ct);
    }

    [TestCaseSource(nameof(ResumableExceptions))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AsAsyncEnumerable_WhenCursorFailsWithResumableError_ContinuesAfterReconnect(
        Exception exception,
        CancellationToken ct)
    {
        ChangeStreamBatch[] batch =
        [
            new() { Items = [CreateChange(new BsonDocument("_data", "1"))] },
            new() { Items = [CreateChange(new BsonDocument("_data", "2"))] },
            new() { Exception = exception },
            new() { Items = [CreateChange(new BsonDocument("_data", "3"))] }
        ];

        SetupChangeStream(batch);

        using var loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ChangeStreamSubscriptionTests>();

        await using var subscription = _mockCollection.Object.SubscribeToChangeStream(logger: logger);

        var expectedCount = batch.Count(x => x.Exception is null);

        var receivedItems = await subscription
            .ReadAllAsync(ct)
            .Take(expectedCount)
            .ToArrayAsync(ct);

        receivedItems.ShouldBe(batch.SelectMany(x => x.Items ?? []));
    }

    [TestCaseSource(nameof(UnresumableExceptions))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AsAsyncEnumerable_WhenCursorFailsWithUnresumableError_ThrowsException(
        Exception exception,
        CancellationToken ct)
    {
        ChangeStreamBatch[] batch =
        [
            new() { Items = [CreateChange(new BsonDocument("_data", "1"))] },
            new() { Items = [CreateChange(new BsonDocument("_data", "2"))] },
            new() { Exception = exception },
            new() { Items = [CreateChange(new BsonDocument("_data", "3"))] }
        ];

        SetupChangeStream(batch);

        using var loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ChangeStreamSubscriptionTests>();

        await using var subscription = _mockCollection.Object.SubscribeToChangeStream(logger: logger);

        var thrownException = await subscription
            .ReadAllAsync(ct)
            .ToArrayAsync(ct)
            .AsTask()
            .ShouldThrowAsync(exception.GetType());

        thrownException.ShouldBe(exception);
    }

    private static MongoException CreateResumableMongoException()
    {
        var exception = new MongoException("Resumable error");
        exception.AddErrorLabel(ErrorLabels.ResumableChangeStreamError);
        return exception;
    }

    private static ConnectionId CreateConnectionId()
    {
        var clusterId = new ClusterId();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 27017);
        var serverId = new ServerId(clusterId, endpoint);
        return new ConnectionId(serverId);
    }

    private static ChangeStreamDocument<BsonDocument> CreateChange(BsonDocument id)
    {
        return new ChangeStreamDocument<BsonDocument>(
            // The ID is the resume token for change stream documents.
            new BsonDocument("_id", id),
            BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>());
    }

    private void SetupChangeStream(IEnumerable<ChangeStreamBatch<ChangeStreamDocument<BsonDocument>>> batches)
    {
        var testCursor = new TestChangeStreamCursor<ChangeStreamDocument<BsonDocument>>(batches);

        _mockCollection
            .Setup(x => x.WatchAsync(
                It.IsAny<PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>>>(),
                It.IsAny<ChangeStreamOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCursor);
    }

    private class ChangeStreamBatch<TDocument>
    {
        public IEnumerable<TDocument>? Items { get; init; }
        public BsonDocument? ResumeToken { get; init; }
        public Exception? Exception { get; init; }
    }

    private class ChangeStreamBatch : ChangeStreamBatch<ChangeStreamDocument<BsonDocument>>;

    private class TestChangeStreamCursor<TDocument>(IEnumerable<ChangeStreamBatch<TDocument>> batches) :
        IChangeStreamCursor<TDocument>
    {
        private readonly Queue<ChangeStreamBatch<TDocument>> _batches = new(batches);
        private ChangeStreamBatch<TDocument>? _currentBatch;

        public IEnumerable<TDocument> Current => _currentBatch!.Items!;

        public bool MoveNext(CancellationToken cancellationToken = default)
        {
            return MoveNextAsync(cancellationToken).GetAwaiter().GetResult();
        }

        public async Task<bool> MoveNextAsync(CancellationToken cancellationToken = default)
        {
            // When there are no more batches, block indefinitely to simulate a live, blocking change-stream cursor.
            if (_batches.Count == 0)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            _currentBatch = _batches.Dequeue();

            return _currentBatch switch
            {
                { Items: not null } => true,
                { Exception: not null } => throw _currentBatch.Exception,
                _ => false
            };
        }

        public BsonDocument GetResumeToken() => _currentBatch!.ResumeToken!;

        public void Dispose()
        {
        }
    }

    private class TestMongoConnectionException(bool isNetworkException) :
        MongoConnectionException(CreateConnectionId(), null)
    {
        public override bool IsNetworkException => isNetworkException;
    }
}