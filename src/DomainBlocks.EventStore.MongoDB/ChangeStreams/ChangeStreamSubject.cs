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
    private readonly ObserverRegistry _observers;
    private readonly string _subjectId = CorrelationId.ReserveGenerated();
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
        _observers = new ObserverRegistry(logger, _subjectId);
    }

    public IDisposable Attach(IChangeStreamObserver<TResult> observer, string correlationId = "unknown") =>
        _observers.Attach(observer, correlationId);

    public IChangeStreamConnection Connect()
    {
        return Interlocked.Exchange(ref _connected, 1) == 0
            ? new Connection(
                _cursorFactory,
                _pipeline,
                _resumeTokenSelector,
                _observers,
                _options,
                _logger,
                _subjectId)
            : throw new InvalidOperationException("Connect may only be called once.");
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
        private readonly ChangeStreamCursorFactory<TDocument, TResult> _cursorFactory;
        private readonly PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> _pipeline;
        private readonly Func<TResult, BsonDocument> _resumeTokenSelector;
        private readonly ObserverRegistry _observers;
        private readonly ChangeStreamSubjectOptions _options;
        private readonly ILogger? _logger;
        private readonly string _changeStreamId;
        private readonly Task _producerTask;
        private readonly CancellationTokenSource _stopCts = new();
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private BsonDocument? _lastResumeToken;
        private int _disposed;

        public Connection(
            ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
            PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
            Func<TResult, BsonDocument> resumeTokenSelector,
            ObserverRegistry observers,
            ChangeStreamSubjectOptions options,
            ILogger? logger,
            string changeStreamId)
        {
            _cursorFactory = cursorFactory;
            _pipeline = pipeline;
            _resumeTokenSelector = resumeTokenSelector;
            _observers = observers;
            _options = options;
            _logger = logger;
            _changeStreamId = changeStreamId;

            _producerTask = RunProducerAsync();
        }

        public Task Completion => _completionTcs.Task;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _logger?.ChangeStreamStopping(_changeStreamId);

            using (_stopCts)
            {
                if (!_stopCts.IsCancellationRequested)
                    await _stopCts.CancelAsync().ConfigureAwait(false);

                await _producerTask.ConfigureAwait(false);
            }

            CorrelationId.Release(_changeStreamId);
        }

        private async Task RunProducerAsync()
        {
            _logger?.ChangeStreamStarted(_changeStreamId);

            try
            {
                while (true)
                {
                    _stopCts.Token.ThrowIfCancellationRequested();

                    using var cursor = await GetChangeStreamCursorAsync().ConfigureAwait(false);

                    try
                    {
                        while (await cursor.MoveNextAsync(_stopCts.Token).ConfigureAwait(false))
                        {
                            var count = 0;

                            foreach (var result in cursor.Current)
                            {
                                await _observers.NotifyNextAsync(result, _stopCts.Token).ConfigureAwait(false);
                                _lastResumeToken = _resumeTokenSelector(result);
                                count++;
                            }

                            _logger?.ChangeStreamBatchProcessed(_changeStreamId, count);

                            var batchResumeToken = cursor.GetResumeToken();
                            if (batchResumeToken is not null)
                                _lastResumeToken = batchResumeToken;
                        }

                        _logger?.ChangeStreamCursorEnded(_changeStreamId);
                    }
                    catch (Exception ex) when (ChangeStreamResumePolicy.CanResume(ex))
                    {
                        _logger?.ChangeStreamConnectionLost(ex, _changeStreamId);
                        // Continue outer loop (reconnect)
                    }
                }
            }
            catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
            {
                _logger?.ChangeStreamCanceled(_changeStreamId);
                _completionTcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger?.ChangeStreamFailed(ex, _changeStreamId);

                try
                {
                    await _observers.NotifyErrorAsync(ex, _stopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
                {
                    _logger?.ChangeStreamCanceled(_changeStreamId);
                }

                _completionTcs.TrySetException(ex);
            }
            finally
            {
                _logger?.ChangeStreamStopped(_changeStreamId);
            }
        }

        private async Task<IChangeStreamCursor<TResult>> GetChangeStreamCursorAsync()
        {
            var options = _options.MongoOptions;

            if (_lastResumeToken is not null)
            {
                options = options.Copy();
                options.ResumeAfter = _lastResumeToken;
                options.StartAfter = null;
                options.StartAtOperationTime = null;
            }

            return await _cursorFactory(_pipeline, options, _stopCts.Token).ConfigureAwait(false);
        }
    }

    private sealed class ObserverRegistry(ILogger? logger, string subjectId)
    {
        private State _state = new([]);

        public IDisposable Attach(IChangeStreamObserver<TResult> observer, string observerId)
        {
            var attachment = new Attachment(this, observer, observerId);

            while (true)
            {
                var current = Volatile.Read(ref _state);
                if (current.Error is not null)
                    throw new InvalidOperationException("The change stream subject has faulted.", current.Error);

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
                if (current.Error is not null)
                    return;

                var faulted = new State(current.Attachments, error);
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

        private void Detach(Attachment attachment)
        {
            while (true)
            {
                var current = Volatile.Read(ref _state);
                var next = new State(current.Attachments.Remove(attachment), current.Error);

                if (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current))
                    continue;

                logger?.ObserverDetached(subjectId, attachment.ObserverId, next.Attachments.Length);
                return;
            }
        }

        private sealed class State(ImmutableArray<Attachment> attachments, Exception? error = null)
        {
            public ImmutableArray<Attachment> Attachments { get; } = attachments;
            public Exception? Error { get; } = error;
        }

        private sealed class Attachment(
            ObserverRegistry registry,
            IChangeStreamObserver<TResult> observer,
            string observerId) :
            IDisposable
        {
            private int _disposed;

            public IChangeStreamObserver<TResult> Observer { get; } = observer;
            public string ObserverId { get; } = observerId;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    registry.Detach(this);
            }
        }
    }
}