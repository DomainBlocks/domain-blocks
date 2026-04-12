using System.Net;
using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
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

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit.ChangeStreams;

public class ChangeStreamSubjectTests
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
    public async Task Connect_WhenSubjectCreatedWithoutResumeOption_ResumesFromAnchorToken(CancellationToken ct)
    {
        var anchorToken = new BsonDocument("_data", "1");

        var batch = new ChangeStreamBatch
        {
            Items = [], // First batch can be empty
            ResumeToken = anchorToken
        };

        // Capture the ChangeStreamOptions from the second WatchAsync call (the one made by the
        // producer when Connect() is called), so we can assert the anchor token is used.
        var connectCallTcs = new TaskCompletionSource<ChangeStreamOptions>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var callCount = 0;
        var testCursor = new TestChangeStreamCursor<ChangeStreamDocument<BsonDocument>>([batch]);

        _mockCollection
            .Setup(x => x.WatchAsync(
                It.IsAny<PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>>>(),
                It.IsAny<ChangeStreamOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<
                PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>>,
                ChangeStreamOptions,
                CancellationToken>((_, options, _) =>
            {
                // Call #1 is the anchoring call inside CreateSubjectAsync.
                // Call #2 is the producer's first call after Connect().
                if (++callCount == 2)
                    connectCallTcs.TrySetResult(options);
            })
            .ReturnsAsync(testCursor);

        var subject = await ChangeStreamSubjectFactory.CreateAsync(
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken,
            cancellationToken: ct);

        await using var connection = subject.Connect();

        var capturedOptions = await connectCallTcs.Task.WaitAsync(ct);
        capturedOptions.ResumeAfter.ShouldBe(anchorToken);
    }

    [TestCaseSource(nameof(ResumableExceptions))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenCursorFailsWithResumableError_ContinuesAfterReconnect(
        Exception exception,
        CancellationToken ct)
    {
        ChangeStreamBatch[] batches =
        [
            new() { Items = [CreateChange(new BsonDocument("_data", "1"))] },
            new() { Items = [CreateChange(new BsonDocument("_data", "2"))] },
            new() { Exception = exception },
            new() { Items = [CreateChange(new BsonDocument("_data", "3"))] }
        ];

        SetupChangeStream(batches);

        using var loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ChangeStreamSubjectTests>();

        // Pre-seed a ResumeAfter so CreateSubjectAsync skips its internal anchoring call,
        // ensuring the test batches are consumed exclusively by the producer.
        var subject = await ChangeStreamSubjectFactory.CreateAsync(
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken,
            new ChangeStreamSubjectOptions
            {
                MongoOptions = new ChangeStreamOptions { ResumeAfter = new BsonDocument("_data", "0") }
            },
            logger,
            ct);

        var observer = new TestObserver();
        using var _ = subject.Attach(observer);
        await using var connection = subject.Connect();

        var expectedCount = batches.Count(x => x.Exception is null);

        var receivedItems = await observer
            .ReadAllAsync(ct)
            .Take(expectedCount)
            .ToArrayAsync(ct);

        receivedItems.ShouldBe(batches.SelectMany(x => x.Items ?? []));
    }

    [TestCaseSource(nameof(UnresumableExceptions))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenCursorFailsWithUnresumableError_FaultsCompletion(
        Exception exception,
        CancellationToken ct)
    {
        ChangeStreamBatch[] batches =
        [
            new() { Items = [CreateChange(new BsonDocument("_data", "1"))] },
            new() { Items = [CreateChange(new BsonDocument("_data", "2"))] },
            new() { Exception = exception },
            new() { Items = [CreateChange(new BsonDocument("_data", "3"))] }
        ];

        SetupChangeStream(batches);

        using var loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ChangeStreamSubjectTests>();

        // Pre-seed a ResumeAfter so CreateSubjectAsync skips its internal anchoring call.
        var subject = await ChangeStreamSubjectFactory.CreateAsync(
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken,
            new ChangeStreamSubjectOptions
            {
                MongoOptions = new ChangeStreamOptions { ResumeAfter = new BsonDocument("_data", "0") }
            },
            logger,
            ct);

        await using var connection = subject.Connect();

        var thrownException = await connection.Completion.ShouldThrowAsync(exception.GetType());
        thrownException.ShouldBe(exception);
    }

    private static MongoException CreateResumableMongoException()
    {
        var exception = new MongoException("Resumable error");
        exception.AddErrorLabel(MongoErrorLabels.ResumableChangeStreamError);
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

    // Collects items pushed by the subject's producer via OnNextAsync, and exposes them
    // as an async enumerable for assertions.
    private class TestObserver : IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
    {
        private readonly Channel<ChangeStreamDocument<BsonDocument>> _channel =
            Channel.CreateUnbounded<ChangeStreamDocument<BsonDocument>>();

        public ValueTask OnNextAsync(ChangeStreamDocument<BsonDocument> change, CancellationToken cancellationToken)
        {
            _channel.Writer.TryWrite(change);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<ChangeStreamDocument<BsonDocument>> ReadAllAsync(CancellationToken ct) =>
            _channel.Reader.ReadAllAsync(ct);
    }

    private class TestMongoConnectionException(bool isNetworkException) :
        MongoConnectionException(CreateConnectionId(), null)
    {
        public override bool IsNetworkException => isNetworkException;
    }
}