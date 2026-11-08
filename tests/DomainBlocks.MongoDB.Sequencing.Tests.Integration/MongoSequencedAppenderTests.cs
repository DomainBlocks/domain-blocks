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
        var firstBatchHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batches = new List<string>();
        var rejection = new InvalidOperationException();

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: async (batch, token) =>
            {
                batches.Add(string.Join(' ', batch.Select(x => x.Context)));

                switch (batches.Count)
                {
                    case 1:
                        firstBatchHeld.SetResult();
                        await releaseFirstBatch.Task;
                        break;
                    case 2:
                        batch.Single(x => x.Context == "rejected").TryComplete(rejection);
                        break;
                    default:
                        // Removing the other writer's document lets the retry commit.
                        await targets.DeleteOneAsync(x => x.Value == "other", token);
                        break;
                }
            },
            onConflict: _ => ConflictResolution.Retry);

        await using var appender = CreateAppender(policy: policy);

        // While the policy holds the first batch, the next two appends queue up and form the second batch together.
        var first = appender.AppendAsync([new TargetDoc { Value = "first" }], "first", cancellationToken: ct);
        await firstBatchHeld.Task.WaitAsync(ct);
        var rejected = appender.AppendAsync([new TargetDoc { Value = "rejected" }], "rejected", cancellationToken: ct);
        var pending = appender.AppendAsync([new TargetDoc { Value = "pending" }], "pending", cancellationToken: ct);

        // Another writer takes the second batch's sequence number, so that batch conflicts and is retried.
        var other = new TargetDoc { Value = "other", Nested = new NestedDoc { Sequence = 1 } };
        await targets.InsertOneAsync(other, cancellationToken: ct);
        releaseFirstBatch.SetResult();

        await first;
        (await rejected.ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(rejection);
        await pending;

        batches.ShouldBe(["first", "rejected pending", "pending"]);
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