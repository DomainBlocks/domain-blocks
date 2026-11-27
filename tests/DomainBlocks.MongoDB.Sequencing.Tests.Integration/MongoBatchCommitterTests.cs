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
    public async Task CommitAsync_DuplicateKey_ReturnsOperationTimeAfterConflictingWrite(CancellationToken ct)
    {
        await CreateUniqueValueIndexAsync(ct);
        var committer = CreateCommitter();
        using var otherSession = await MongoClient.StartSessionAsync(cancellationToken: ct);

        await MongoClient
            .GetDatabase(DatabaseName)
            .GetCollection<BsonDocument>(TargetNs.CollectionName)
            .InsertOneAsync(otherSession, CreateDocument("taken"), cancellationToken: ct);

        var result = await committer.CommitAsync([CreateDocument("taken")], ct);

        // The conflicting write is in the transaction's snapshot, so a read after this time sees it.
        var conflict = result.ShouldBeOfType<CommitResult.Conflict>();
        conflict.OperationTime.ShouldBeGreaterThanOrEqualTo(otherSession.OperationTime);
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

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task CommitAsync_SequenceHeldByOtherTransaction_ReturnsTransientError(CancellationToken ct)
    {
        var committer = CreateCommitter();

        // Creates the sequence document, so that another transaction can hold it.
        await committer.CommitAsync([CreateDocument("first")], ct);

        using var otherSession = await MongoClient.StartSessionAsync(cancellationToken: ct);
        otherSession.StartTransaction();

        await MongoClient
            .GetDatabase(DatabaseName)
            .GetCollection<BsonDocument>(SequenceNs.CollectionName)
            .UpdateOneAsync(
                otherSession,
                Builders<BsonDocument>.Filter.Eq("_id", SequenceId),
                Builders<BsonDocument>.Update.Inc(SequenceFieldNames.Next, 1L),
                cancellationToken: ct);

        // Claiming the sequence conflicts with the other transaction's uncommitted update.
        var result = await committer.CommitAsync([CreateDocument("second")], ct);

        result.ShouldBeOfType<CommitResult.TransientError>();
        await otherSession.AbortTransactionAsync(ct);
    }

    private MongoBatchCommitter<TargetDoc> CreateCommitter()
    {
        var logger = MongoTestEnvironment.LoggerFactory.CreateLogger("committer");
        return new MongoBatchCommitter<TargetDoc>(MongoClient, CreateBinding(), logger);
    }

    // A unique value stands in for a key that a policy owns, like a stream position in the event store.
    private Task CreateUniqueValueIndexAsync(CancellationToken ct)
    {
        var uniqueValueIndex = new CreateIndexModel<TargetDoc>(
            Builders<TargetDoc>.IndexKeys.Ascending(x => x.Value),
            new CreateIndexOptions { Unique = true });

        return MongoClient
            .GetDatabase(DatabaseName)
            .GetCollection<TargetDoc>(TargetNs.CollectionName)
            .Indexes
            .CreateOneAsync(uniqueValueIndex, cancellationToken: ct);
    }

    private static BsonDocument CreateDocument(string value) => new TargetDoc { Value = value }.ToBsonDocument();
}