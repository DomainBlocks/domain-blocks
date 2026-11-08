using DomainBlocks.MongoDB.Sequencing;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class AppenderPolicyTests
{
    private readonly MongoEventStoreOptions _options = new() { DatabaseName = $"pol_{Guid.NewGuid():N}" };

    [TearDown]
    public Task TearDown() => MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);

    /// <summary>
    /// Another writer creating the stream makes the batch conflict, and the appender prepares it again. By then, the
    /// request that expected the stream to exist matches, but it was rejected the first time and isn't written, so the
    /// request after it must take the next position.
    /// </summary>
    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task OnBatchCommittingAsync_PreparedAgain_SkipsCompletedRequest(CancellationToken ct)
    {
        await using var store = new MongoEventStoreBuilder<object>()
            .UseClient(MongoTestEnvironment.MongoClient)
            .UseOptions(_options)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();

        await store.EnsureInitializedAsync(ct);

        var eventLog = MongoTestEnvironment.MongoClient
            .GetDatabase(_options.DatabaseName)
            .GetCollection<BsonDocument>(_options.EventLogCollectionName);

        var policy = new AppenderPolicy(eventLog);
        const string streamId = "stream-1";
        var rejected = CreateRequest(streamId, ExpectedStreamState.Exists<StreamPosition>());
        var appended = CreateRequest(streamId, ExpectedStreamState.Any<StreamPosition>());
        AppendRequest<AppendContext>[] batch = [rejected, appended];

        await policy.OnBatchCommittingAsync(batch, ct);
        await store.AppendAsync(streamId, [new TestEvent { Value = "a" }], cancellationToken: ct);
        await policy.OnBatchCommittingAsync(batch, ct);

        appended.Documents[0][EventLogEntry.FieldNames.StreamPosition].AsInt64.ShouldBe(1);
    }

    private static AppendRequest<AppendContext> CreateRequest(
        string streamId,
        ExpectedStreamState<StreamPosition> expectedState)
    {
        var commitId = Guid.NewGuid();

        var document = new BsonDocument
        {
            { EventLogEntry.FieldNames.CommitId, new BsonBinaryData(commitId, GuidRepresentation.Standard) },
            { EventLogEntry.FieldNames.StreamId, streamId }
        };

        return new AppendRequest<AppendContext>([document], new AppendContext(commitId, streamId, expectedState));
    }
}