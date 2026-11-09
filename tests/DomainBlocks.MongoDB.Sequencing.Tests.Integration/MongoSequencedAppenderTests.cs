using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.MongoDB.Sequencing.Tests.Integration;

public class MongoSequencedAppenderTests : MongoIntegrationTestBase
{
    private const int TimeoutMillis = 10_000;

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task BatchProcessingError_WhenExceptionThrown_FaultsRequestAndContinues(CancellationToken ct)
    {
        const string errorMessage = "Simulated batch failure";
        var failed = 0;

        var policy = new CallbackAppenderPolicy<object>(
            onBatchCommitting: (_, _) => Interlocked.Exchange(ref failed, 1) == 0
                ? throw new InvalidOperationException(errorMessage)
                : Task.CompletedTask);

        await using var appender = CreateAppender(policy: policy);

        // First append should be faulted by the policy.
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => appender.AppendAsync(
            [new TargetDoc { Value = "fail" }],
            new object(),
            cancellationToken: ct));

        ex.Message.ShouldBe(errorMessage);

        // Nothing should be appended.
        var sequence = await ReadSequenceAsync(ct);
        sequence.ShouldBe([]);

        // Second append should succeed normally - worker loop must still be running.
        await appender.AppendAsync([new TargetDoc { Value = "succeed" }], new object(), cancellationToken: ct);

        sequence = await ReadSequenceAsync(ct);
        sequence.ShouldBe([0]);
    }

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task AppendAsync_LargestMaxBatchSize_Commits(CancellationToken ct)
    {
        await using var appender = CreateAppender<object>(
            options: new MongoSequencedAppenderOptions { MaxBatchSize = int.MaxValue });

        await appender.AppendAsync([new TargetDoc { Value = "e" }], new object(), cancellationToken: ct);

        (await ReadSequenceAsync(ct)).ShouldBe([0]);
    }

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task AppendAsync_ConflictRetried_PreparesOnlyPendingRequests(CancellationToken ct)
    {
        var targets = MongoClient.GetDatabase(DatabaseName).GetCollection<TargetDoc>(TargetNs.CollectionName);
        var batchHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejection = new InvalidOperationException();
        var preparedBatches = new List<string>();

        // A unique value stands in for a key the policy owns, like a stream position in the event store.
        var uniqueValue = new CreateIndexModel<TargetDoc>(
            Builders<TargetDoc>.IndexKeys.Ascending(x => x.Value),
            new CreateIndexOptions { Unique = true });

        await targets.Indexes.CreateOneAsync(uniqueValue, cancellationToken: ct);

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: async (batch, _) =>
            {
                preparedBatches.Add(string.Join(", ", batch.Select(x => x.Context)));

                switch (preparedBatches.Count)
                {
                    // Holding the first batch keeps the appender busy, so the next two appends form one batch.
                    case 1:
                        batchHeld.SetResult();
                        await releaseBatch.Task;
                        break;

                    // The policy rejects one request in the pair. The other's value is already taken, so its insert
                    // conflicts and the batch is retried.
                    case 2:
                        batch.Single(x => x.Context == "rejected").TryComplete(rejection);
                        break;

                    // On the retry, the policy gives the pending document a value of its own, so the batch commits.
                    case 3:
                        batch.Single(x => x.Context == "pending").Documents[0][nameof(TargetDoc.Value)] = "pending";
                        break;
                }
            },
            onConflict: _ => ConflictResolution.Retry);

        await using var appender = CreateAppender(policy: policy);

        var held = appender.AppendAsync([new TargetDoc { Value = "held" }], "held", cancellationToken: ct);
        await batchHeld.Task.WaitAsync(ct);
        var rejected = appender.AppendAsync([new TargetDoc { Value = "rejected" }], "rejected", cancellationToken: ct);

        // The pending document reuses the held document's value, as a stale read would reuse a stream position.
        var pending = appender.AppendAsync([new TargetDoc { Value = "held" }], "pending", cancellationToken: ct);
        releaseBatch.SetResult();

        await held;
        (await rejected.ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(rejection);
        await pending;

        // The retry left out the request that the policy had already rejected.
        preparedBatches.ShouldBe(["held", "rejected, pending", "pending"]);
    }

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task AppendAsync_RepeatedRequestId_AppendsFirstOnly(CancellationToken ct)
    {
        var batchHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparedBatches = new List<string>();

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: async (batch, _) =>
            {
                preparedBatches.Add(string.Join(", ", batch.Select(x => x.Context)));

                // Holding the first batch keeps the appender busy, so the next two appends form one batch.
                if (preparedBatches.Count == 1)
                {
                    batchHeld.SetResult();
                    await releaseBatch.Task;
                }
            });

        await using var appender = CreateAppender(policy: policy);
        var requestId = Guid.NewGuid();

        var held = AppendValueAsync(appender, "held", null, ct);
        await batchHeld.Task.WaitAsync(ct);
        var first = AppendValueAsync(appender, "first", requestId, ct);
        var repeat = AppendValueAsync(appender, "repeat", requestId, ct);
        releaseBatch.SetResult();

        await Task.WhenAll(held, first, repeat);

        preparedBatches.ShouldBe(["held", "first"]);
        (await ReadValuesAsync(ct)).ShouldBe(["held", "first"]);
    }

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task AppendAsync_FirstOfRepeatedRequestIdRejected_RejectsRepeat(CancellationToken ct)
    {
        var batchHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejection = new InvalidOperationException();
        var batchCount = 0;

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: async (batch, _) =>
            {
                switch (++batchCount)
                {
                    // Holding the first batch keeps the appender busy, so the next two appends form one batch.
                    case 1:
                        batchHeld.SetResult();
                        await releaseBatch.Task;
                        break;

                    case 2:
                        batch.Single().TryComplete(rejection);
                        break;
                }
            });

        await using var appender = CreateAppender(policy: policy);
        var requestId = Guid.NewGuid();

        var held = AppendValueAsync(appender, "held", null, ct);
        await batchHeld.Task.WaitAsync(ct);
        var first = AppendValueAsync(appender, "first", requestId, ct);
        var repeat = AppendValueAsync(appender, "repeat", requestId, ct);
        releaseBatch.SetResult();

        await held;
        (await first.ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(rejection);
        (await repeat.ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(rejection);
        (await ReadValuesAsync(ct)).ShouldBe(["held"]);
    }

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task AppendAsync_RequestIdRepeatedInLaterBatch_AppendsBoth(CancellationToken ct)
    {
        // The appender doesn't make appends idempotent: it only groups requests with the same ID in one batch, so a
        // repeat in a later batch is appended again. Making appends idempotent is up to the policy.
        var preparedBatches = new List<string>();

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: (batch, _) =>
            {
                preparedBatches.Add(string.Join(", ", batch.Select(x => x.Context)));
                return Task.CompletedTask;
            });

        await using var appender = CreateAppender(policy: policy);
        var requestId = Guid.NewGuid();

        await AppendValueAsync(appender, "first", requestId, ct);
        await AppendValueAsync(appender, "repeat", requestId, ct);

        preparedBatches.ShouldBe(["first", "repeat"]);
        (await ReadValuesAsync(ct)).ShouldBe(["first", "repeat"]);
    }

    // Uses the value as the context too, so a test can tell which requests reached the policy.
    private static Task AppendValueAsync(
        MongoSequencedAppender<TargetDoc, string> appender,
        string value,
        Guid? requestId,
        CancellationToken ct)
    {
        return appender.AppendAsync([new TargetDoc { Value = value }], value, requestId, cancellationToken: ct);
    }

    private async Task<string[]> ReadValuesAsync(CancellationToken ct)
    {
        var docs = await MongoClient
            .GetDatabase(DatabaseName)
            .GetCollection<TargetDoc>(TargetNs.CollectionName)
            .Find(FilterDefinition<TargetDoc>.Empty)
            .Sort(Builders<TargetDoc>.Sort.Ascending(x => x.Nested!.Sequence))
            .ToListAsync(ct);

        return [.. docs.Select(x => x.Value)];
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