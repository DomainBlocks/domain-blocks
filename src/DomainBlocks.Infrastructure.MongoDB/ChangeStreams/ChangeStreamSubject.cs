using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

internal sealed class ChangeStreamSubject<TDocument, TResult>(
    ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
    PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
    Func<TResult, BsonDocument> resumeTokenSelector,
    ChangeStreamSubjectOptions options,
    ILogger? logger) :
    IChangeStreamSubject<TResult>
{
    private readonly ObserverRegistry<IChangeStreamObserver<TResult>> _observers = new();
    private int _connected;

    public IDisposable Attach(IChangeStreamObserver<TResult> observer)
    {
        return _observers.Attach(observer);
    }

    public IDisposable AttachGroup(IEnumerable<IChangeStreamObserver<TResult>> observers)
    {
        return _observers.AttachGroup(observers);
    }

    public IChangeStreamConnection Connect()
    {
        return Interlocked.Exchange(ref _connected, 1) == 0
            ? new Connection(cursorFactory, pipeline, resumeTokenSelector, _observers, options, logger)
            : throw new InvalidOperationException("ConnectAsync may only be called once.");
    }

    private sealed class Connection : IChangeStreamConnection
    {
        private readonly ChangeStreamCursorFactory<TDocument, TResult> _cursorFactory;
        private readonly PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> _pipeline;
        private readonly Func<TResult, BsonDocument> _resumeTokenSelector;
        private readonly ObserverRegistry<IChangeStreamObserver<TResult>> _observers;
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
            ObserverRegistry<IChangeStreamObserver<TResult>> observers,
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

                    using var cursor = await GetChangeStreamCursorAsync().ConfigureAwait(false);

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
                        "OnNextAsync failed for observer '{ObserverType}'",
                        observer.GetType().FullName);
                }
            }
        }
    }
}