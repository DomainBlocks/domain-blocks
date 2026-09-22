using System.Collections.Immutable;
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
        Func<TChange, TResult> resultSelector,
        ChangeStreamSubjectOptions? options = null,
        ILogger? logger = null)
    {
        return new ChangeStreamSubject<TDocument, TChange, TResult>(
            mongoClient,
            cursorFactory,
            pipeline,
            resumeTokenSelector,
            resultSelector,
            options,
            logger);
    }
}

internal sealed class ChangeStreamSubject<TDocument, TChange, TResult> : IChangeStreamSubject<TResult>
{
    private readonly IMongoClient _mongoClient;
    private readonly ChangeStreamCursorFactory<TDocument, TChange> _cursorFactory;
    private readonly PipelineDefinition<ChangeStreamDocument<TDocument>, TChange> _pipeline;
    private readonly Func<TChange, BsonDocument> _resumeTokenSelector;
    private readonly Func<TChange, TResult> _resultSelector;
    private readonly ChangeStreamSubjectOptions _options;
    private readonly ILogger? _logger;
    private readonly ConnectionState _connectionState;
    private readonly string _subjectId = CorrelationId.ReserveGenerated();
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _connected;

    public ChangeStreamSubject(
        IMongoClient mongoClient,
        ChangeStreamCursorFactory<TDocument, TChange> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TChange> pipeline,
        Func<TChange, BsonDocument> resumeTokenSelector,
        Func<TChange, TResult> resultSelector,
        ChangeStreamSubjectOptions? options = null,
        ILogger? logger = null)
    {
        options ??= ChangeStreamSubjectOptions.Default;

        cursorFactory = AddResilience(
            cursorFactory,
            options.MaxRetryAttempts,
            options.MaxRetryDelay,
            logger,
            _subjectId);

        _mongoClient = mongoClient;
        _cursorFactory = cursorFactory;
        _pipeline = pipeline;
        _resumeTokenSelector = resumeTokenSelector;
        _resultSelector = resultSelector;
        _options = options;
        _logger = logger;
        _connectionState = new ConnectionState(logger, _subjectId);
    }

    public IDisposable Attach(IChangeStreamObserver<TResult> observer, string correlationId = "unknown") =>
        _connectionState.Attach(observer, correlationId);

    public async Task<IChangeStreamConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var connection = Interlocked.Exchange(ref _connected, 1) == 0
            ? new Connection(this)
            : throw new InvalidOperationException("Connect may only be called once.");

        var task = await Task
            .WhenAny(_connectedTcs.Task, connection.Completion)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        if (task == _connectedTcs.Task)
            return connection;

        await connection.Completion.ConfigureAwait(false);
        throw new InvalidOperationException("The change stream connection completed before it connected.");
    }

    private static ChangeStreamCursorFactory<TDocument, TChange> AddResilience(
        ChangeStreamCursorFactory<TDocument, TChange> cursorFactory,
        int maxRetryAttempts,
        TimeSpan maxRetryDelay,
        ILogger? logger,
        string changeStreamId)
    {
        var resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
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

        return async (pipeline, options, ct) => await resiliencePipeline
            .ExecuteAsync(
                async innerCt => await cursorFactory(pipeline, options, innerCt).ConfigureAwait(false),
                ct)
            .ConfigureAwait(false);
    }

    private sealed class Connection : IChangeStreamConnection
    {
        private readonly ChangeStreamSubject<TDocument, TChange, TResult> _subject;
        private readonly Func<TChange, BsonDocument> _resumeTokenSelector;
        private readonly Func<TChange, TResult> _resultSelector;
        private readonly ConnectionState _state;
        private readonly ILogger? _logger;
        private readonly string _subjectId;
        private readonly Task _producerTask;
        private readonly CancellationTokenSource _stopCts = new();
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private BsonTimestamp? _operationTime;
        private int _disposed;

        public Connection(ChangeStreamSubject<TDocument, TChange, TResult> subject)
        {
            _subject = subject;
            _resumeTokenSelector = subject._resumeTokenSelector;
            _resultSelector = subject._resultSelector;
            _state = subject._connectionState;
            _logger = subject._logger;
            _subjectId = subject._subjectId;

            _producerTask = RunProducerAsync();
        }

        public Task Completion => _completionTcs.Task;

        public BsonTimestamp OperationTime =>
            _operationTime ?? throw new InvalidOperationException("The change stream connection is not connected.");

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

                    // Anchor for catch-up subscriptions: the stream delivers every change after this optime, and a
                    // majority read that waits for it sees at least every change up to it. It must be an oplog optime,
                    // not a cluster time: after a transaction commit the logical clock runs one tick ahead of the
                    // oplog, and a non-sharded replica set leaves a read waiting for such a time stalled until the
                    // periodic no-op writer runs. Without an explicit start the server would start the stream at the
                    // same point, but the anchor would be unknown:
                    // https://github.com/mongodb/mongo/blob/r7.0.16/src/mongo/db/pipeline/document_source_change_stream.cpp#L238-L253
                    _operationTime ??= await GetLastAppliedOpTimeAsync().ConfigureAwait(false);

                    using var cursor = await GetChangeStreamCursorAsync(resumeToken).ConfigureAwait(false);

                    // Seed the resume token from the open, so a cursor that fails before its first batch resumes
                    // instead of restarting later. The driver exposes it when the first batch is empty; a non-empty
                    // first batch cannot fail before it is read.
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
                                await _state.NotifyNextAsync(result, _stopCts.Token).ConfigureAwait(false);
                                resumeToken = _resumeTokenSelector(change);
                                batchCount++;
                            }

                            _logger?.ChangeStreamBatchProcessed(_subjectId, batchCount);

                            var batchResumeToken = cursor.GetResumeToken();
                            if (batchResumeToken is not null)
                                resumeToken = batchResumeToken;
                        }

                        // A live change stream is expected to remain open. Log a warning and reconnect.
                        _logger?.ChangeStreamCursorEnded(_subjectId);
                    }
                    catch (Exception ex) when (ChangeStreamResumePolicy.CanResume(ex))
                    {
                        _logger?.ChangeStreamConnectionLost(ex, _subjectId);
                        // Continue outer loop (reconnect)
                    }
                }
            }
            catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
            {
                _logger?.ChangeStreamCanceled(_subjectId);
                _state.SetComplete();
                _completionTcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger?.ChangeStreamFailed(ex, _subjectId);

                try
                {
                    await _state.NotifyErrorAsync(ex, _stopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
                {
                    _logger?.ChangeStreamCanceled(_subjectId);
                }

                _completionTcs.TrySetException(ex);
            }
            finally
            {
                _logger?.ChangeStreamStopped(_subjectId);
            }
        }

        private async Task<BsonTimestamp> GetLastAppliedOpTimeAsync()
        {
            // hello.lastWrite.opTime is the optime of the last write applied on the node that answers. The primary
            // read preference is for freshness, not correctness.
            // https://www.mongodb.com/docs/manual/reference/command/hello/
            //
            // Floor: MongoDB 4.4.2. The driver requires 4.4, and the hello command arrived in 4.4.2.
            // https://github.com/mongodb/mongo-csharp-driver/blob/v3.11.1/src/MongoDB.Driver/Core/Misc/WireVersion.cs#L187
            // https://github.com/mongodb/mongo/blob/r4.4.2/src/mongo/db/repl/replication_info.cpp#L304
            var hello = await _subject._mongoClient
                .GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), ReadPreference.Primary, _stopCts.Token)
                .ConfigureAwait(false);

            if (!hello.TryGetValue("lastWrite", out var lastWrite))
            {
                throw new NotSupportedException(
                    "The server did not report a last write. Change streams require a replica set member.");
            }

            return lastWrite["opTime"]["ts"].AsBsonTimestamp;
        }

        private async Task<IChangeStreamCursor<TChange>> GetChangeStreamCursorAsync(BsonDocument? resumeToken)
        {
            var options = _subject._options.MongoOptions;

            if (resumeToken is not null)
            {
                options = options.Copy();
                options.ResumeAfter = resumeToken;
                options.StartAfter = null;
                options.StartAtOperationTime = null;
            }
            else if (options.ResumeAfter is null && options.StartAfter is null && options.StartAtOperationTime is null)
            {
                // A caller's own start point is earlier, so it stands. startAtOperationTime is inclusive and the write
                // at the anchor belongs to the catch-up read, so start one tick later.
                options = options.Copy();
                options.StartAtOperationTime = new BsonTimestamp(_operationTime!.Value + 1);
            }

            return await _subject._cursorFactory(_subject._pipeline, options, _stopCts.Token).ConfigureAwait(false);
        }
    }

    private sealed class ConnectionState(ILogger? logger, string subjectId)
    {
        private State _state = new([]);

        public IDisposable Attach(IChangeStreamObserver<TResult> observer, string observerId)
        {
            var attachment = new Attachment(observer, observerId, Detach);

            TryUpdate(
                static (current, attachment) => current.Completion switch
                {
                    { Error: { } error } => throw new InvalidOperationException(
                        "Cannot attach to a faulted change stream connection.",
                        error),

                    { Error: null } => throw new InvalidOperationException(
                        "Cannot attach to a completed change stream connection."),

                    _ => new State(current.Attachments.Add(attachment))
                },
                attachment,
                out var next);

            logger?.ObserverAttached(subjectId, observerId, next.Attachments.Length);
            return attachment;
        }

        public async ValueTask NotifyNextAsync(TResult item, CancellationToken cancellationToken)
        {
            foreach (var attachment in Volatile.Read(ref _state).Attachments)
            {
                try
                {
                    await attachment.Observer.OnNextAsync(item, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception observerException)
                {
                    logger?.ObserverOnNextFailed(observerException, subjectId, attachment.ObserverId);
                    attachment.Dispose();
                }
            }
        }

        public async ValueTask NotifyErrorAsync(Exception error, CancellationToken cancellationToken)
        {
            if (!TryUpdate(
                    static (current, error) => current.Completion is null
                        ? new State(current.Attachments, new Completion(error))
                        : null,
                    error,
                    out var faulted))
            {
                return;
            }

            foreach (var attachment in faulted.Attachments)
            {
                try
                {
                    await attachment.Observer.OnErrorAsync(error, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception observerException)
                {
                    logger?.ObserverOnErrorFailed(observerException, subjectId, attachment.ObserverId);
                }
            }
        }

        public void SetComplete()
        {
            TryUpdate(
                static (current, completion) => current.Completion is null
                    ? new State(current.Attachments, completion)
                    : null,
                new Completion(),
                out _);
        }

        private void Detach(Attachment attachment)
        {
            TryUpdate(
                static (current, attachment) => new State(current.Attachments.Remove(attachment), current.Completion),
                attachment,
                out var next);

            logger?.ObserverDetached(subjectId, attachment.ObserverId, next.Attachments.Length);
        }

        private bool TryUpdate<TArg>(Func<State, TArg, State?> updater, TArg argument, out State result)
        {
            while (true)
            {
                var current = Volatile.Read(ref _state);
                var next = updater(current, argument);

                if (next is null)
                {
                    result = current;
                    return false;
                }

                if (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current))
                    continue;

                result = next;
                return true;
            }
        }

        private sealed class State(ImmutableArray<Attachment> attachments, Completion? completion = null)
        {
            public ImmutableArray<Attachment> Attachments { get; } = attachments;
            public Completion? Completion { get; } = completion;
        }

        private sealed class Attachment(
            IChangeStreamObserver<TResult> observer,
            string observerId,
            Action<Attachment> onDispose) :
            IDisposable
        {
            private int _disposed;

            public IChangeStreamObserver<TResult> Observer { get; } = observer;
            public string ObserverId { get; } = observerId;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    onDispose(this);
            }
        }

        private sealed class Completion(Exception? error = null)
        {
            public Exception? Error { get; } = error;
        }
    }
}