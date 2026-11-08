using DomainBlocks.MongoDB.Sequencing;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// Runs the store's appender policy in an appender of its own against an isolated database, with a hold that controls
/// which appends share a batch.
/// </summary>
internal sealed class AppenderPolicyScenario : IAsyncDisposable
{
    private readonly MongoEventStoreOptions _options;
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly HoldingPolicy _policy;
    private readonly MongoSequencedAppender<BsonDocument, AppendContext> _appender;

    private AppenderPolicyScenario(MongoEventStoreOptions options)
    {
        var client = MongoTestEnvironment.MongoClient;
        var database = client.GetDatabase(options.DatabaseName);

        _options = options;
        _eventLog = database.GetCollection<BsonDocument>(options.EventLogCollectionName);
        _policy = new HoldingPolicy(new AppenderPolicy(_eventLog));

        var binding = new MongoSequenceBinding<BsonDocument>(
            new CollectionNamespace(database.DatabaseNamespace, options.SequencesCollectionName),
            MongoEventStore.SequenceId,
            new CollectionNamespace(database.DatabaseNamespace, options.EventLogCollectionName),
            new StringFieldDefinition<BsonDocument, long>(EventLogEntry.FieldNames.Position));

        _appender = new MongoSequencedAppender<BsonDocument, AppendContext>(
            client,
            binding,
            _policy,
            options: null,
            MongoTestEnvironment.LoggerFactory.CreateLogger("AppenderPolicyScenario"));
    }

    public static async Task<AppenderPolicyScenario> StartAsync(CancellationToken ct)
    {
        var options = new MongoEventStoreOptions { DatabaseName = $"pol_{Guid.NewGuid():N}" };
        await MongoEventStoreAdmin.EnsureInitializedAsync(MongoTestEnvironment.MongoClient, options, ct);

        return new AppenderPolicyScenario(options);
    }

    public Task AppendAsync(
        string streamId,
        ExpectedStreamState<StreamPosition> expectedState,
        Guid commitId,
        CancellationToken ct)
    {
        var document = new BsonDocument
        {
            { EventLogEntry.FieldNames.CommitId, new BsonBinaryData(commitId, GuidRepresentation.Standard) },
            { EventLogEntry.FieldNames.StreamId, streamId }
        };

        return _appender.AppendAsync([document], new AppendContext(commitId, streamId, expectedState), null, ct);
    }

    /// <summary>
    /// Makes the same append twice in one batch: both queue up while the policy holds the batch before them.
    /// </summary>
    public async Task<(Task First, Task Repeat)> AppendTwiceInOneBatchAsync(
        string streamId,
        ExpectedStreamState<StreamPosition> expectedState,
        Guid commitId,
        CancellationToken ct)
    {
        _policy.HoldNextBatch();
        var held = AppendAsync("held", ExpectedStreamState.Any<StreamPosition>(), Guid.NewGuid(), ct);
        await _policy.BatchHeld.WaitAsync(ct);

        var first = AppendAsync(streamId, expectedState, commitId, ct);
        var repeat = AppendAsync(streamId, expectedState, commitId, ct);

        _policy.ReleaseBatch();
        await held;

        return (first, repeat);
    }

    public Task<long> CountEventsAsync(string streamId, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);
        return _eventLog.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _appender.DisposeAsync();
        await MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);
    }

    private sealed class HoldingPolicy(IMongoSequencedAppenderPolicy<AppendContext> policy) :
        IMongoSequencedAppenderPolicy<AppendContext>
    {
        private readonly TaskCompletionSource _batchHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _batchReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _isHoldPending;

        public Task BatchHeld => _batchHeld.Task;

        public void HoldNextBatch() => Volatile.Write(ref _isHoldPending, 1);

        public void ReleaseBatch() => _batchReleased.SetResult();

        public async Task OnBatchCommittingAsync(
            IReadOnlyList<AppendRequest<AppendContext>> batch,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _isHoldPending, 0) == 1)
            {
                _batchHeld.SetResult();
                await _batchReleased.Task.WaitAsync(cancellationToken);
            }

            await policy.OnBatchCommittingAsync(batch, cancellationToken);
        }

        public ConflictResolution OnConflict(AppendConflict<AppendContext> conflict) => policy.OnConflict(conflict);
    }
}