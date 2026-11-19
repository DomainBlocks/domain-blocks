using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Commits batches of append requests, preparing each batch with the policy and resolving its conflicts.
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
    /// Commits the batch and completes its requests. Removes from the batch the requests that complete before the
    /// commit, and the repeats of a request ID, which complete with the first. If this method throws, failing the
    /// requests left in the batch completes every request that hasn't completed.
    /// </summary>
    public async Task ProcessAsync(List<AppendRequest<TContext>> batch, CancellationToken cancellationToken)
    {
        _conflictRetryCounts.Clear();

        while (batch.Count > 0)
        {
            await PrepareCommitAsync(batch, cancellationToken).ConfigureAwait(false);

            // The policy may have completed every request.
            if (batch.Count == 0)
                return;

            var result = await committer.CommitAsync(_documents, cancellationToken).ConfigureAwait(false);

            switch (result)
            {
                case CommitResult.Success:
                    foreach (var request in batch)
                        request.TryComplete();
                    return;
                case CommitResult.Conflict conflict:
                    await HandleConflictAsync(batch, conflict, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
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
}