using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.Infrastructure.MongoDB.Errors;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly;
using Polly.Retry;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public sealed class ChangeStreamSubscription<TDocument, TResult> : IChangeStreamSubscription<TResult>
{
    private readonly ChangeStreamCursorFactory<TDocument, TResult> _cursorFactory;
    private readonly PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> _pipeline;
    private readonly Func<TResult, BsonDocument> _resumeTokenSelector;
    private readonly ChangeStreamSubscriptionOptions _options;
    private readonly ILogger? _logger;
    private readonly Channel<TResult> _channel;
    private readonly Task _producerTask;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly TaskCompletionSource _liveTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BsonDocument? _lastProducedResumeToken;
    private int _consumptionStarted;
    private int _disposed;

    public ChangeStreamSubscription(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        Func<TResult, BsonDocument> resumeTokenSelector,
        ChangeStreamSubscriptionOptions? options,
        ILogger? logger)
    {
        _cursorFactory = cursorFactory;
        _pipeline = pipeline;
        _resumeTokenSelector = resumeTokenSelector;
        _options = options ?? new ChangeStreamSubscriptionOptions();
        _logger = logger;

        var channelOptions = new BoundedChannelOptions(_options.QueueSize)
        {
            SingleWriter = true,
            SingleReader = true
        };

        _channel = Channel.CreateBounded<TResult>(channelOptions);
        _producerTask = RunProducerAsync();
    }

    public async Task WaitUntilLiveAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
        await _liveTcs.Task.WaitAsync(linkedCts.Token);
    }

    public async Task ForEachAsync(
        Func<TResult, CancellationToken, ValueTask> onNext,
        CancellationToken cancellationToken = default)
    {
        EnsureConsumptionNotStarted();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        try
        {
            await foreach (var result in _channel.Reader.ReadAllAsync(linkedCts.Token).ConfigureAwait(false))
            {
                await onNext(result, linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            _logger?.LogDebug("ForEachAsync canceled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ForEachAsync failed");
            await _stopCts.CancelAsync().ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<TResult> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConsumptionNotStarted();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        try
        {
            await foreach (var result in _channel.Reader.ReadAllAsync(linkedCts.Token).ConfigureAwait(false))
            {
                yield return result;
            }
        }
        finally
        {
            if (!_stopCts.IsCancellationRequested)
                await _stopCts.CancelAsync().ConfigureAwait(false);
        }
    }

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

    private void EnsureConsumptionNotStarted()
    {
        if (Interlocked.Exchange(ref _consumptionStarted, 1) != 0)
        {
            throw new InvalidOperationException(
                "Consumption of this subscription has already started. Only a single consumer is supported.");
        }
    }

    private async Task RunProducerAsync()
    {
        _logger?.LogInformation("Producer started");

        Exception? terminalException = null;

        try
        {
            while (true)
            {
                _stopCts.Token.ThrowIfCancellationRequested();

                using var cursor = await GetCursorAsync().ConfigureAwait(false);

                try
                {
                    while (await cursor.MoveNextAsync(_stopCts.Token).ConfigureAwait(false))
                    {
                        if (_liveTcs.TrySetResult())
                            _logger?.LogInformation("Subscription live");

                        foreach (var result in cursor.Current)
                        {
                            await _channel.Writer.WriteAsync(result, _stopCts.Token).ConfigureAwait(false);
                            _lastProducedResumeToken = _resumeTokenSelector(result);
                        }

                        var batchResumeToken = cursor.GetResumeToken();
                        if (batchResumeToken is not null)
                            _lastProducedResumeToken = batchResumeToken;
                    }
                }
                catch (Exception ex) when (CanResume(ex))
                {
                    _logger?.LogWarning(ex, "Connection lost; will reconnect");
                    // Continue outer loop (reconnect)
                }
            }
        }
        catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
        {
            _logger?.LogDebug("Producer canceled by stop token");
            _liveTcs.TrySetCanceled();
        }
        catch (Exception ex)
        {
            terminalException = ex;
            _logger?.LogError(ex, "Producer failed");
            _liveTcs.TrySetException(ex);
        }
        finally
        {
            _channel.Writer.TryComplete(terminalException);
            _logger?.LogInformation("Producer stopped");
        }
    }

    private async Task<IChangeStreamCursor<TResult>> GetCursorAsync()
    {
        try
        {
            _logger?.LogInformation("Connecting");

            var mongoOptions = _options.MongoOptions ?? new ChangeStreamOptions();
            var isLive = _liveTcs.Task.IsCompletedSuccessfully;

            if (isLive && _lastProducedResumeToken is not null)
            {
                _logger?.LogInformation("Resuming after '{ResumeToken}'", _lastProducedResumeToken);

                mongoOptions = CopyMongoOptions(mongoOptions);
                mongoOptions.ResumeAfter = _lastProducedResumeToken;
                mongoOptions.StartAfter = null;
                mongoOptions.StartAtOperationTime = null;
            }

            var cursor = await GetResiliencePipeline()
                .ExecuteAsync(
                    async ct => await _cursorFactory(_pipeline, mongoOptions, ct).ConfigureAwait(false),
                    _stopCts.Token)
                .ConfigureAwait(false);

            _logger?.LogInformation("Connected");

            return cursor;
        }
        catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
        {
            // Avoid logging errors during expected shutdown
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect");
            throw;
        }
    }

    private static ChangeStreamOptions CopyMongoOptions(ChangeStreamOptions mongoOptions)
    {
        return new ChangeStreamOptions
        {
            BatchSize = mongoOptions.BatchSize,
            Collation = mongoOptions.Collation,
            Comment = mongoOptions.Comment,
            FullDocument = mongoOptions.FullDocument,
            FullDocumentBeforeChange = mongoOptions.FullDocumentBeforeChange,
            MaxAwaitTime = mongoOptions.MaxAwaitTime,
            ResumeAfter = mongoOptions.ResumeAfter,
            ShowExpandedEvents = mongoOptions.ShowExpandedEvents,
            StartAfter = mongoOptions.StartAfter,
            StartAtOperationTime = mongoOptions.StartAtOperationTime
        };
    }

    private ResiliencePipeline GetResiliencePipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxDelay = _options.MaxRetryDelay,
                ShouldHandle = args =>
                {
                    var ex = args.Outcome.Exception;
                    var shouldRetry = ex is not null && CanResume(ex);
                    return ValueTask.FromResult(shouldRetry);
                },
                OnRetry = args =>
                {
                    var ex = args.Outcome.Exception;
                    var attempt = args.AttemptNumber + 1;
                    var delay = args.RetryDelay;

                    _logger?.LogWarning(
                        ex,
                        "Attempt {Attempt}: Failed to connect; will retry in {Delay}",
                        attempt,
                        delay);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private static bool CanResume(Exception exception)
    {
        // See: https://github.com/mongodb/specifications/blob/master/source/change-streams/change-streams.md#resumable-error
        if (exception is
            MongoConnectionException { IsNetworkException: true } or
            MongoConnectionPoolPausedException or
            MongoCursorNotFoundException or
            TimeoutException)
        {
            return true;
        }

        // Requires wire version 9 or higher.
        return exception is MongoException ex && ex.HasErrorLabel(ErrorLabels.ResumableChangeStreamError);
    }
}