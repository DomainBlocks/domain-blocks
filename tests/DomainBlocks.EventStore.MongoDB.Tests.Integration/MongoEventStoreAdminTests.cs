using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoEventStoreAdminTests
{
    private readonly MongoEventStoreOptions _options = new() { DatabaseName = $"adm_{Guid.NewGuid():N}" };

    private IMongoCollection<BsonDocument> Sequences => MongoTestEnvironment.MongoClient
        .GetDatabase(_options.DatabaseName)
        .GetCollection<BsonDocument>(_options.SequencesCollectionName);

    [TearDown]
    public Task TearDown() => MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task EnsureInitializedAsync_OnNewDatabase_CreatesSequenceDocumentAtZero(CancellationToken ct)
    {
        await MongoEventStoreAdmin.EnsureInitializedAsync(MongoTestEnvironment.MongoClient, _options, ct);

        var sequence = await Sequences.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync(ct);

        sequence.ShouldBe(new BsonDocument { { "_id", MongoEventStore.SequenceId }, { "next", 0L } });
    }

    /// <summary>
    /// The document that initialization creates is the one that the appender claims positions from. If the two
    /// disagreed on its ID or its field, the appender would create another and this one would stay at zero.
    /// </summary>
    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task EnsureInitializedAsync_ThenAppends_AdvancesTheSameSequenceDocument(CancellationToken ct)
    {
        await using var store = CreateEventStore();
        await store.EnsureInitializedAsync(ct);

        object[] events = [new TestEvent { Value = "a" }, new TestEvent { Value = "b" }, new TestEvent { Value = "c" }];
        await store.AppendAsync("stream-1", events, cancellationToken: ct);

        var sequence = await Sequences.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync(ct);

        sequence.ShouldBe(new BsonDocument { { "_id", MongoEventStore.SequenceId }, { "next", 3L } });
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task EnsureInitializedAsync_WhenSequenceHasAdvanced_LeavesItUnchanged(CancellationToken ct)
    {
        await using var store = CreateEventStore();
        await store.EnsureInitializedAsync(ct);
        await store.AppendAsync("stream-1", [new TestEvent { Value = "a" }], cancellationToken: ct);

        await store.EnsureInitializedAsync(ct);

        var sequence = await Sequences.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync(ct);
        sequence["next"].ShouldBe(1L);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task EnsureInitializedAsync_CalledConcurrently_CreatesOneSequenceDocument(CancellationToken ct)
    {
        var calls = Enumerable
            .Range(0, 8)
            .Select(_ => MongoEventStoreAdmin.EnsureInitializedAsync(MongoTestEnvironment.MongoClient, _options, ct));

        await Should.NotThrowAsync(() => Task.WhenAll(calls));

        (await Sequences.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct)).ShouldBe(1);
    }

    /// <summary>
    /// Why the sequence document is created up front. Two transactions claim a position as the appender does. With the
    /// document in place, each claim is an update of it, and the second conflicts with the first and is retried. On a
    /// new database, each would create the collection for itself and both would claim the first position.
    /// </summary>
    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task EnsureInitializedAsync_ThenTwoTransactionsClaimPositions_SecondConflictsWithFirst(
        CancellationToken ct)
    {
        await MongoEventStoreAdmin.EnsureInitializedAsync(MongoTestEnvironment.MongoClient, _options, ct);

        using var first = await MongoTestEnvironment.MongoClient.StartSessionAsync(cancellationToken: ct);
        using var second = await MongoTestEnvironment.MongoClient.StartSessionAsync(cancellationToken: ct);
        first.StartTransaction();
        second.StartTransaction();

        var claimed = await ClaimPositionAsync(first, ct);

        var exception = await Should.ThrowAsync<MongoCommandException>(() => ClaimPositionAsync(second, ct));

        claimed.ShouldNotBeNull()["next"].ShouldBe(0L);
        exception.HasErrorLabel("TransientTransactionError").ShouldBeTrue();
    }

    private IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore()
    {
        return new MongoEventStoreBuilder<object>()
            .UseClient(MongoTestEnvironment.MongoClient)
            .UseOptions(_options)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();
    }

    private Task<BsonDocument?> ClaimPositionAsync(IClientSessionHandle session, CancellationToken ct)
    {
        return Sequences.FindOneAndUpdateAsync(
            session,
            Builders<BsonDocument>.Filter.Eq("_id", MongoEventStore.SequenceId),
            Builders<BsonDocument>.Update.Inc("next", 1L),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument?>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.Before
            },
            ct);
    }
}