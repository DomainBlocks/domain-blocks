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

        var subject = ChangeStreamSubject.Create(
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

        using var loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ChangeStreamSubjectTests>();

        var subject = ChangeStreamSubject.Create(
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