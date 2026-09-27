using DomainBlocks.EventStore.Filtering;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// The subscription filter of a store is evaluated by the server, for every subscription of the store.
/// </summary>
public class MongoSubscriptionFilterTests
{
    private static readonly EventFilter Orders =
        EventFilter.StreamIdStartsWith("order-") & !EventFilter.Metadata("tenant", "initech");

    private readonly MongoEventStoreOptions _options = new()
    {
        DatabaseName = $"sub_{Guid.NewGuid():N}",
        SubscriptionFilter = Orders
    };

    private IEventStore<object, string, StreamPosition, LogPosition> _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _store = CreateStore(_options);
        await _store.EnsureInitializedAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _store.DisposeAsync();
        await MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ChangeStream_WhenWatchedWithThePipelineOfTheStore_LeavesOutWhatTheFilterDoes(CancellationToken ct)
    {
        // Not through the store, which would filter again: this is what the server sends.
        var eventLog = MongoTestEnvironment.MongoClient
            .GetDatabase(_options.DatabaseName)
            .GetCollection<BsonDocument>(_options.EventLogCollectionName);

        var pipeline = MongoFilterTranslator.ToChangeStreamPipeline(Orders);
        using var cursor = await eventLog.WatchAsync(pipeline, cancellationToken: ct);

        await AppendAsync("invoice-1", "passed over", "acme", ct);
        await AppendAsync("order-1", "passed over too", "initech", ct);
        await AppendAsync("order-1", "selected", "acme", ct);
        await AppendAsync("order-2", "selected without a tenant", null, ct);

        var sent = new List<string>();

        while (sent.Count < 2 && await cursor.MoveNextAsync(ct))
            sent.AddRange(cursor.Current.Select(x => x.FullDocument["eventData"]["Value"].AsString));

        sent.ShouldBe(["selected", "selected without a tenant"]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Subscriptions_WhenTheStoreHasAFilter_AreSubjectToItWhileCatchingUpAndLive(CancellationToken ct)
    {
        await AppendAsync("invoice-1", "old invoice", "acme", ct);
        await AppendAsync("order-1", "old order", "acme", ct);

        await using var all = _store.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);

        await using var invoices = _store
            .SubscribeToStream("invoice-1", SubscriptionOrigin.Start)
            .GetAsyncEnumerator(ct);

        (await ReadUntilCaughtUpAsync(all)).ShouldBe(["old order"]);
        (await ReadUntilCaughtUpAsync(invoices)).ShouldBeEmpty();

        await AppendAsync("invoice-1", "new invoice", "acme", ct);
        await AppendAsync("order-1", "new order", "acme", ct);

        (await all.MoveNextAsync()).ShouldBeTrue();
        ValueOf(all.Current).ShouldBe("new order");
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Subscriptions_WhenGivenAFilterOfTheirOwn_AreSubjectToBoth(CancellationToken ct)
    {
        await AppendAsync("order-1", "one", "acme", ct);
        await AppendAsync("order-2", "two", "acme", ct);
        await AppendAsync("invoice-2", "three", "acme", ct);

        var options = new SubscriptionOptions { Filter = EventFilter.StreamIds("order-2", "invoice-2") };
        await using var subscription = _store.SubscribeToAll(SubscriptionOrigin.Start, options).GetAsyncEnumerator(ct);

        (await ReadUntilCaughtUpAsync(subscription)).ShouldBe(["two"]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Reads_WhenTheStoreHasASubscriptionFilter_AreNotSubjectToIt(CancellationToken ct)
    {
        await AppendAsync("invoice-1", "invoice", "acme", ct);
        await AppendAsync("order-1", "order", "acme", ct);

        var read = await _store.ReadAll().ToArrayAsync(ct);

        read.Select(x => ((TestEvent)x.Payload).Value).ShouldBe(["invoice", "order"]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Subscriptions_WhenTheOptionsAreSetAgainAfterTheStoreIsBuilt_GoByTheFilterItWasBuiltWith(
        CancellationToken ct)
    {
        await AppendAsync("invoice-1", "invoice", "acme", ct);
        await AppendAsync("order-1", "order", "acme", ct);

        _options.SubscriptionFilter = EventFilter.All;

        try
        {
            // The change stream of the store still leaves invoices out, so catching up has to as well.
            await using var all = _store.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);

            (await ReadUntilCaughtUpAsync(all)).ShouldBe(["order"]);
        }
        finally
        {
            _options.SubscriptionFilter = Orders;
        }
    }

    [Test]
    public void Build_WhenTheSubscriptionFilterIsByType_Refuses()
    {
        // As every store does: the filter of a store is about what is stored beside the payload.
        EventFilter[] byType = [EventFilter.OfType<TestEvent>(), EventFilter.OfType<TestEvent>(e => e.Value.Length > 3)];

        foreach (var filter in byType)
        {
            var options = new MongoEventStoreOptions
            {
                DatabaseName = _options.DatabaseName,
                SubscriptionFilter = EventFilter.StreamIdStartsWith("order-") & filter
            };

            Should.Throw<EventFilterNotSupportedException>(() => CreateStore(options));
        }
    }

    [Test]
    public void Build_WhenTheServerCannotEvaluateTheWholeSubscriptionFilter_Refuses()
    {
        // A key with a dot in it would be taken for a path, so it is left to be tested in process.
        var options = new MongoEventStoreOptions
        {
            DatabaseName = _options.DatabaseName,
            SubscriptionFilter = EventFilter.Metadata("a.b", "x")
        };

        Should.Throw<EventFilterNotSupportedException>(() => CreateStore(options));
    }

    private static IEventStore<object, string, StreamPosition, LogPosition> CreateStore(MongoEventStoreOptions options)
    {
        return new MongoEventStoreBuilder<object>()
            .UseClient(MongoTestEnvironment.MongoClient)
            .UseOptions(options)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .UseLoggerFactory(MongoTestEnvironment.LoggerFactory)
            .Build();
    }

    private Task AppendAsync(string streamId, string value, string? tenant, CancellationToken ct)
    {
        KeyValuePair<string, string>[] metadata = tenant is null ? [] : [new("tenant", tenant)];
        var e = AppendableEvent.Create<object>(new TestEvent { Value = value }, metadata);

        return _store.AppendAsync(streamId, [e], cancellationToken: ct);
    }

    private static async Task<List<string>> ReadUntilCaughtUpAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> subscription)
    {
        var values = new List<string>();

        while (await subscription.MoveNextAsync() && !subscription.Current.IsCaughtUp)
        {
            if (subscription.Current.Event is not null)
                values.Add(ValueOf(subscription.Current));
        }

        return values;
    }

    private static string ValueOf(SubscriptionMessage<object, string, StreamPosition, LogPosition> message) =>
        ((TestEvent)message.Event!.Value.Payload).Value;
}