using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.MongoDB.Sequencing.Tests.Unit;

public class BatchProcessorTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ProcessAsync_ConflictRetried_PreparesOnlyPendingRequests(CancellationToken ct)
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

        var processor = CreateProcessor(committer, policy);
        var rejected = CreateRequest("rejected");
        var pending = CreateRequest("pending", value: "taken");

        await processor.ProcessAsync([rejected, pending], ct);

        var exception = await rejected.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>();
        exception.ShouldBeSameAs(rejection);
        await pending.Completion.WaitAsync(ct);

        // The retry left out the request that the policy had already rejected.
        preparedBatches.ShouldBe(["rejected, pending", "pending"]);
        committer.CommittedValues.ShouldBe(["taken", "pending"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ProcessAsync_ConflictRetriesExhausted_FailsConflictingRequest(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter(committedValues: ["taken"]);
        var options = new MongoSequencedAppenderOptions { MaxConflictRetries = 2, ConflictRetryDelay = TimeSpan.Zero };
        var processor = CreateProcessor(committer, DefaultSequencedAppenderPolicy<string>.Shared, options);
        var conflicting = CreateRequest("taken");
        var other = CreateRequest("other");

        await processor.ProcessAsync([conflicting, other], ct);

        var exception = await conflicting.Completion.WaitAsync(ct).ShouldThrowAsync<AppendConflictException>();
        exception.Message.ShouldBe("Conflict not resolved after 2 retries.");
        await other.Completion.WaitAsync(ct);
        committer.CommitAttempts.ShouldBe(["taken, other", "taken, other", "taken, other", "other"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ProcessAsync_ConflictFailedByPolicy_FailsConflictingRequestOnly(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter(committedValues: ["taken"]);
        var rejection = new InvalidOperationException();
        var policy = new CallbackAppenderPolicy<string>(onConflict: _ => ConflictResolution.Fail(rejection));
        var processor = CreateProcessor(committer, policy);
        var other = CreateRequest("other");
        var conflicting = CreateRequest("taken");

        await processor.ProcessAsync([other, conflicting], ct);

        await other.Completion.WaitAsync(ct);
        var exception = await conflicting.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>();
        exception.ShouldBeSameAs(rejection);
        committer.CommitAttempts.ShouldBe(["other, taken", "other"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ProcessAsync_ConflictFailedWithoutException_ThrowsAppendConflictException(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter(committedValues: ["taken"]);
        var policy = new CallbackAppenderPolicy<string>(onConflict: _ => ConflictResolution.Fail());
        var processor = CreateProcessor(committer, policy);
        var conflicting = CreateRequest("taken");

        await processor.ProcessAsync([conflicting], ct);

        var exception = await conflicting.Completion.WaitAsync(ct).ShouldThrowAsync<AppendConflictException>();
        exception.InnerException.ShouldNotBeNull().Message.ShouldBe("Duplicate value 'taken'.");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ProcessAsync_RepeatedRequestId_CommitsFirstOnly(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter();
        var preparedBatches = new List<string>();

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: (batch, _) =>
            {
                preparedBatches.Add(string.Join(", ", batch.Select(x => x.Context)));
                return Task.CompletedTask;
            });

        var processor = CreateProcessor(committer, policy);
        var requestId = Guid.NewGuid();
        var first = CreateRequest("first", requestId: requestId);
        var repeat = CreateRequest("repeat", requestId: requestId);

        await processor.ProcessAsync([first, repeat], ct);

        await Task.WhenAll(first.Completion, repeat.Completion).WaitAsync(ct);
        preparedBatches.ShouldBe(["first"]);
        committer.CommittedValues.ShouldBe(["first"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ProcessAsync_FirstOfRepeatedRequestIdRejected_RejectsRepeat(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter();
        var rejection = new InvalidOperationException();

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: (batch, _) =>
            {
                batch.Single().TryComplete(rejection);
                return Task.CompletedTask;
            });

        var processor = CreateProcessor(committer, policy);
        var requestId = Guid.NewGuid();
        var first = CreateRequest("first", requestId: requestId);
        var repeat = CreateRequest("repeat", requestId: requestId);

        await processor.ProcessAsync([first, repeat], ct);

        (await first.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(rejection);
        (await repeat.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(rejection);
        committer.CommitAttempts.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ProcessAsync_RequestIdRepeatedInLaterBatch_CommitsBoth(CancellationToken ct)
    {
        // The processor doesn't make appends idempotent: it only groups requests with the same ID in one batch, so a
        // repeat in a later batch is committed again. Making appends idempotent is up to the policy.
        var committer = new FakeBatchCommitter();
        var processor = CreateProcessor(committer, DefaultSequencedAppenderPolicy<string>.Shared);
        var requestId = Guid.NewGuid();
        var first = CreateRequest("first", requestId: requestId);
        var repeat = CreateRequest("repeat", requestId: requestId);

        await processor.ProcessAsync([first], ct);
        await processor.ProcessAsync([repeat], ct);

        await Task.WhenAll(first.Completion, repeat.Completion).WaitAsync(ct);
        committer.CommittedValues.ShouldBe(["first", "repeat"]);
    }

    private static BatchProcessor<string> CreateProcessor(
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

    private class CallbackAppenderPolicy<TContext>(
        Func<IReadOnlyList<AppendRequest<TContext>>, CancellationToken, Task>? onBatchCommitting = null,
        Func<AppendConflict<TContext>, ConflictResolution>? onConflict = null) :
        IMongoSequencedAppenderPolicy<TContext>
    {
        public Task OnBatchCommittingAsync(IReadOnlyList<AppendRequest<TContext>> batch, CancellationToken ct)
        {
            return onBatchCommitting?.Invoke(batch, ct) ?? Task.CompletedTask;
        }

        public ConflictResolution OnConflict(AppendConflict<TContext> conflict)
        {
            return onConflict?.Invoke(conflict) ?? ConflictResolution.Fail();
        }
    }
}