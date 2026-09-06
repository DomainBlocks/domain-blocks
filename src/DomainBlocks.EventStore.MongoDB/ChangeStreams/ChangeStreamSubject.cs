using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal static class ChangeStreamSubject
{
    public static ChangeStreamSubject<TDocument, TResult> Create<TDocument, TResult>(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        Func<TResult, BsonDocument> resumeTokenSelector,
        ChangeStreamSubjectOptions? options = null,
        ILogger? logger = null)
    {
        return new ChangeStreamSubject<TDocument, TResult>(
            cursorFactory,
            pipeline,
            resumeTokenSelector,
            options,
            logger);
    }
}

internal sealed class ChangeStreamSubject<TDocument, TResult> : IChangeStreamSubject<TResult>
{
    private readonly ChangeStreamCursorFactory<TDocument, TResult> _cursorFactory;
    private readonly PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> _pipeline;
    private readonly Func<TResult, BsonDocument> _resumeTokenSelector;
    private readonly ChangeStreamSubjectOptions _options;
    private readonly ILogger? _logger;
    private readonly ConnectionState _connectionState;
    private readonly string _subjectId = CorrelationId.ReserveGenerated();
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _connected;

    public ChangeStreamSubject(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        Func<TResult, BsonDocument> resumeTokenSelector,
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

        _cursorFactory = cursorFactory;
        _pipeline = pipeline;
        _resumeTokenSelector = resumeTokenSelector;
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

    private static ChangeStreamCursorFactory<TDocument, TResult> AddResilience(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
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
                    ValueTask.FromResult(args.Outcome.Exception is { } ex && ChangeStreamResumePolicy.CanResume(ex)),
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
        private readonly ChangeStreamSubject<TDocument, TResult> _subject;
        private readonly Func<TResult, BsonDocument> _resumeTokenSelector;
        private readonly ConnectionState _state;
        private readonly ILogger? _logger;
        private readonly string _subjectId;
        private readonly Task _producerTask;
        private readonly CancellationTokenSource _stopCts = new();
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private BsonDocument? _lastResumeToken;
        private int _disposed;

        public Connection(ChangeStreamSubject<TDocument, TResult> subject)
        {
            _subject = subject;
            _resumeTokenSelector = subject._resumeTokenSelector;
            _state = subject._connectionState;
            _logger = subject._logger;
            _subjectId = subject._subjectId;

            _producerTask = RunProducerAsync();
        }

        public Task Completion => _completionTcs.Task;

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
                while (true)
                {
                    _stopCts.Token.ThrowIfCancellationRequested();

                    using var cursor = await GetChangeStreamCursorAsync().ConfigureAwait(false);

                    _subject._connectedTcs.TrySetResult();

                    try
                    {
                        while (await cursor.MoveNextAsync(_stopCts.Token).ConfigureAwait(false))
                        {
                            var count = 0;

                            foreach (var result in cursor.Current)
                            {
                                await _state.NotifyNextAsync(result, _stopCts.Token).ConfigureAwait(false);
                                _lastResumeToken = _resumeTokenSelector(result);
                                count++;
                            }

                            _logger?.ChangeStreamBatchProcessed(_subjectId, count);

                            var batchResumeToken = cursor.GetResumeToken();
                            if (batchResumeToken is not null)
                                _lastResumeToken = batchResumeToken;
                        }

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

        private async Task<IChangeStreamCursor<TResult>> GetChangeStreamCursorAsync()
        {
            var options = _subject._options.MongoOptions;

            if (_lastResumeToken is not null)
            {
                options = options.Copy();
                options.ResumeAfter = _lastResumeToken;
                options.StartAfter = null;
                options.StartAtOperationTime = null;
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

            while (true)
            {
                var current = Volatile.Read(ref _state);

                if (current.Completion is { Error: var error })
                    throw new InvalidOperationException("Cannot attach to a faulted change stream connection.", error);

                if (current.Completion is { Error: null })
                    throw new InvalidOperationException("Cannot attach to a completed change stream connection.");

                var next = new State(current.Attachments.Add(attachment));
                if (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current))
                    continue;

                logger?.ObserverAttached(subjectId, observerId, next.Attachments.Length);
                return attachment;
            }
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
            while (true)
            {
                var current = Volatile.Read(ref _state);
                if (current.Completion is not null)
                    return;

                var faulted = new State(current.Attachments, new Completion(error));
                if (!ReferenceEquals(Interlocked.CompareExchange(ref _state, faulted, current), current))
                    continue;

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

                return;
            }
        }

        public void SetComplete()
        {
            while (true)
            {
                var current = Volatile.Read(ref _state);
                if (current.Completion is not null)
                    return;

                var completed = new State(current.Attachments, new Completion());
                if (!ReferenceEquals(Interlocked.CompareExchange(ref _state, completed, current), current))
                    continue;

                return;
            }
        }

        private void Detach(Attachment attachment)
        {
            while (true)
            {
                var current = Volatile.Read(ref _state);
                var next = new State(current.Attachments.Remove(attachment), current.Completion);

                if (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current))
                    continue;

                logger?.ObserverDetached(subjectId, attachment.ObserverId, next.Attachments.Length);
                return;
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