using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.MongoDB.Sequencing.Tests.Integration;

public class MongoBatchCommitterTests : MongoIntegrationTestBase
{
    private const int TimeoutMillis = 10_000;

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task CommitAsync_DuplicateKey_ReturnsConflictingDocumentIndex(CancellationToken ct)
    {
        await CreateUniqueValueIndexAsync(ct);
        var committer = CreateCommitter();
        await committer.CommitAsync([CreateDocument("taken")], ct);

        var result = await committer.CommitAsync([CreateDocument("new"), CreateDocument("taken")], ct);

        result.ShouldBeOfType<CommitResult.Conflict>().DocumentIndex.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task CommitAsync_DuplicateKey_RollsBackSequenceClaim(CancellationToken ct)
    {
        await CreateUniqueValueIndexAsync(ct);
        var committer = CreateCommitter();
        await committer.CommitAsync([CreateDocument("taken")], ct);
        await committer.CommitAsync([CreateDocument("new"), CreateDocument("taken")], ct);

        await committer.CommitAsync([CreateDocument("new")], ct);

        // The conflicting commit wrote nothing and claimed no sequence numbers.
        (await ReadSequenceAsync(ct)).ShouldBe([0, 1]);
    }

    private MongoBatchCommitter<TargetDoc> CreateCommitter()
    {
        var logger = MongoTestEnvironment.LoggerFactory.CreateLogger("committer");
        return new MongoBatchCommitter<TargetDoc>(MongoClient, CreateBinding(), logger);
    }

    // A unique value stands in for a key that a policy owns, like a stream position in the event store.
    private Task CreateUniqueValueIndexAsync(CancellationToken ct)
    {
        var uniqueValue = new CreateIndexModel<TargetDoc>(
            Builders<TargetDoc>.IndexKeys.Ascending(x => x.Value),
            new CreateIndexOptions { Unique = true });

        return MongoClient
            .GetDatabase(DatabaseName)
            .GetCollection<TargetDoc>(TargetNs.CollectionName)
            .Indexes
            .CreateOneAsync(uniqueValue, cancellationToken: ct);
    }

    private static BsonDocument CreateDocument(string value) => new TargetDoc { Value = value }.ToBsonDocument();
}