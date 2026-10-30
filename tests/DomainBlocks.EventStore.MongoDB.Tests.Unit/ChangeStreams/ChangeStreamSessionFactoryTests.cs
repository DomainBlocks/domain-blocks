using System.Net;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
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

public class ChangeStreamSessionFactoryTests
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
    private Exception? _helloError;
    private Exception? _resumeError;

    [SetUp]
    public void SetUp()
    {
        _mockCollection = new Mock<IMongoCollection<BsonDocument>>();
        _watchOptions = [];
        _helloCount = 0;
        _helloError = null;
        _resumeError = null;

        // The server reports a resume token in the initial response.
        _initialResumeToken = new BsonDocument("_data", "0");

        // Each hello reports a later last applied optime, as if answered at a later point, unless a test has set an
        // error for the next one to throw.
        var mockAdminDb = new Mock<IMongoDatabase>();

        mockAdminDb
            .Setup(x => x.RunCommandAsync(
                It.IsAny<Command<BsonDocument>>(),
                It.IsAny<ReadPreference>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Exchange(ref _helloError, null) is { } error
                ? throw error
                : new BsonDocument(
                    "lastWrite",
                    new BsonDocument("opTime", new BsonDocument("ts", CreateOperationTime(++_helloCount)))));

        _mockClient = new Mock<IMongoClient>();

        _mockClient
            .Setup(x => x.GetDatabase("admin", It.IsAny<MongoDatabaseSettings>()))
            .Returns(mockAdminDb.Object);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task CreateAsync_NoResumeToken_StartsAfterLastWrite(CancellationToken ct)
    {
        SetupChangeStream([]);
        var sessionFactory = CreateSessionFactory();

        await using var session = await sessionFactory.CreateAsync(ct);

        session.IsResumed.ShouldBeFalse();
        session.InitialMark.ShouldBe(CreateOperationTime(1));

        // The stream starts one tick after the last write.
        _watchOptions.ShouldHaveSingleItem().ShouldNotBeNull().StartAtOperationTime.ShouldBe(new BsonTimestamp(1, 1));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task CreateAsync_ChangeDelivered_ResumesAfterChange(CancellationToken ct)
    {
        var deliveredChange = CreateChange(new BsonDocument("_data", "1"));
        var pendingChange = CreateChange(new BsonDocument("_data", "2"));
        SetupChangeStream([new ChangeStreamBatch { Items = [deliveredChange, pendingChange] }]);
        var sessionFactory = CreateSessionFactory();

        await using (var session = await sessionFactory.CreateAsync(ct))
        {
            await using var changes = session.ReadAsync(ct).GetAsyncEnumerator(ct);

            // The feed asks for the next change once observers have the last one.
            (await changes.MoveNextAsync()).ShouldBeTrue();
            (await changes.MoveNextAsync()).ShouldBeTrue();
        }

        await using var resumedSession = await sessionFactory.CreateAsync(ct);

        resumedSession.IsResumed.ShouldBeTrue();
        resumedSession.InitialMark.ShouldBeNull();
        _helloCount.ShouldBe(1);
        _watchOptions.Last().ShouldNotBeNull().ResumeAfter.ShouldBe(deliveredChange.ResumeToken);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task CreateAsync_BatchDelivered_ResumesAfterPostBatchToken(CancellationToken ct)
    {
        var postBatchResumeToken = new BsonDocument("_data", "post-batch");

        SetupChangeStream(
        [
            new ChangeStreamBatch
            {
                Items = [CreateChange(new BsonDocument("_data", "1"))],
                ResumeToken = postBatchResumeToken
            },
            new ChangeStreamBatch { Exception = new TimeoutException() }
        ]);

        var sessionFactory = CreateSessionFactory();

        await using (var session = await sessionFactory.CreateAsync(ct))
            await session.ReadAsync(ct).ToArrayAsync(ct).AsTask().ShouldThrowAsync<TimeoutException>();

        await using var resumedSession = await sessionFactory.CreateAsync(ct);

        _watchOptions.Last().ShouldNotBeNull().ResumeAfter.ShouldBe(postBatchResumeToken);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task CreateAsync_CursorFailedBeforeFirstBatch_ResumesAfterInitialToken(CancellationToken ct)
    {
        SetupChangeStream([new ChangeStreamBatch { Exception = new TimeoutException() }]);
        var sessionFactory = CreateSessionFactory();

        await using (var session = await sessionFactory.CreateAsync(ct))
            await session.ReadAsync(ct).ToArrayAsync(ct).AsTask().ShouldThrowAsync<TimeoutException>();

        await using var resumedSession = await sessionFactory.CreateAsync(ct);

        _watchOptions.Select(x => x?.ResumeAfter).ShouldBe([null, _initialResumeToken]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task CreateAsync_NoInitialToken_ResumesAfterLastChange(CancellationToken ct)
    {
        // A write landing while the cursor opens fills the first batch, and the driver then reports no token until the
        // batch has been read. The resume must come from the changes.
        _initialResumeToken = null;
        var lastChange = CreateChange(new BsonDocument("_data", "2"));

        SetupChangeStream(
        [
            new ChangeStreamBatch { Items = [CreateChange(new BsonDocument("_data", "1"))] },
            new ChangeStreamBatch { Items = [lastChange] },
            new ChangeStreamBatch { Exception = new TimeoutException() }
        ]);

        var sessionFactory = CreateSessionFactory();

        await using (var session = await sessionFactory.CreateAsync(ct))
            await session.ReadAsync(ct).ToArrayAsync(ct).AsTask().ShouldThrowAsync<TimeoutException>();

        await using var resumedSession = await sessionFactory.CreateAsync(ct);

        _watchOptions.Last().ShouldNotBeNull().ResumeAfter.ShouldBe(lastChange.ResumeToken);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task CreateAsync_ResumeTokenLost_StartsNewChangeStream(CancellationToken ct)
    {
        SetupChangeStream([new ChangeStreamBatch { Exception = new TimeoutException() }]);
        var sessionFactory = CreateSessionFactory();

        await using (var session = await sessionFactory.CreateAsync(ct))
            await session.ReadAsync(ct).ToArrayAsync(ct).AsTask().ShouldThrowAsync<TimeoutException>();

        // The oplog no longer holds the change that the resume token names.
        _resumeError = CreateCommandException(MongoErrorCodes.ChangeStreamHistoryLost);

        await using var newSession = await sessionFactory.CreateAsync(ct);

        newSession.IsResumed.ShouldBeFalse();
        newSession.InitialMark.ShouldBe(CreateOperationTime(2));
        _watchOptions.Select(x => x?.ResumeAfter is not null).ShouldBe([false, true, false]);
        _watchOptions.Last().ShouldNotBeNull().StartAtOperationTime.ShouldBe(new BsonTimestamp(2, 1));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_Change_ReportsClusterTime(CancellationToken ct)
    {
        var clusterTime = new BsonTimestamp(5, 1);
        var change = CreateChange(new BsonDocument("_data", "1"), clusterTime);
        SetupChangeStream([new ChangeStreamBatch { Items = [change] }]);
        var sessionFactory = CreateSessionFactory();

        await using var session = await sessionFactory.CreateAsync(ct);
        await using var changes = session.ReadAsync(ct).GetAsyncEnumerator(ct);

        (await changes.MoveNextAsync()).ShouldBeTrue();
        changes.Current.Mark.ShouldBe(clusterTime);
    }

    [TestCaseSource(nameof(ResumableExceptions))]
    public void IsTransient_ResumableError_ReturnsTrue(Exception exception)
    {
        CreateSessionFactory().IsTransient(exception).ShouldBeTrue();
    }

    [TestCaseSource(nameof(UnresumableExceptions))]
    public void IsTransient_UnresumableError_ReturnsFalse(Exception exception)
    {
        CreateSessionFactory().IsTransient(exception).ShouldBeFalse();
    }

    [Test]
    public void IsTransient_HistoryLost_ReturnsTrue()
    {
        var exception = CreateCommandException(MongoErrorCodes.ChangeStreamHistoryLost);

        CreateSessionFactory().IsTransient(exception).ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_HelloFailsTransiently_RetriesThenConnects(CancellationToken ct)
    {
        SetupChangeStream([]);
        _helloError = new TimeoutException();

        var feed = new EventLogFeed<ChangeStreamDocument<BsonDocument>, BsonTimestamp>(
            CreateSessionFactory(),
            new EventLogFeedOptions { RetryDelay = TimeSpan.FromMilliseconds(1) });

        await using var connection = await feed.ConnectAsync(ct);

        connection.HighWaterMark.ShouldBe(CreateOperationTime(1));
        _watchOptions.ShouldHaveSingleItem().ShouldNotBeNull().StartAtOperationTime.ShouldBe(new BsonTimestamp(1, 1));
    }

    private static BsonTimestamp CreateOperationTime(int helloCount) => new(helloCount, 0);

    private static MongoException CreateResumableMongoException()
    {
        var exception = new MongoException("Resumable error");
        exception.AddErrorLabel(MongoErrorLabels.ResumableChangeStreamError);
        return exception;
    }

    private static MongoCommandException CreateCommandException(int code)
    {
        return new MongoCommandException(
            CreateConnectionId(),
            "The command failed.",
            new BsonDocument("aggregate", 1),
            new BsonDocument { { "ok", 0 }, { "code", code } });
    }

    private static ConnectionId CreateConnectionId()
    {
        var clusterId = new ClusterId();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 27017);
        var serverId = new ServerId(clusterId, endpoint);
        return new ConnectionId(serverId);
    }

    // Changes are committed after the first hello's operation time unless the test says otherwise.
    private static ChangeStreamDocument<BsonDocument> CreateChange(BsonDocument id, BsonTimestamp? clusterTime = null)
    {
        return new ChangeStreamDocument<BsonDocument>(
            new BsonDocument { { "_id", id }, { "clusterTime", clusterTime ?? new BsonTimestamp(2, 0) } },
            BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>());
    }

    private IFeedSessionFactory<ChangeStreamDocument<BsonDocument>, BsonTimestamp> CreateSessionFactory()
    {
        return ChangeStreamSessionFactory.Create(
            _mockClient.Object,
            _mockCollection.Object.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>(),
            static x => x.ResumeToken,
            static x => x.ClusterTime,
            static x => x);
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
            .Returns((
                    PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>> _,
                    ChangeStreamOptions? options,
                    CancellationToken _) =>
                options?.ResumeAfter is not null && Interlocked.Exchange(ref _resumeError, null) is { } error
                    ? Task.FromException<IChangeStreamCursor<ChangeStreamDocument<BsonDocument>>>(error)
                    : Task.FromResult<IChangeStreamCursor<ChangeStreamDocument<BsonDocument>>>(testCursor));
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

    private class TestMongoConnectionException(bool isNetworkException) :
        MongoConnectionException(CreateConnectionId(), null)
    {
        public override bool IsNetworkException => isNetworkException;
    }
}