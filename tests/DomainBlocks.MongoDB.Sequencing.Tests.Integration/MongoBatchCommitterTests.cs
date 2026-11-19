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

        // Succeeds, since no document has the value yet.
        await committer.CommitAsync([CreateDocument("taken")], ct);

        // Conflicts at the second document, since the first commit took its value.
        var result = await committer.CommitAsync([CreateDocument("new"), CreateDocument("taken")], ct);

        result.ShouldBeOfType<CommitResult.Conflict>().DocumentIndex.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task CommitAsync_DuplicateKey_RollsBackSequenceClaim(CancellationToken ct)
    {
        await CreateUniqueValueIndexAsync(ct);
        var committer = CreateCommitter();

        // Succeeds with sequence number 0, since no document has the value yet.
        await committer.CommitAsync([CreateDocument("taken")], ct);

        // Conflicts at the second document, since the first commit took its value.
        await committer.CommitAsync([CreateDocument("new"), CreateDocument("taken")], ct);

        // Succeeds with sequence number 1, since the conflicting commit wrote nothing and claimed no sequence numbers.
        var result = await committer.CommitAsync([CreateDocument("new")], ct);

        result.ShouldBeOfType<CommitResult.Success>();
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