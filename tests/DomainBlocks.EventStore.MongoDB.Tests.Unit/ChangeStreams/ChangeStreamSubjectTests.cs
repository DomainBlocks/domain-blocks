using System.Net;
using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using DomainBlocks.Testing;
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

    private Mock<IMongoClient> _mockClient = null!;
    private Mock<IMongoCollection<BsonDocument>> _mockCollection = null!;
    private List<ChangeStreamOptions?> _watchOptions = null!;
    private BsonDocument? _initialResumeToken;
    private int _helloCount;

    [SetUp]
    public void SetUp()
    {
        _mockCollection = new Mock<IMongoCollection<BsonDocument>>();
        _watchOptions = [];
        _helloCount = 0;

        // The server reports a resume token in the initial response.
        _initialResumeToken = new BsonDocument("_data", "0");

        // Each hello reports a later last applied optime, as if answered at a later point.
        var mockAdminDb = new Mock<IMongoDatabase>();
        mockAdminDb
            .Setup(x => x.RunCommandAsync(
                It.IsAny<Command<BsonDocument>>(),
                It.IsAny<ReadPreference>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new BsonDocument(
                "lastWrite",
                new BsonDocument("opTime", new BsonDocument("ts", CreateOperationTime(++_helloCount)))));

        _mockClient = new Mock<IMongoClient>();
        _mockClient
            .Setup(x => x.GetDatabase("admin", It.IsAny<MongoDatabaseSettings>()))
            .Returns(mockAdminDb.Object);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_Always_StartsAfterOperationTimeAndKeepsItAcrossResume(CancellationToken ct)
    {
        ChangeStreamBatch[] batches =
        [
            new() { Items = [CreateChange(new BsonDocument("_data", "1"))] },
            new() { Exception = new TimeoutException() },
            new() { Items = [CreateChange(new BsonDocument("_data", "2"))] }
        ];

        SetupChangeStream(batches);

        var subject = ChangeStreamSubject.Create(
            _mockClient.Object,
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken);

        var observer = new TestObserver();
        using var _ = subject.Attach(observer);
        await using var connection = await subject.ConnectAsync(ct);

        await observer.ReadAllAsync(ct).Take(2).ToArrayAsync(ct);

        _helloCount.ShouldBe(1);
        connection.OperationTime.ShouldBe(CreateOperationTime(1));
        // The stream starts one tick after the operation time.
        _watchOptions.Select(x => x?.StartAtOperationTime).ShouldBe([new BsonTimestamp(1, 1), null]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WithExplicitStartPoint_KeepsIt(CancellationToken ct)
    {
        SetupChangeStream([new ChangeStreamBatch { Items = [] }]);
        var resumeAfter = new BsonDocument("_data", "explicit");

        var subject = ChangeStreamSubject.Create(
            _mockClient.Object,
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken,
            new ChangeStreamSubjectOptions { MongoOptions = new ChangeStreamOptions { ResumeAfter = resumeAfter } });

        await using var connection = await subject.ConnectAsync(ct);

        connection.OperationTime.ShouldBe(CreateOperationTime(1));
        _watchOptions.Single()!.ResumeAfter.ShouldBe(resumeAfter);
        _watchOptions.Single()!.StartAtOperationTime.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenCursorFailsBeforeFirstBatch_ResumesFromInitialToken(CancellationToken ct)
    {
        var change = CreateChange(new BsonDocument("_data", "1"));

        SetupChangeStream(
            [new ChangeStreamBatch { Exception = new TimeoutException() }, new ChangeStreamBatch { Items = [change] }]);

        var subject = ChangeStreamSubject.Create(
            _mockClient.Object,
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken);

        var observer = new TestObserver();
        using var _ = subject.Attach(observer);
        await using var connection = await subject.ConnectAsync(ct);

        var receivedItems = await observer.ReadAllAsync(ct).Take(1).ToArrayAsync(ct);

        receivedItems.ShouldBe([change]);
        _watchOptions.Select(x => x?.ResumeAfter).ShouldBe([null, _initialResumeToken]);
    }

    [TestCaseSource(nameof(ResumableExceptions))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenCursorFailsWithResumableError_ContinuesAfterReconnect(
        Exception exception,
        CancellationToken ct)
    {
        // A write landing while the cursor opens fills the first batch, and the driver then reports no token until
        // the batch has been read. The resume must come from the documents rather than fault.
        _initialResumeToken = null;

        ChangeStreamBatch[] batches =
        [
            new() { Items = [CreateChange(new BsonDocument("_data", "1"))] },
            new() { Items = [CreateChange(new BsonDocument("_data", "2"))] },
            new() { Exception = exception },
            new() { Items = [CreateChange(new BsonDocument("_data", "3"))] }
        ];

        SetupChangeStream(batches);

        using var loggerFactory = LoggerFactory.Create(x => x
            .AddProvider(new NUnitLoggerProvider())
            .SetMinimumLevel(LogLevel.Debug));

        var logger = loggerFactory.CreateLogger<ChangeStreamSubjectTests>();

        var subject = ChangeStreamSubject.Create(
            _mockClient.Object,
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken,
            logger: logger);

        var observer = new TestObserver();
        using var _ = subject.Attach(observer);
        await using var connection = await subject.ConnectAsync(ct);

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

        using var loggerFactory = LoggerFactory.Create(x => x
            .AddProvider(new NUnitLoggerProvider())
            .SetMinimumLevel(LogLevel.Debug));

        var logger = loggerFactory.CreateLogger<ChangeStreamSubjectTests>();

        var subject = ChangeStreamSubject.Create(
            _mockClient.Object,
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken,
            new ChangeStreamSubjectOptions
            {
                MongoOptions = new ChangeStreamOptions { ResumeAfter = new BsonDocument("_data", "0") }
            },
            logger);

        await using var connection = await subject.ConnectAsync(ct);

        var completion = connection.Completion.WaitAsync(ct);
        var completionException = await completion.ShouldThrowAsync(exception.GetType());
        completionException.ShouldBe(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenCursorFailsWithUnresumableError_NotifiesAttachedObservers(CancellationToken ct)
    {
        var exception = new InvalidOperationException();
        SetupChangeStream([new ChangeStreamBatch { Exception = exception }]);

        var subject = ChangeStreamSubject.Create(
            _mockClient.Object,
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken);

        var observer1 = new TestObserver();
        var observer2 = new TestObserver();
        using var attachment1 = subject.Attach(observer1);
        using var attachment2 = subject.Attach(observer2);

        await using var connection = await subject.ConnectAsync(ct);
        var completion = connection.Completion.WaitAsync(ct);

        (await completion.ShouldThrowAsync(exception.GetType())).ShouldBeSameAs(exception);
        (await observer1.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
        (await observer2.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Attach_AfterUnresumableError_Throws(CancellationToken ct)
    {
        var exception = new InvalidOperationException();
        SetupChangeStream([new ChangeStreamBatch { Exception = exception }]);

        var subject = ChangeStreamSubject.Create(
            _mockClient.Object,
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken);

        await using var connection = await subject.ConnectAsync(ct);
        var completion = connection.Completion.WaitAsync(ct);

        (await completion.ShouldThrowAsync(exception.GetType())).ShouldBeSameAs(exception);

        var attachException = Should.Throw<InvalidOperationException>(() => subject.Attach(new TestObserver()));
        attachException.InnerException.ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task OnNextAsync_WhenObserverThrows_DetachesObserver(CancellationToken ct)
    {
        var change1 = CreateChange(new BsonDocument("_data", "1"));
        var change2 = CreateChange(new BsonDocument("_data", "2"));

        SetupChangeStream(
        [
            new ChangeStreamBatch { Items = [change1] },
            new ChangeStreamBatch { Items = [change2] }
        ]);

        var subject = ChangeStreamSubject.Create(
            _mockClient.Object,
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken);

        var throwingObserver = new ThrowingObserver();
        var receivingObserver = new TestObserver();
        using var throwingAttachment = subject.Attach(throwingObserver);
        using var receivingAttachment = subject.Attach(receivingObserver);
        await using var connection = await subject.ConnectAsync(ct);

        var receivedItems = await receivingObserver.ReadAllAsync(ct).Take(2).ToArrayAsync(ct);

        receivedItems.ShouldBe([change1, change2]);
        throwingObserver.CallCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Attach_AfterConnectionIsDisposed_Throws(CancellationToken ct)
    {
        SetupChangeStream([new ChangeStreamBatch { Items = [] }]);

        var subject = ChangeStreamSubject.Create(
            _mockClient.Object,
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            x => x.ResumeToken);

        var connection = await subject.ConnectAsync(ct);
        await connection.DisposeAsync();

        var exception = Should.Throw<InvalidOperationException>(() => subject.Attach(new TestObserver()));
        exception.Message.ShouldBe("Cannot attach to a completed change stream connection.");
    }

    private static BsonTimestamp CreateOperationTime(int helloCount) => new(helloCount, 0);

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
        var testCursor = new TestChangeStreamCursor<ChangeStreamDocument<BsonDocument>>(batches, _initialResumeToken);

        _mockCollection
            .Setup(x => x.WatchAsync(
                It.IsAny<PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>>>(),
                It.IsAny<ChangeStreamOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback((
                    PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>> _,
                    ChangeStreamOptions? options,
                    CancellationToken _) =>
                _watchOptions.Add(options))
            .ReturnsAsync(testCursor);
    }

    private class ChangeStreamBatch<TDocument>
    {
        public IEnumerable<TDocument>? Items { get; init; }
        public BsonDocument? ResumeToken { get; init; }
        public Exception? Exception { get; init; }
    }

    private class ChangeStreamBatch : ChangeStreamBatch<ChangeStreamDocument<BsonDocument>>;

    private class TestChangeStreamCursor<TDocument>(
        IEnumerable<ChangeStreamBatch<TDocument>> batches,
        BsonDocument? initialResumeToken) :
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

        // Like the driver, reports the token from the initial response until a batch has been read.
        public BsonDocument GetResumeToken() => (_currentBatch?.ResumeToken ?? initialResumeToken)!;

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

        private readonly TaskCompletionSource<Exception> _errorTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Exception> Error => _errorTcs.Task;

        public ValueTask OnNextAsync(ChangeStreamDocument<BsonDocument> change, CancellationToken cancellationToken)
        {
            _channel.Writer.TryWrite(change);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _errorTcs.TrySetResult(exception);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<ChangeStreamDocument<BsonDocument>> ReadAllAsync(CancellationToken ct) =>
            _channel.Reader.ReadAllAsync(ct);
    }

    private sealed class ThrowingObserver : IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
    {
        public int CallCount { get; private set; }

        public ValueTask OnNextAsync(ChangeStreamDocument<BsonDocument> change, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("The test observer failed.");
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private class TestMongoConnectionException(bool isNetworkException) :
        MongoConnectionException(CreateConnectionId(), null)
    {
        public override bool IsNetworkException => isNetworkException;
    }
}