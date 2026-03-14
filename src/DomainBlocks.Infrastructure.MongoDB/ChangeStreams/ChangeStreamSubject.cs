using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public sealed class ChangeStreamSubject<TDocument, TResult>(
    ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
    PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
    Func<TResult, BsonDocument> resumeTokenSelector,
    ChangeStreamSubjectOptions options,
    ILogger? logger) :
    IChangeStreamSubject<TResult>
{
    private readonly ObserverRegistry _observers = new();
    private int _connected;

    public IDisposable Attach(IChangeStreamObserver<TResult> observer) => _observers.Attach(observer);

    public IChangeStreamConnection Connect()
    {
        return Interlocked.Exchange(ref _connected, 1) == 0
            ? new Connection(cursorFactory, pipeline, resumeTokenSelector, _observers, options, logger)
            : throw new InvalidOperationException("ConnectAsync may only be called once.");
    }

    private sealed class ObserverRegistry
    {
        private ImmutableArray<IChangeStreamObserver<TResult>> _observers = [];

        public IDisposable Attach(IChangeStreamObserver<TResult> observer)
        {
            ImmutableInterlocked.Update(
                ref _observers,
                (observers, o) => observers.Add(o),
                observer);

            return new ObserverAttachment(this, observer);
        }

        public void Detach(IChangeStreamObserver<TResult> observer)
        {
            ImmutableInterlocked.Update(
                ref _observers,
                (observers, o) => observers.Remove(o),
                observer);
        }

        public ImmutableArray<IChangeStreamObserver<TResult>> Snapshot() => _observers;
    }

    private sealed class ObserverAttachment(ObserverRegistry registry, IChangeStreamObserver<TResult> observer) :
        IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                registry.Detach(observer);
        }
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
        private readonly TaskCompletionSource _completionTcs = new();
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
            {
                _logger?.LogDebug("Dispose ignored; already disposed");
                return;
            }

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
            _logger?.LogInformation("Producer started");

            try
            {
                while (true)
                {
                    _stopCts.Token.ThrowIfCancellationRequested();

                    var mongoOptions = _options.MongoOptions;

                    if (_lastResumeToken is not null)
                    {
                        mongoOptions = mongoOptions.Copy();
                        mongoOptions.ResumeAfter = _lastResumeToken;
                        mongoOptions.StartAfter = null;
                        mongoOptions.StartAtOperationTime = null;
                    }

                    using var cursor = await _cursorFactory(_pipeline, mongoOptions, _stopCts.Token)
                        .ConfigureAwait(false);

                    try
                    {
                        while (await cursor.MoveNextAsync(_stopCts.Token).ConfigureAwait(false))
                        {
                            foreach (var result in cursor.Current)
                            {
                                await NotifyObserversAsync(result).ConfigureAwait(false);
                                _lastResumeToken = _resumeTokenSelector(result);
                            }

                            var batchResumeToken = cursor.GetResumeToken();
                            if (batchResumeToken is not null)
                                _lastResumeToken = batchResumeToken;
                        }
                    }
                    catch (Exception ex) when (ChangeStreamResumePolicy.CanResume(ex))
                    {
                        _logger?.LogWarning(ex, "Connection lost; will reconnect");
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
                _completionTcs.TrySetException(ex);
            }
        }

        private async Task NotifyObserversAsync(TResult result)
        {
            var observers = _observers.Snapshot();

            foreach (var observer in observers)
            {
                try
                {
                    await observer.OnNextAsync(result, _stopCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(
                        ex,
                        "OnNextAsync for observer '{ObserverType}' failed",
                        observer.GetType().FullName);
                }
            }
        }
    }
}