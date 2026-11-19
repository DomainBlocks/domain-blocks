using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.MongoDB.Sequencing.Tests.Unit;

public class BatchProcessorTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task RunAsync_ConflictRetried_PreparesOnlyPendingRequests(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter(committedValues: ["taken"]);
        var rejection = new InvalidOperationException();
        var preparedBatches = new List<string>();

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: (batch, _) =>
            {
                preparedBatches.Add(string.Join(", ", batch.Select(x => x.Context)));

                switch (preparedBatches.Count)
                {
                    // The policy rejects one request in the pair. The other's value is already taken, so its commit
                    // conflicts and the batch is retried.
                    case 1:
                        batch.Single(x => x.Context == "rejected").TryComplete(rejection);
                        break;

                    // On the retry, the policy gives the pending document a value of its own, so the batch commits.
                    case 2:
                        var pendingDocument = batch.Single(x => x.Context == "pending").Documents[0];
                        pendingDocument[FakeBatchCommitter.ValueFieldName] = "pending";
                        break;
                }

                return Task.CompletedTask;
            },
            onConflict: _ => ConflictResolution.Retry);

        var batchProcessor = CreateBatchProcessor(committer, policy);
        var rejectedRequest = CreateRequest("rejected");
        var pendingRequest = CreateRequest("pending", value: "taken");

        await RunOneBatchAsync(batchProcessor, [rejectedRequest, pendingRequest], ct);

        var exception = await rejectedRequest.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>();
        exception.ShouldBeSameAs(rejection);
        await pendingRequest.Completion.WaitAsync(ct);

        // The retry left out the request that the policy had already rejected.
        preparedBatches.ShouldBe(["rejected, pending", "pending"]);
        committer.CommittedValues.ShouldBe(["taken", "pending"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task RunAsync_ConflictRetriesExhausted_FailsConflictingRequest(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter(committedValues: ["taken"]);
        var options = new MongoSequencedAppenderOptions { MaxConflictRetries = 2, ConflictRetryDelay = TimeSpan.Zero };
        var batchProcessor = CreateBatchProcessor(committer, DefaultSequencedAppenderPolicy<string>.Shared, options);
        var conflictingRequest = CreateRequest("taken");
        var otherRequest = CreateRequest("other");

        await RunOneBatchAsync(batchProcessor, [conflictingRequest, otherRequest], ct);

        var exception = await conflictingRequest.Completion.WaitAsync(ct).ShouldThrowAsync<AppendConflictException>();
        exception.Message.ShouldBe("Conflict not resolved after 2 retries.");
        await otherRequest.Completion.WaitAsync(ct);
        committer.CommitAttempts.ShouldBe(["taken, other", "taken, other", "taken, other", "other"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task RunAsync_ConflictFailedByPolicy_FailsConflictingRequestOnly(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter(committedValues: ["taken"]);
        var rejection = new InvalidOperationException();
        var policy = new CallbackAppenderPolicy<string>(onConflict: _ => ConflictResolution.Fail(rejection));
        var batchProcessor = CreateBatchProcessor(committer, policy);
        var otherRequest = CreateRequest("other");
        var conflictingRequest = CreateRequest("taken");

        await RunOneBatchAsync(batchProcessor, [otherRequest, conflictingRequest], ct);

        await otherRequest.Completion.WaitAsync(ct);
        var exception = await conflictingRequest.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>();
        exception.ShouldBeSameAs(rejection);
        committer.CommitAttempts.ShouldBe(["other, taken", "other"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task RunAsync_ConflictFailedWithoutException_ThrowsAppendConflictException(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter(committedValues: ["taken"]);
        var policy = new CallbackAppenderPolicy<string>(onConflict: _ => ConflictResolution.Fail());
        var batchProcessor = CreateBatchProcessor(committer, policy);
        var conflictingRequest = CreateRequest("taken");

        await RunOneBatchAsync(batchProcessor, [conflictingRequest], ct);

        var exception = await conflictingRequest.Completion.WaitAsync(ct).ShouldThrowAsync<AppendConflictException>();
        exception.InnerException.ShouldNotBeNull().Message.ShouldBe("Duplicate value 'taken'.");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task RunAsync_ConflictInLaterRequest_ReportsIndexWithinRequest(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter(committedValues: ["taken"]);
        var conflicts = new List<AppendConflict<string>>();

        var policy = new CallbackAppenderPolicy<string>(
            onConflict: conflict =>
            {
                conflicts.Add(conflict);
                return ConflictResolution.Fail();
            });

        var batchProcessor = CreateBatchProcessor(committer, policy);
        var otherRequest = CreateRequest("other");

        var conflictingRequest = new AppendRequest<string>(
            [FakeBatchCommitter.CreateDocument("new"), FakeBatchCommitter.CreateDocument("taken")],
            "conflicting");

        await RunOneBatchAsync(batchProcessor, [otherRequest, conflictingRequest], ct);

        // The commit's third document is the conflicting request's second.
        conflicts.ShouldHaveSingleItem().DocumentIndex.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task RunAsync_RepeatedRequestId_CommitsFirstOnly(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter();
        var preparedBatches = new List<string>();

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: (batch, _) =>
            {
                preparedBatches.Add(string.Join(", ", batch.Select(x => x.Context)));
                return Task.CompletedTask;
            });

        var batchProcessor = CreateBatchProcessor(committer, policy);
        var requestId = Guid.NewGuid();
        var firstRequest = CreateRequest("first", requestId: requestId);
        var repeatedRequest = CreateRequest("repeat", requestId: requestId);

        await RunOneBatchAsync(batchProcessor, [firstRequest, repeatedRequest], ct);

        await Task.WhenAll(firstRequest.Completion, repeatedRequest.Completion).WaitAsync(ct);
        preparedBatches.ShouldBe(["first"]);
        committer.CommittedValues.ShouldBe(["first"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task RunAsync_FirstOfRepeatedRequestIdRejected_RejectsRepeat(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter();
        var rejection = new InvalidOperationException();

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: (batch, _) =>
            {
                batch.Single().TryComplete(rejection);
                return Task.CompletedTask;
            });

        var batchProcessor = CreateBatchProcessor(committer, policy);
        var requestId = Guid.NewGuid();
        var firstRequest = CreateRequest("first", requestId: requestId);
        var repeatedRequest = CreateRequest("repeat", requestId: requestId);

        await RunOneBatchAsync(batchProcessor, [firstRequest, repeatedRequest], ct);

        var firstException = await firstRequest.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>();
        firstException.ShouldBeSameAs(rejection);

        var repeatException =
            await repeatedRequest.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>();
        repeatException.ShouldBeSameAs(rejection);
        committer.CommitAttempts.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task RunAsync_RequestIdRepeatedInLaterBatch_CommitsBoth(CancellationToken ct)
    {
        // The batch processor doesn't make appends idempotent: it only groups requests with the same ID in one batch,
        // so a repeat in a later batch is committed again. Making appends idempotent is up to the policy.
        var committer = new FakeBatchCommitter();
        var batchProcessor = CreateBatchProcessor(committer, DefaultSequencedAppenderPolicy<string>.Shared);
        var requestId = Guid.NewGuid();
        var firstRequest = CreateRequest("first", requestId: requestId);
        var repeatedRequest = CreateRequest("repeat", requestId: requestId);

        var channel = Channel.CreateUnbounded<AppendRequest<string>>();
        var run = batchProcessor.RunAsync(channel.Reader, ct);

        // The first request commits before the repeat is queued, so the two are in separate batches.
        channel.Writer.TryWrite(firstRequest).ShouldBeTrue();
        await firstRequest.Completion.WaitAsync(ct);
        channel.Writer.TryWrite(repeatedRequest).ShouldBeTrue();
        channel.Writer.Complete();
        await run;

        await repeatedRequest.Completion.WaitAsync(ct);
        committer.CommittedValues.ShouldBe(["first", "repeat"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task RunAsync_CanceledDuringCommit_FailsReadRequestsOnly(CancellationToken ct)
    {
        var committing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: async (_, token) =>
            {
                committing.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            });

        var batchProcessor = CreateBatchProcessor(new FakeBatchCommitter(), policy);
        var channel = Channel.CreateUnbounded<AppendRequest<string>>();
        var readRequest = CreateRequest("read");
        var queuedRequest = CreateRequest("queued");
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        channel.Writer.TryWrite(readRequest).ShouldBeTrue();
        var run = batchProcessor.RunAsync(channel.Reader, stopCts.Token);

        // The processor has read the first request and is committing it when the second is queued.
        await committing.Task.WaitAsync(ct);
        channel.Writer.TryWrite(queuedRequest).ShouldBeTrue();
        await stopCts.CancelAsync();

        await run.ShouldThrowAsync<OperationCanceledException>();
        readRequest.IsCompleted.ShouldBeTrue();
        await readRequest.Completion.ShouldThrowAsync<OperationCanceledException>();
        queuedRequest.IsCompleted.ShouldBeFalse();
    }

    // Queues the requests before the processor runs, so that it reads them as one batch, and completes the channel, so
    // that the processor returns after committing them.
    private static async Task RunOneBatchAsync(
        BatchProcessor<string> batchProcessor,
        AppendRequest<string>[] requests,
        CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<AppendRequest<string>>();

        foreach (var request in requests)
            channel.Writer.TryWrite(request).ShouldBeTrue();

        channel.Writer.Complete();
        await batchProcessor.RunAsync(channel.Reader, ct);
    }

    private static BatchProcessor<string> CreateBatchProcessor(
        IBatchCommitter committer,
        IMongoSequencedAppenderPolicy<string> policy,
        MongoSequencedAppenderOptions? options = null)
    {
        options ??= new MongoSequencedAppenderOptions { ConflictRetryDelay = TimeSpan.Zero };
        return new BatchProcessor<string>(committer, policy, options, NullLogger.Instance);
    }

    // The context names the request, so a test can tell which requests reached the policy. The document's value is the
    // context unless a test gives it one.
    private static AppendRequest<string> CreateRequest(string context, string? value = null, Guid? requestId = null)
    {
        return new AppendRequest<string>([FakeBatchCommitter.CreateDocument(value ?? context)], context, requestId);
    }
}