using System.Threading.Channels;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// The subscription filter of a store is the row filter of its publication, so it is the server that leaves events
/// out of the live feed.
/// </summary>
[TestFixture]
public class PostgresSubscriptionFilterTests() : PostgresIntegrationTest(x => x.SubscriptionFilter = Orders)
{
    private static readonly EventFilter Orders =
        EventFilter.StreamIdStartsWith("order-") &
        !EventFilter.Metadata("tenant", "o'brien", @"back\slash") &
        EventFilter.EventNames(nameof(TestEvent)) &
        EventFilter.CreatedAtOrAfter(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private IEventStore<object, string, StreamPosition, LogPosition> _store = null!;

    [SetUp]
    public void SetUp() => _store = CreateEventStore();

    [TearDown]
    public ValueTask TearDown() => _store.DisposeAsync();

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Feed_WhenReadWithoutTheStore_IsSentOnlyWhatTheRowFilterSelects(CancellationToken ct)
    {
        // Not through a subscription, which would filter again: this is what the server sends.
        var names = new SchemaObjectNames(Schema, Orders);
        var observer = new CollectingObserver();

        var feed = new EventLogFeed<EventLogRow<string>>(
            token => ReplicationEventLogSession.OpenAsync(
                PostgresTestEnvironment.ConnectionString,
                $"dbx_subfilter_{Guid.NewGuid():N}"[..40],
                names,
                Options.Replication,
                ValueDecoder.Instance,
                LoggerFactory.CreateLogger("ReplicationEventLogSession"),
                token));

        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        await AppendAsync("invoice-1", "another stream", "acme", ct);
        await AppendAsync("order-1", "another tenant", "o'brien", ct);
        await AppendAsync("order-1", "a tenant that is written as an escape string", @"back\slash", ct);
        await AppendAsync("order-1", "selected", "acme", ct);
        await AppendAsync("order-2", "selected without a tenant", null, ct);

        var sent = new List<string>();

        while (sent.Count < 2)
            sent.Add(await observer.Reader.ReadAsync(ct));

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
    public async Task Subscriptions_WhenTheOptionsAreSetAgainAfterTheStoreIsBuilt_GoByTheFilterItWasBuiltWith(
        CancellationToken ct)
    {
        await AppendAsync("invoice-1", "invoice", "acme", ct);
        await AppendAsync("order-1", "order", "acme", ct);

        Options.SubscriptionFilter = EventFilter.All;

        try
        {
            // The publication of the store still leaves invoices out, so catching up has to as well.
            await using var all = _store.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);

            (await ReadUntilCaughtUpAsync(all)).ShouldBe(["order"]);
        }
        finally
        {
            Options.SubscriptionFilter = Orders;
        }
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
    public async Task EnsureInitialized_WhenThereIsAFilter_NamesThePublicationAfterItAndSaysWhatItIsFor(
        CancellationToken ct)
    {
        var names = new SchemaObjectNames(Schema, Orders);

        names.Publication.ShouldStartWith($"{Schema}_event_log_pub_");
        (await DescribePublicationAsync(names.Publication, ct)).ShouldBe(Orders.ToString());

        // Again, and nothing changes. Other tests add publications of their own to the schema.
        var before = await GetPublicationsAsync(ct);
        await PostgresEventStoreAdmin.EnsureInitializedAsync(DataSource, Options, cancellationToken: ct);

        before.ShouldContain(names.Publication);
        (await GetPublicationsAsync(ct)).ShouldBe(before);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task EnsureInitialized_WhenAnotherStoreHasAnotherFilter_GivesEachItsOwnPublication(CancellationToken ct)
    {
        var other = new PostgresEventStoreOptions
        {
            Schema = Schema,
            SubscriptionFilter = EventFilter.StreamIdStartsWith("invoice-")
        };

        await PostgresEventStoreAdmin.EnsureInitializedAsync(DataSource, other, cancellationToken: ct);

        var publications = await GetPublicationsAsync(ct);

        publications.ShouldContain(new SchemaObjectNames(Schema, Orders).Publication);
        publications.ShouldContain(new SchemaObjectNames(Schema, other.SubscriptionFilter).Publication);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task EnsureInitialized_WhenThePublicationIsForAnotherFilter_Throws(CancellationToken ct)
    {
        var filter = EventFilter.StreamId("collides");
        var options = new PostgresEventStoreOptions { Schema = Schema, SubscriptionFilter = filter };
        var names = new SchemaObjectNames(Schema, filter);

        await PostgresEventStoreAdmin.EnsureInitializedAsync(DataSource, options, cancellationToken: ct);

        // As if another filter had hashed to the same name.
        await using (var command = DataSource.CreateCommand($"COMMENT ON PUBLICATION {names.Publication} IS 'another'"))
            await command.ExecuteNonQueryAsync(ct);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => PostgresEventStoreAdmin.EnsureInitializedAsync(DataSource, options, cancellationToken: ct));

        exception.Message.ShouldContain(names.Publication);
        exception.Message.ShouldContain("another");
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Drop_WhenTheSchemaHasPublicationsForSeveralFilters_DropsThemAll(CancellationToken ct)
    {
        var schema = $"dbx_drop_{Guid.NewGuid():N}"[..24];
        var plain = new PostgresEventStoreOptions { Schema = schema };
        var filtered = new PostgresEventStoreOptions { Schema = schema, SubscriptionFilter = EventFilter.StreamId("s") };

        await PostgresEventStoreAdmin.EnsureInitializedAsync(DataSource, plain, cancellationToken: ct);
        await PostgresEventStoreAdmin.EnsureInitializedAsync(DataSource, filtered, cancellationToken: ct);
        (await GetPublicationsAsync(ct, schema)).Count.ShouldBe(2);

        await PostgresEventStoreAdmin.DropAsync(DataSource, plain, ct);

        (await GetPublicationsAsync(ct, schema)).ShouldBeEmpty();
    }

    [Test]
    public void Build_WhenTheSubscriptionFilterIsByType_Refuses()
    {
        var byType = new PostgresEventStoreOptions
        {
            Schema = Schema,
            SubscriptionFilter = EventFilter.OfType<TestEvent>()
        };

        var exception = Should.Throw<EventFilterNotSupportedException>(() => CreateEventStore(options: byType));
        exception.Message.ShouldContain("names");

        Should.ThrowAsync<EventFilterNotSupportedException>(
            () => PostgresEventStoreAdmin.EnsureInitializedAsync(DataSource, byType));
    }

    private Task AppendAsync(string streamId, string value, string? tenant, CancellationToken ct)
    {
        KeyValuePair<string, string>[] metadata = tenant is null ? [] : [new("tenant", tenant)];
        var e = AppendableEvent.Create<object>(new TestEvent { Value = value }, metadata);

        return _store.AppendAsync(streamId, [e], cancellationToken: ct);
    }

    private async Task<string?> DescribePublicationAsync(string publication, CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT obj_description(oid, 'pg_publication') FROM pg_publication WHERE pubname = $1");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = publication });

        return await command.ExecuteScalarAsync(ct) as string;
    }

    private async Task<List<string>> GetPublicationsAsync(CancellationToken ct, string? schema = null)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT pubname FROM pg_publication WHERE starts_with(pubname, $1) ORDER BY pubname");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"{schema ?? Schema}_event_log_pub" });

        var publications = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            publications.Add(reader.GetString(0));

        return publications;
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

    // Reads the value of a test event out of its JSON, for a feed that has no codec.
    private sealed class ValueDecoder : IEventDecoder<string, PostgresEventData, string>
    {
        public static readonly ValueDecoder Instance = new();

        public DecodedEvent<string> Decode(string eventName, PostgresEventData eventData, string? metadata)
        {
            using var json = System.Text.Json.JsonDocument.Parse(eventData.Json);

            var value = json.RootElement.GetProperty("Value").GetString()!;

            return DecodedEvent.Create(value, new Dictionary<string, string>());
        }

        public IReadOnlyCollection<string> ResolveEventNames(Type eventType) => [];
    }

    private sealed class CollectingObserver : IEventLogObserver<EventLogRow<string>>
    {
        private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

        public ChannelReader<string> Reader => _channel.Reader;

        public ValueTask OnNextAsync(EventLogRow<string> row, CancellationToken cancellationToken)
        {
            _channel.Writer.TryWrite(row.DecodedEvent.Payload);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _channel.Writer.TryComplete(exception);
            return ValueTask.CompletedTask;
        }
    }
}