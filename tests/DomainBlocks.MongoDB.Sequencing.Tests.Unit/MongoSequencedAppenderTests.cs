using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.MongoDB.Sequencing.Tests.Unit;

public class MongoSequencedAppenderTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendAsync_BatchProcessingFails_FaultsRequestAndContinues(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter();
        var failure = new InvalidOperationException();

        var policy = new CallbackAppenderPolicy<string>(
            onBatchCommitting: (batch, _) => batch.Any(x => x.Context == "failing")
                ? throw failure
                : Task.CompletedTask);

        await using var appender = CreateAppender(committer, policy);

        var exception = await appender
            .AppendAsync([FakeBatchCommitter.CreateDocument("failing")], "failing", cancellationToken: ct)
            .ShouldThrowAsync<InvalidOperationException>();

        exception.ShouldBeSameAs(failure);

        // The append loop still runs after a batch fails.
        await appender.AppendAsync([FakeBatchCommitter.CreateDocument("next")], "next", cancellationToken: ct);

        committer.CommittedValues.ShouldBe(["next"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendAsync_LargestMaxBatchSize_Commits(CancellationToken ct)
    {
        var committer = new FakeBatchCommitter();
        var options = new MongoSequencedAppenderOptions { MaxBatchSize = int.MaxValue };
        await using var appender = CreateAppender(committer, options: options);

        await appender.AppendAsync([FakeBatchCommitter.CreateDocument("value")], "context", cancellationToken: ct);

        committer.CommittedValues.ShouldBe(["value"]);
    }

    private static MongoSequencedAppender<BsonDocument, string> CreateAppender(
        IBatchCommitter batchCommitter,
        IMongoSequencedAppenderPolicy<string>? policy = null,
        MongoSequencedAppenderOptions? options = null)
    {
        return new MongoSequencedAppender<BsonDocument, string>(batchCommitter, policy, options, NullLogger.Instance);
    }
}