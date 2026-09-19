using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoEventStoreBuilderTests
{
    private readonly MongoEventStoreOptions _options = new() { DatabaseName = $"bld_{Guid.NewGuid():N}" };

    [TearDown]
    public Task DropDatabaseAsync() => MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task OwnedPath_InitializeAppendReadSubscribeDispose_Works(CancellationToken ct)
    {
        var store = new MongoEventStoreBuilder<object>()
            .UseConnectionString(MongoTestEnvironment.ConnectionString)
            .UseOptions(_options)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .UseLoggerFactory(MongoTestEnvironment.LoggerFactory)
            .Build();

        await using (store)
        {
            await store.EnsureInitializedAsync(ct);

            var appended = new TestEvent { Value = "owned" };
            await store.AppendAsync("s1", [AppendableEvent.Create<object>(appended)], cancellationToken: ct);

            var read = await store.ReadStream("s1").ToArrayAsync(ct);
            read.ShouldHaveSingleItem().Payload.ShouldBe(appended);

            await using var subscription = store.SubscribeToAll().GetAsyncEnumerator(ct);
            (await subscription.MoveNextAsync()).ShouldBeTrue();
            subscription.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);

            var live = new TestEvent { Value = "live" };
            await store.AppendAsync("s2", [AppendableEvent.Create<object>(live)], cancellationToken: ct);

            (await subscription.MoveNextAsync()).ShouldBeTrue();
            subscription.Current.Event.ShouldNotBeNull().Payload.ShouldBe(live);
        }

        var indexes = await (await MongoTestEnvironment.MongoClient
                .GetDatabase(_options.DatabaseName)
                .GetCollection<BsonDocument>(_options.EventLogCollectionName)
                .Indexes
                .ListAsync(ct))
            .ToListAsync(ct);

        indexes.Select(i => i["name"].AsString).ShouldContain(EventLogIndexNames.UniqueStreamVersion);
    }

    [Test]
    public async Task BorrowedPath_DisposingTheStore_LeavesTheClientUsable()
    {
        using var client = new MongoClient(MongoTestEnvironment.ConnectionString);

        var store = new MongoEventStoreBuilder<object>()
            .UseClient(client)
            .UseOptions(_options)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();

        await store.EnsureInitializedAsync();
        await store.AppendAsync("s1", [AppendableEvent.Create<object>(new TestEvent { Value = "v" })]);
        await store.DisposeAsync();

        var names = await client.ListDatabaseNamesAsync();
        (await names.ToListAsync()).ShouldContain(_options.DatabaseName);
    }

    [Test]
    public async Task Defaults_EventWithAGuidProperty_RoundTripsWorks()
    {
        await using var store = new MongoEventStoreBuilder<object>()
            .UseClient(MongoTestEnvironment.MongoClient)
            .UseOptions(_options)
            .ConfigureCodec(x => x.MapEvent<OrderPlaced>())
            .Build();

        await store.EnsureInitializedAsync();

        var appended = new OrderPlaced(Guid.NewGuid(), "bson-by-default");
        await store.AppendAsync("s1", [AppendableEvent.Create<object>(appended, [new("k", "v")])]);

        var read = (await store.ReadStream("s1").ToArrayAsync()).ShouldHaveSingleItem();
        read.Payload.ShouldBe(appended);
        read.Context.Metadata.ShouldBe(new Dictionary<string, string> { ["k"] = "v" });
    }

    private sealed record OrderPlaced(Guid OrderId, string Note);
}