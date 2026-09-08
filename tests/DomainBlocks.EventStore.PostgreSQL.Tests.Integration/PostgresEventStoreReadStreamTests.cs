using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// PostgreSQL-specific ReadStream cases beyond the shared suite, mainly around keyset paging.
/// </summary>
[TestFixture]
public class PostgresEventStoreReadStreamTests
{
    private const int BatchSize = 7;
    private const string Schema = "dbx_es_read_stream_tests";

    private static readonly PostgresEventStoreOptions Options = new() { Schema = Schema, ReadBatchSize = BatchSize };
    private static readonly EventTypeMap EventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

    private PostgresEventStore<object> _eventStore = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, Options);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, Options);
    }

    [SetUp]
    public void SetUp()
    {
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
    public async Task ReadStream_MoreRowsThanBatchSize_PagesWithoutGapsOrDuplicates(CancellationToken ct)
    {
        var (streamId, events) = await AppendEventsAsync(50, ct);

        var forward = await _eventStore.ReadStream(streamId).ToArrayAsync(ct);
        forward.Select(x => x.Payload).ShouldBe(events);
        forward.Select(x => x.Context.StreamPosition.Value).ShouldBe(Enumerable.Range(0, 50).Select(i => (ulong)i));

        var backward = await _eventStore.ReadStream(streamId, ReadDirection.Backward).ToArrayAsync(ct);
        backward.Select(x => x.Payload).ShouldBe(events.Reverse());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_ExactMultipleOfBatchSize_DoesNotIssueAnEmptyTrailingPage(CancellationToken ct)
    {
        var (streamId, events) = await AppendEventsAsync(BatchSize * 2, ct);

        var read = await _eventStore.ReadStream(streamId).ToArrayAsync(ct);

        read.Select(x => x.Payload).ShouldBe(events);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_MaxCountSmallerThanBatch_ReturnsMaxCount(CancellationToken ct)
    {
        var (streamId, events) = await AppendEventsAsync(10, ct);

        var read = await _eventStore
            .ReadStream(streamId, options: new ReadStreamOptions { MaxCount = 3 })
            .ToArrayAsync(ct);

        read.Select(x => x.Payload).ShouldBe(events.Take(3));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_MaxCountAcrossPages_ReturnsMaxCount(CancellationToken ct)
    {
        var (streamId, events) = await AppendEventsAsync(30, ct);

        var read = await _eventStore
            .ReadStream(streamId, options: new ReadStreamOptions { MaxCount = (BatchSize * 2) + 3 })
            .ToArrayAsync(ct);

        read.Select(x => x.Payload).ShouldBe(events.Take((BatchSize * 2) + 3));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_FromPositionAcrossPages_ReturnsExpectedEvents(CancellationToken ct)
    {
        var (streamId, events) = await AppendEventsAsync(20, ct);

        var forward = await _eventStore
            .ReadStream(streamId, origin: ReadOrigin.At(new StreamPosition(5)))
            .ToArrayAsync(ct);

        forward.Select(x => x.Payload).ShouldBe(events.Skip(5));

        var backward = await _eventStore
            .ReadStream(streamId, ReadDirection.Backward, ReadOrigin.At(new StreamPosition(15)))
            .ToArrayAsync(ct);

        backward.Select(x => x.Payload).ShouldBe(events.Take(16).Reverse());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_IncludeMetadataFalse_ReturnsEmptyMetadata(CancellationToken ct)
    {
        var streamId = NewStreamId();

        await _eventStore.AppendAsync(
            streamId,
            [AppendableEvent.Create<object>(new TestEvent { Value = "a" }, [new("tenant", "acme")])],
            cancellationToken: ct);

        var withMetadata = await _eventStore.ReadStream(streamId).SingleAsync(ct);
        withMetadata.Context.Metadata["tenant"].ShouldBe("acme");

        var withoutMetadata = await _eventStore
            .ReadStream(streamId, options: new ReadStreamOptions { IncludeMetadata = false })
            .SingleAsync(ct);

        withoutMetadata.Context.Metadata.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_AtPositionBeyondEndWithThrow_ReturnsEmpty(CancellationToken ct)
    {
        var (streamId, _) = await AppendEventsAsync(3, ct);

        var read = await _eventStore
            .ReadStream(
                streamId,
                origin: ReadOrigin.At(new StreamPosition(10)),
                options: new ReadStreamOptions { StreamNotFoundBehavior = StreamNotFoundBehavior.Throw })
            .ToArrayAsync(ct);

        read.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_PreservesContext(CancellationToken ct)
    {
        var (streamId, _) = await AppendEventsAsync(2, ct);
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);

        var read = await _eventStore.ReadStream(streamId).ToArrayAsync(ct);

        read.ShouldAllBe(x => x.Context.StreamId == streamId);
        read.Select(x => x.Context.StreamPosition).ShouldBe([new StreamPosition(0), new StreamPosition(1)]);
        read[1].Context.LogPosition.Value.ShouldBe(read[0].Context.LogPosition.Value + 1);
        read.ShouldAllBe(x => x.Context.CreatedAt > before && x.Context.CreatedAt.Offset == TimeSpan.Zero);
    }

    private async Task<(string StreamId, TestEvent[] Events)> AppendEventsAsync(int count, CancellationToken ct)
    {
        var streamId = NewStreamId();
        var events = Enumerable.Range(0, count).Select(i => new TestEvent { Value = $"e{i}" }).ToArray();

        await _eventStore.AppendAsync(
            streamId,
            events.Select(x => AppendableEvent.Create<object>(x)),
            cancellationToken: ct);

        return (streamId, events);
    }

    private static string NewStreamId() => $"test-{Guid.NewGuid():N}";
}
