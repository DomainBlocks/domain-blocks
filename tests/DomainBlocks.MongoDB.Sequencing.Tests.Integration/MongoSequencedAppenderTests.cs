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

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: async (batch, token) =>
            {
                preparedBatches.Add(string.Join(", ", batch.Select(x => x.Context)));

                switch (preparedBatches.Count)
                {
                    // Holding the first batch keeps the appender busy, so the next two appends form one batch.
                    case 1:
                        batchHeld.SetResult();
                        await releaseBatch.Task;
                        break;

                    // The policy rejects one request in the pair. Another writer then takes the next sequence number,
                    // so the pending request's insert conflicts and the batch is retried.
                    case 2:
                        batch.Single(x => x.Context == "rejected").TryComplete(rejection);
                        var other = new TargetDoc { Value = "other", Nested = new NestedDoc { Sequence = 1 } };
                        await targets.InsertOneAsync(other, cancellationToken: token);
                        break;

                    // The retry claims the same sequence number, so it commits only once the other document is gone.
                    case 3:
                        await targets.DeleteOneAsync(x => x.Value == "other", token);
                        break;
                }
            },
            onConflict: _ => ConflictResolution.Retry);

        await using var appender = CreateAppender(policy: policy);

        var held = appender.AppendAsync([new TargetDoc { Value = "held" }], "held", cancellationToken: ct);
        await batchHeld.Task.WaitAsync(ct);
        var rejected = appender.AppendAsync([new TargetDoc { Value = "rejected" }], "rejected", cancellationToken: ct);
        var pending = appender.AppendAsync([new TargetDoc { Value = "pending" }], "pending", cancellationToken: ct);
        releaseBatch.SetResult();

        await held;
        (await rejected.ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(rejection);
        await pending;

        // The retry left out the request that the policy had already rejected.
        preparedBatches.ShouldBe(["held", "rejected, pending", "pending"]);
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