using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[TestFixture]
public class PostgresEventStoreReadAllTests
{
    private const int BatchSize = 7;
    private const string Schema = "dbx_es_read_all_tests";

    private static readonly PostgresEventStoreOptions Options = new() { Schema = Schema, ReadBatchSize = BatchSize };
    private static readonly EventTypeMap EventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

    private AppendFunctionClient _client = null!;
    private PostgresEventStore<object> _eventStore = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, Options);
        _client = new AppendFunctionClient(SetUpFixture.DataSource, Schema);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, Options);
    }

    [SetUp]
    public async Task SetUp()
    {
        await _client.ResetAsync();

        _eventStore = PostgresEventStore.Create(
            SetUpFixture.DataSource,
            TestPostgresEventCodec.Create<object>(EventTypeMap),
            Options,
            SetUpFixture.LoggerFactory.CreateLogger("PostgresEventStore"));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _eventStore.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_Forward_ReturnsGapFreeAscendingPositionsAcrossPages(CancellationToken ct)
    {
        var events = await AppendAcrossStreamsAsync(50, ct);

        var read = await _eventStore.ReadAll().ToArrayAsync(ct);

        read.Select(x => x.Payload).ShouldBe(events);
        read.Select(x => x.Context.LogPosition.Value).ShouldBe(Enumerable.Range(0, 50).Select(i => (ulong)i));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_Backward_ReturnsDescendingPositions(CancellationToken ct)
    {
        var events = await AppendAcrossStreamsAsync(20, ct);

        var read = await _eventStore.ReadAll(ReadDirection.Backward).ToArrayAsync(ct);

        read.Select(x => x.Payload).ShouldBe(events.Reverse());
        read.Select(x => x.Context.LogPosition.Value).ShouldBe(Enumerable.Range(0, 20).Reverse().Select(i => (ulong)i));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_AtPosition_IsInclusiveInBothDirections(CancellationToken ct)
    {
        var events = await AppendAcrossStreamsAsync(20, ct);

        var forward = await _eventStore.ReadAll(origin: ReadOrigin.At(new LogPosition(8))).ToArrayAsync(ct);
        forward.Select(x => x.Payload).ShouldBe(events.Skip(8));

        var backward = await _eventStore
            .ReadAll(ReadDirection.Backward, ReadOrigin.At(new LogPosition(8)))
            .ToArrayAsync(ct);

        backward.Select(x => x.Payload).ShouldBe(events.Take(9).Reverse());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_MaxCount_LimitsAcrossPages(CancellationToken ct)
    {
        var events = await AppendAcrossStreamsAsync(30, ct);

        var read = await _eventStore
            .ReadAll(options: new ReadAllOptions { MaxCount = (BatchSize * 2) + 3 })
            .ToArrayAsync(ct);

        read.Select(x => x.Payload).ShouldBe(events.Take((BatchSize * 2) + 3));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_MultipleStreams_InterleavesInCommitOrder(CancellationToken ct)
    {
        await _eventStore.AppendAsync("a", [Appendable("a0"), Appendable("a1")], cancellationToken: ct);
        await _eventStore.AppendAsync("b", [Appendable("b0")], cancellationToken: ct);
        await _eventStore.AppendAsync("a", [Appendable("a2")], cancellationToken: ct);

        var read = await _eventStore.ReadAll().ToArrayAsync(ct);

        read.Select(x => (x.Context.StreamId, x.Context.StreamPosition.Value, x.Context.LogPosition.Value)).ShouldBe(
        [
            ("a", 0UL, 0UL),
            ("a", 1UL, 1UL),
            ("b", 0UL, 2UL),
            ("a", 2UL, 3UL)
        ]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_IncludeMetadataFalse_ReturnsEmptyMetadata(CancellationToken ct)
    {
        await _eventStore.AppendAsync(
            "s",
            [AppendableEvent.Create<object>(new TestEvent { Value = "a" }, [new("tenant", "acme")])],
            cancellationToken: ct);

        var withMetadata = await _eventStore.ReadAll().SingleAsync(ct);
        withMetadata.Context.Metadata["tenant"].ShouldBe("acme");

        var withoutMetadata = await _eventStore
            .ReadAll(options: new ReadAllOptions { IncludeMetadata = false })
            .SingleAsync(ct);

        withoutMetadata.Context.Metadata.ShouldBeEmpty();
    }

    [TestCase(ReadDirection.Forward, true)]
    [TestCase(ReadDirection.Backward, false)]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_EdgeCaseDirectionAndOrigin_ReturnsEmpty(
        ReadDirection direction,
        bool fromEnd,
        CancellationToken ct)
    {
        await AppendAcrossStreamsAsync(3, ct);

        ReadOrigin<LogPosition> origin = fromEnd ? ReadOrigin.End<LogPosition>() : ReadOrigin.Start<LogPosition>();

        var read = await _eventStore.ReadAll(direction, origin).ToArrayAsync(ct);

        read.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_EmptyLog_ReturnsEmpty(CancellationToken ct)
    {
        (await _eventStore.ReadAll().ToArrayAsync(ct)).ShouldBeEmpty();
        (await _eventStore.ReadAll(ReadDirection.Backward).ToArrayAsync(ct)).ShouldBeEmpty();
    }

    private async Task<TestEvent[]> AppendAcrossStreamsAsync(int count, CancellationToken ct)
    {
        var events = Enumerable.Range(0, count).Select(i => new TestEvent { Value = $"e{i}" }).ToArray();

        foreach (var (e, i) in events.Select((e, i) => (e, i)))
            await _eventStore.AppendAsync($"s{i % 3}", [AppendableEvent.Create<object>(e)], cancellationToken: ct);

        return events;
    }

    private static AppendableEvent<object> Appendable(string value)
    {
        return AppendableEvent.Create<object>(new TestEvent { Value = value });
    }
}
