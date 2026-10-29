using DomainBlocks.EventStore.Internal;
using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal static class ChangeStreamSubject
{
    public static ChangeStreamSubject<TDocument, TChange, TResult> Create<TDocument, TChange, TResult>(
        IMongoClient mongoClient,
        ChangeStreamCursorFactory<TDocument, TChange> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TChange> pipeline,
        Func<TChange, BsonDocument> resumeTokenSelector,
        Func<TChange, BsonTimestamp> clusterTimeSelector,
        Func<TChange, TResult> resultSelector,
        ChangeStreamSubjectOptions? options = null,
        ILogger? logger = null)
    {
        return new ChangeStreamSubject<TDocument, TChange, TResult>(
            mongoClient,
            cursorFactory,
            pipeline,
            resumeTokenSelector,
            clusterTimeSelector,
            resultSelector,
            options,
            logger);
    }
}

internal sealed class ChangeStreamSubject<TDocument, TChange, TResult> : IFeed<TResult, BsonTimestamp>
{
    private readonly IMongoClient _mongoClient;
    private readonly ChangeStreamCursorFactory<TDocument, TChange> _cursorFactory;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly PipelineDefinition<ChangeStreamDocument<TDocument>, TChange> _pipeline;
    private readonly Func<TChange, BsonDocument> _resumeTokenSelector;
    private readonly Func<TChange, BsonTimestamp> _clusterTimeSelector;
    private readonly Func<TChange, TResult> _resultSelector;
    private readonly ILogger? _logger;
    private readonly FeedObserverHub<TResult> _observerHub;
    private readonly string _subjectId = CorrelationId.ReserveGenerated();
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _connected;

    public ChangeStreamSubject(
        IMongoClient mongoClient,
        ChangeStreamCursorFactory<TDocument, TChange> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TChange> pipeline,
        Func<TChange, BsonDocument> resumeTokenSelector,
        Func<TChange, BsonTimestamp> clusterTimeSelector,
        Func<TChange, TResult> resultSelector,
        ChangeStreamSubjectOptions? options = null,
        ILogger? logger = null)
    {
        options ??= ChangeStreamSubjectOptions.Default;

        _resiliencePipeline = CreateResiliencePipeline(
            options.MaxRetryAttempts,
            options.MaxRetryDelay,
            logger,
            _subjectId);

        _mongoClient = mongoClient;
        _cursorFactory = cursorFactory;
        _pipeline = pipeline;
        _resumeTokenSelector = resumeTokenSelector;
        _clusterTimeSelector = clusterTimeSelector;
        _resultSelector = resultSelector;
        _logger = logger;
        _observerHub = new FeedObserverHub<TResult>(logger, _subjectId);
    }

    public IDisposable Attach(IFeedObserver<TResult> observer, string correlationId = "unknown") =>
        _observerHub.Attach(observer, correlationId);

    public async Task<IFeedConnection<BsonTimestamp>> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var connection = Interlocked.Exchange(ref _connected, 1) == 0
            ? new Connection(this)
            : throw new InvalidOperationException("Connect may only be called once.");

        Task task;

        try
        {
            task = await Task
                .WhenAny(_connectedTcs.Task, connection.Completion)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Nobody will own this connection, so stop the producer rather than leak it.
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (task == _connectedTcs.Task)
            return connection;

        await connection.DisposeAsync().ConfigureAwait(false);
        await connection.Completion.ConfigureAwait(false);
        throw new InvalidOperationException("The change stream connection completed before it connected.");
    }

    private static ResiliencePipeline CreateResiliencePipeline(
        int maxRetryAttempts,
        TimeSpan maxRetryDelay,
        ILogger? logger,
        string changeStreamId)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = maxRetryDelay,
                ShouldHandle = args =>
                {
                    var shouldHandle = args.Outcome.Exception is { } ex && ChangeStreamResumePolicy.CanResume(ex);
                    return ValueTask.FromResult(shouldHandle);
                },
                OnRetry = args =>
                {
                    if (args.Outcome.Exception is { } ex)
                        logger?.ChangeStreamRetrying(ex, changeStreamId, args.AttemptNumber + 1, args.RetryDelay);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private sealed class Connection : IFeedConnection<BsonTimestamp>
    {
        private readonly ChangeStreamSubject<TDocument, TChange, TResult> _subject;
        private readonly Func<TChange, BsonDocument> _resumeTokenSelector;
        private readonly Func<TChange, BsonTimestamp> _clusterTimeSelector;
        private readonly Func<TChange, TResult> _resultSelector;
        private readonly FeedObserverHub<TResult> _observerHub;
        private readonly ILogger? _logger;
        private readonly string _subjectId;
        private readonly Task _producerTask;
        private readonly CancellationTokenSource _stopCts = new();
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private BsonTimestamp? _opTimeHighWaterMark;
        private int _disposed;

        public Connection(ChangeStreamSubject<TDocument, TChange, TResult> subject)
        {
            _subject = subject;
            _resumeTokenSelector = subject._resumeTokenSelector;
            _clusterTimeSelector = subject._clusterTimeSelector;
            _resultSelector = subject._resultSelector;
            _observerHub = subject._observerHub;
            _logger = subject._logger;
            _subjectId = subject._subjectId;

            _producerTask = RunProducerAsync();
        }

        public Task Completion => _completionTcs.Task;

        /// <summary>
        /// Gets an optime at or after every change that the stream has delivered or won't deliver. It starts at the
        /// primary's last write, just before the stream's start, and then follows the changes delivered. A majority
        /// read that waits for it sees every such change, even on a node whose commit point lagged after a failover.
        /// </summary>
        public BsonTimestamp HighWaterMark =>
            Volatile.Read(ref _opTimeHighWaterMark) ??
            throw new InvalidOperationException("The change stream connection is not connected.");

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _logger?.ChangeStreamStopping(_subjectId);

            using (_stopCts)
            {
                if (!_stopCts.IsCancellationRequested)
                    await _stopCts.CancelAsync().ConfigureAwait(false);

                await _producerTask.ConfigureAwait(false);
            }

            CorrelationId.Release(_subjectId);
        }

        private async Task RunProducerAsync()
        {
            _logger?.ChangeStreamStarted(_subjectId);

            try
            {
                BsonDocument? resumeToken = null;

                while (true)
                {
                    _stopCts.Token.ThrowIfCancellationRequested();

                    using var cursor = await OpenCursorAsync(resumeToken).ConfigureAwait(false);

                    // The resume token is seeded when the cursor opens, so a cursor that fails before its first batch
                    // resumes rather than restarting later. The driver exposes the token when the first batch is empty,
                    // and a non-empty first batch cannot fail before it is read.
                    // See:
                    // - https://github.com/mongodb/specifications/blob/bed11334/source/change-streams/change-streams.md#updating-the-cached-resume-token
                    // - https://jira.mongodb.org/browse/SERVER-35740
                    resumeToken ??= cursor.GetResumeToken();

                    _logger?.ChangeStreamConnected(_subjectId);
                    _subject._connectedTcs.TrySetResult();

                    try
                    {
                        while (await cursor.MoveNextAsync(_stopCts.Token).ConfigureAwait(false))
                        {
                            var batchCount = 0;

                            foreach (var change in cursor.Current)
                            {
                                var result = _resultSelector(change);
                                var clusterTime = _clusterTimeSelector(change);

                                // A change's cluster time is the optime of the oplog entry that committed it, so a read
                                // can wait for it. The mark is raised before observers are notified, and the exchange
                                // is a full fence, so an observer that attaches too late for a change reads a mark that
                                // covers it.
                                Interlocked.Exchange(ref _opTimeHighWaterMark, clusterTime);

                                await _observerHub.NotifyNextAsync(result, _stopCts.Token).ConfigureAwait(false);
                                resumeToken = _resumeTokenSelector(change);
                                batchCount++;
                            }

                            _logger?.ChangeStreamBatchProcessed(_subjectId, batchCount);

                            var batchResumeToken = cursor.GetResumeToken();
                            if (batchResumeToken is not null)
                                resumeToken = batchResumeToken;
                        }

                        // A live change stream should never end, so reconnect.
                        _logger?.ChangeStreamCursorEnded(_subjectId);
                    }
                    catch (Exception ex) when (ChangeStreamResumePolicy.CanResume(ex))
                    {
                        _logger?.ChangeStreamConnectionLost(ex, _subjectId);
                    }
                }
            }
            catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
            {
                _logger?.ChangeStreamCanceled(_subjectId);
                await _observerHub.NotifyDisposedAsync().ConfigureAwait(false);
                _completionTcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger?.ChangeStreamFailed(ex, _subjectId);

                // A subscription told of the error may attach again straight away. Completing first means it sees that
                // this connection has finished and gets a new one, rather than failing to attach to this one.
                _completionTcs.TrySetException(ex);

                try
                {
                    await _observerHub.NotifyErrorAsync(ex, _stopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
                {
                    _logger?.ChangeStreamCanceled(_subjectId);
                }
            }
            finally
            {
                _logger?.ChangeStreamStopped(_subjectId);
            }
        }

        // Opens a cursor through the retry. Without a resume token, the open also reads the primary's last write, so a
        // failure there is retried as a failure to open the cursor is.
        private async Task<IChangeStreamCursor<TChange>> OpenCursorAsync(BsonDocument? resumeToken)
        {
            return await _subject._resiliencePipeline
                .ExecuteAsync(
                    static async (s, ct) =>
                    {
                        var options = s.ResumeToken is not null
                            ? new ChangeStreamOptions { ResumeAfter = s.ResumeToken }
                            : await s.Self.GetInitialOptionsAsync(ct).ConfigureAwait(false);

                        return await s.Self._subject._cursorFactory(s.Self._subject._pipeline, options, ct)
                            .ConfigureAwait(false);
                    },
                    (Self: this, ResumeToken: resumeToken),
                    _stopCts.Token)
                .ConfigureAwait(false);
        }

        // Returns the options for the first open, and starts the high-water mark at or after every change that the
        // stream won't deliver.
        private async Task<ChangeStreamOptions> GetInitialOptionsAsync(CancellationToken cancellationToken)
        {
            // The stream starts one tick after the primary's last write. The mark must be an oplog optime rather than
            // the current cluster time, because a catch-up reads only once the oplog reaches the mark. After a
            // transaction commits, the logical clock runs one tick ahead of the oplog, so a mark at the current
            // cluster time would stall a catch-up until the periodic no-op writer runs, every 10 seconds by default.
            // Only a shard writes a no-op to reach the time at once:
            // https://github.com/mongodb/mongo/blob/r7.0.16/src/mongo/db/read_concern_mongod.cpp#L146-L153
            // The server would start the stream at the same point by default, but the mark would be unknown:
            // https://github.com/mongodb/mongo/blob/r7.0.16/src/mongo/db/pipeline/document_source_change_stream.cpp#L238-L253
            var lastApplied = await GetLastAppliedOpTimeAsync(cancellationToken).ConfigureAwait(false);

            // The start is inclusive, so the stream won't deliver the changes up to the last write.
            Volatile.Write(ref _opTimeHighWaterMark, lastApplied);
            return new ChangeStreamOptions { StartAtOperationTime = new BsonTimestamp(lastApplied.Value + 1) };
        }

        private async Task<BsonTimestamp> GetLastAppliedOpTimeAsync(CancellationToken cancellationToken)
        {
            // hello.lastWrite.opTime is the optime of the last write applied on the node that answers. After a
            // failover, only the primary is sure to have applied every majority-committed write.
            // https://www.mongodb.com/docs/manual/reference/command/hello/
            //
            // This requires MongoDB 4.4.2 or later. The driver requires 4.4, and the hello command arrived in 4.4.2.
            // https://github.com/mongodb/mongo-csharp-driver/blob/v3.11.1/src/MongoDB.Driver/Core/Misc/WireVersion.cs#L187
            // https://github.com/mongodb/mongo/blob/r4.4.2/src/mongo/db/repl/replication_info.cpp#L304
            var hello = await _subject._mongoClient
                .GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), ReadPreference.Primary, cancellationToken)
                .ConfigureAwait(false);

            if (!hello.TryGetValue("lastWrite", out var lastWrite))
            {
                throw new NotSupportedException(
                    "The server did not report a last write. Change streams require a replica set member.");
            }

            return lastWrite["opTime"]["ts"].AsBsonTimestamp;
        }
    }
}