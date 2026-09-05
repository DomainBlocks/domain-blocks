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
    private readonly ObserverRegistry _observers = new();
    private int _connected;
    private readonly ChangeStreamCursorFactory<TDocument, TResult> _cursorFactory;
    private readonly PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> _pipeline;
    private readonly Func<TResult, BsonDocument> _resumeTokenSelector;
    private readonly ChangeStreamSubjectOptions _options;
    private readonly ILogger? _logger;

    public ChangeStreamSubject(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        Func<TResult, BsonDocument> resumeTokenSelector,
        ChangeStreamSubjectOptions? options = null,
        ILogger? logger = null)
    {
        options ??= ChangeStreamSubjectOptions.Default;
        cursorFactory = AddResilience(cursorFactory, options.MaxRetryAttempts, options.MaxRetryDelay);

        _cursorFactory = cursorFactory;
        _pipeline = pipeline;
        _resumeTokenSelector = resumeTokenSelector;
        _options = options;
        _logger = logger;
    }

    public IDisposable Attach(IChangeStreamObserver<TResult> observer) => _observers.Attach(observer);

    public IChangeStreamConnection Connect()
    {
        return Interlocked.Exchange(ref _connected, 1) == 0
            ? new Connection(_cursorFactory, _pipeline, _resumeTokenSelector, _observers, _options, _logger)
            : throw new InvalidOperationException("Connect may only be called once.");
    }

    private static ChangeStreamCursorFactory<TDocument, TResult> AddResilience(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
        int maxRetryAttempts,
        TimeSpan maxRetryDelay,
        ILogger? logger = null)
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
                    logger?.LogWarning(
                        args.Outcome.Exception,
                        "Attempt {Attempt}: Connection attempt failed; retrying in {Delay}",
                        args.AttemptNumber + 1,
                        args.RetryDelay);

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
            ILogger? logger)
        {
            _cursorFactory = cursorFactory;
            _pipeline = pipeline;
            _resumeTokenSelector = resumeTokenSelector;
            _observers = observers;
            _options = options;
            _logger = logger;

            _producerTask = RunProducerAsync();
        }

        public Task Completion => _completionTcs.Task;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _logger?.LogDebug("Disposing");

            using (_stopCts)
            {
                if (!_stopCts.IsCancellationRequested)
                    await _stopCts.CancelAsync().ConfigureAwait(false);

                await _producerTask.ConfigureAwait(false);
            }
        }

        private async Task RunProducerAsync()
        {
            _logger?.LogDebug("Producer started");

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
                                await _observers.NotifyNextAsync(result, _logger, _stopCts.Token).ConfigureAwait(false);
                                _lastResumeToken = _resumeTokenSelector(result);
                                count++;
                            }

                            _logger?.LogDebug("Processed change stream batch size: {Count}", count);

                            var batchResumeToken = cursor.GetResumeToken();
                            if (batchResumeToken is not null)
                                _lastResumeToken = batchResumeToken;
                        }

                        _logger?.LogWarning("Change stream cursor ended unexpectedly; reconnecting");
                    }
                    catch (Exception ex) when (ChangeStreamResumePolicy.CanResume(ex))
                    {
                        _logger?.LogWarning(ex, "Connection lost; reconnecting");
                        // Continue outer loop (reconnect)
                    }
                }
            }
            catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
            {
                _logger?.LogDebug("Producer canceled by stop token");
                _completionTcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Producer failed");

                try
                {
                    await _observers.NotifyErrorAsync(ex, _logger, _stopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
                {
                    _logger?.LogDebug("Error notification raced with shutdown");
                }

                _completionTcs.TrySetException(ex);
            }
            finally
            {
                _logger?.LogInformation("Producer stopped");
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

    private sealed class ObserverRegistry
    {
        private State _state = new([]);

        public IDisposable Attach(IChangeStreamObserver<TResult> observer)
        {
            var attachment = new Attachment(this, observer);

            while (true)
            {
                var current = Volatile.Read(ref _state);
                if (current.Error is not null)
                    throw new InvalidOperationException("The change stream subject has faulted.", current.Error);

                var next = new State(current.Attachments.Add(attachment));
                if (ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current))
                    return attachment;
            }
        }

        public async ValueTask NotifyNextAsync(TResult item, ILogger? logger, CancellationToken cancellationToken)
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
                    logger?.LogError(
                        observerException,
                        "OnNextAsync failed for observer '{ObserverType}'; detaching",
                        attachment.Observer.GetType().FullName);

                    attachment.Dispose();
                }
            }
        }

        public async ValueTask NotifyErrorAsync(Exception error, ILogger? logger, CancellationToken cancellationToken)
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
                        logger?.LogError(
                            observerException,
                            "OnErrorAsync failed for observer '{ObserverType}'",
                            attachment.Observer.GetType().FullName);
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
                if (ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current))
                    return;
            }
        }

        private sealed class State(ImmutableArray<Attachment> attachments, Exception? error = null)
        {
            public ImmutableArray<Attachment> Attachments { get; } = attachments;
            public Exception? Error { get; } = error;
        }

        private sealed class Attachment(ObserverRegistry registry, IChangeStreamObserver<TResult> observer) :
            IDisposable
        {
            private int _disposed;

            public IChangeStreamObserver<TResult> Observer { get; } = observer;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    registry.Detach(this);
            }
        }
    }
}