using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Commits queued append requests in batches, preparing each batch with the policy and resolving its conflicts.
/// </summary>
internal sealed class BatchProcessor<TContext>(
    IBatchCommitter committer,
    IMongoSequencedAppenderPolicy<TContext> policy,
    MongoSequencedAppenderOptions options,
    ILogger logger)
{
    private readonly Dictionary<Guid, AppendRequest<TContext>> _requests = [];
    private readonly List<BsonDocument> _documents = [];
    private readonly List<AppendRequest<TContext>> _requestByDocumentIndex = [];
    private readonly Dictionary<Guid, int> _conflictRetryCounts = [];

    /// <summary>
    /// Commits the channel's requests in batches until the channel completes, and completes each request that it
    /// reads. A batch that fails completes its requests with the exception, and later batches go ahead. If this method
    /// throws, it has completed every request that it read, and none left in the channel.
    /// </summary>
    public async Task RunAsync(ChannelReader<AppendRequest<TContext>> channel, CancellationToken cancellationToken)
    {
        var maxBatchSize = options.MaxBatchSize;
        var batchingDelay = options.BatchingDelay;
        var batchingDelayThreshold = options.BatchingDelayMinCount;
        // The batch grows as it fills, since the maximum batch size can be far larger than any real batch.
        var batch = new List<AppendRequest<TContext>>();

        try
        {
            while (await channel.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // Drain what's already queued
                ReadQueued(batch, channel);

                // Waiting for more requests trades latency for fewer, larger transactions.
                if (batchingDelay > TimeSpan.Zero &&
                    batch.Count >= batchingDelayThreshold &&
                    batch.Count < maxBatchSize)
                {
                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    delayCts.CancelAfter(batchingDelay);

                    try
                    {
                        while (batch.Count < maxBatchSize &&
                               await channel.WaitToReadAsync(delayCts.Token).ConfigureAwait(false))
                        {
                            ReadQueued(batch, channel);
                        }
                    }
                    catch (OperationCanceledException) when (delayCts.IsCancellationRequested)
                    {
                        // Delay elapsed - not a real cancellation, continue with what we have
                    }
                }

                if (batch.Count == 0)
                    continue;

                logger.LogTrace(
                    "Batched {BatchSize}/{MaxBatchSize} requests (~{QueueCount}) queued)",
                    batch.Count,
                    maxBatchSize,
                    channel.Count);

                try
                {
                    var start = Stopwatch.GetTimestamp();

                    await ProcessAsync(batch, channel, cancellationToken).ConfigureAwait(false);

                    var elapsed = Stopwatch.GetElapsedTime(start);

                    logger.LogTrace("Batch processed in {Elapsed:F2}ms", elapsed.TotalMilliseconds);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // Stop, rather than fail this batch and go on to the next.
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while processing a batch");
                    FaultAll(batch, ex);
                }

                batch.Clear();
            }
        }
        catch (Exception ex)
        {
            FaultAll(batch, ex);
            throw;
        }
    }

    private async Task ProcessAsync(
        List<AppendRequest<TContext>> batch,
        ChannelReader<AppendRequest<TContext>> channel,
        CancellationToken cancellationToken)
    {
        _conflictRetryCounts.Clear();
        var isPrepared = false;

        while (batch.Count > 0)
        {
            if (!isPrepared)
            {
                await PrepareCommitAsync(batch, cancellationToken).ConfigureAwait(false);

                // The policy may have completed every request.
                if (batch.Count == 0)
                    return;

                isPrepared = true;
            }

            var result = await committer.CommitAsync(_documents, cancellationToken).ConfigureAwait(false);

            switch (result)
            {
                case CommitResult.Success:
                    foreach (var request in batch)
                        request.TryComplete();
                    return;
                case CommitResult.Conflict conflict:
                    await HandleConflictAsync(batch, conflict, cancellationToken).ConfigureAwait(false);
                    isPrepared = false;
                    break;
                case CommitResult.TransientError:
                    // Before retrying, include any request that may have been queued.
                    isPrepared = ReadQueued(batch, channel) == 0;
                    break;
            }
        }
    }

    // Returns the number of requests read.
    private int ReadQueued(List<AppendRequest<TContext>> batch, ChannelReader<AppendRequest<TContext>> channel)
    {
        var count = batch.Count;

        while (batch.Count < options.MaxBatchSize && channel.TryRead(out var request))
            batch.Add(request);

        return batch.Count - count;
    }

    private async Task PrepareCommitAsync(List<AppendRequest<TContext>> batch, CancellationToken ct)
    {
        _requests.Clear();
        _documents.Clear();
        _requestByDocumentIndex.Clear();

        GroupByRequestId(batch);

        await policy.OnBatchCommittingAsync(batch, ct).ConfigureAwait(false);

        // A completed request is never written, so a conflict prepares the batch again without it.
        var count = 0;

        for (var i = 0; i < batch.Count; i++)
        {
            var request = batch[i];

            if (request.IsCompleted)
            {
                _requests.Remove(request.Id);
                continue;
            }

            batch[count++] = request;

            foreach (var document in request.Documents)
            {
                _documents.Add(document);
                _requestByDocumentIndex.Add(request);
            }
        }

        batch.RemoveRange(count, batch.Count - count);
    }

    // Keeps the first request for each ID and makes the others complete with it, so the policy sees each ID once.
    private void GroupByRequestId(List<AppendRequest<TContext>> batch)
    {
        var count = 0;

        for (var i = 0; i < batch.Count; i++)
        {
            var request = batch[i];

            if (!_requests.TryAdd(request.Id, request))
            {
                request.CompleteWith(_requests[request.Id]);
                continue;
            }

            batch[count++] = request;
        }

        batch.RemoveRange(count, batch.Count - count);
    }

    private async Task HandleConflictAsync(
        List<AppendRequest<TContext>> batch,
        CommitResult.Conflict conflict,
        CancellationToken ct)
    {
        var request = _requestByDocumentIndex[conflict.DocumentIndex];

        // Each request's documents are contiguous in the commit, so its first entry marks its first document.
        var firstDocumentIndex = _requestByDocumentIndex.IndexOf(request);

        var appendConflict = new AppendConflict<TContext>(
            request.Documents,
            request.Context,
            conflict.DocumentIndex - firstDocumentIndex,
            conflict.ErrorMessage,
            conflict.OriginatingException);

        using var scope = logger.BeginScope(new { RequestId = request.Id });
        logger.LogDebug("Duplicate key conflict detected; invoking policy");

        var resolution = policy.OnConflict(appendConflict);

        if (resolution is ConflictResolution.FailResolution fail)
        {
            logger.LogDebug("Conflict resolved by policy");

            CompleteAndRemoveFromBatch(
                fail.Exception ??
                new AppendConflictException(innerException: appendConflict.OriginatingException));

            return;
        }

        if (resolution is not ConflictResolution.RetryResolution)
            throw new UnreachableException($"Unknown conflict resolution type '{resolution.GetType()}'.");

        var retryCount = _conflictRetryCounts.GetValueOrDefault(request.Id);
        if (retryCount >= options.MaxConflictRetries)
        {
            logger.LogWarning(
                "Conflict not resolved by policy after {MaxRetries} retries; evicting request",
                options.MaxConflictRetries);

            CompleteAndRemoveFromBatch(new AppendConflictException(
                $"Conflict not resolved after {options.MaxConflictRetries} retries.",
                innerException: appendConflict.OriginatingException));
        }
        else
        {
            logger.LogDebug(
                "Conflict not resolved by policy; retrying batch in {RetryDelay} (attempt {Attempt}/{Max})",
                options.ConflictRetryDelay,
                retryCount + 1,
                options.MaxConflictRetries);

            _conflictRetryCounts[request.Id] = retryCount + 1;

            await Task.Delay(options.ConflictRetryDelay, ct).ConfigureAwait(false);
        }

        void CompleteAndRemoveFromBatch(Exception exception)
        {
            request.TryComplete(exception);
            batch.Remove(request);
        }
    }

    private static void FaultAll(List<AppendRequest<TContext>> entries, Exception exception)
    {
        foreach (var request in entries)
            request.TryComplete(exception);
    }
}