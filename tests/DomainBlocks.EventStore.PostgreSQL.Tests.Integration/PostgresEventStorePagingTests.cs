using System.Linq.Expressions;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Keyset paging edge cases. The contract suites already run with a small read batch size, so this only covers what
/// depends on the exact batch boundary.
/// </summary>
[TestFixture]
public class PostgresEventStorePagingTests() : PostgresIntegrationTest(x => x.ReadBatchSize = BatchSize)
{
    private const int BatchSize = 7;

    // Held once, as predicates are equal only if they are the same one.
    private static readonly Expression<Func<TestEvent, bool>> IsNearTheEnd = e => e.Value == "e20" || e.Value == "e22";

    private IEventStore<object, string, StreamPosition, LogPosition> _eventStore = null!;

    [SetUp]
    public void SetUp()
    {
        _eventStore = CreateEventStore();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _eventStore.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_ExactMultipleOfBatchSize_DoesNotIssueAnEmptyTrailingPage(CancellationToken ct)
    {
        var streamId = $"test-{Guid.NewGuid():N}";
        var events = Enumerable.Range(0, BatchSize * 2).Select(i => new TestEvent { Value = $"e{i}" }).ToArray();

        await _eventStore.AppendAsync(
            streamId,
            events.Select(x => AppendableEvent.Create<object>(x)),
            cancellationToken: ct);

        var read = await _eventStore.ReadStream(streamId).ToArrayAsync(ct);

        read.Select(x => x.Payload).ShouldBe(events);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WhenWholePagesAreTestedInProcessAndSelectNothing_ReadsOnToWhatIsSelected(
        CancellationToken ct)
    {
        var streamId = $"test-{Guid.NewGuid():N}";
        var events = Enumerable.Range(0, (BatchSize * 3) + 2).Select(i => new TestEvent { Value = $"e{i}" }).ToArray();

        await _eventStore.AppendAsync(
            streamId,
            events.Select(x => AppendableEvent.Create<object>(x)),
            cancellationToken: ct);

        // Nothing is left to the database, so the first two pages come back whole and select nothing.
        var options = new ReadStreamOptions
        {
            Filter = EventFilter.OfType(IsNearTheEnd),
            FilterPushdownMode = FilterPushdownMode.None
        };

        var forward = await _eventStore.ReadStream(streamId, options: options).ToArrayAsync(ct);

        var first = await _eventStore
            .ReadStream(streamId, options: options with { MaxCount = 1 })
            .ToArrayAsync(ct);

        var last = await _eventStore
            .ReadStream(streamId, ReadDirection.Backward, options: options with { MaxCount = 1 })
            .ToArrayAsync(ct);

        forward.Select(x => ((TestEvent)x.Payload).Value).ShouldBe(["e20", "e22"]);
        first.Select(x => ((TestEvent)x.Payload).Value).ShouldBe(["e20"]);
        last.Select(x => ((TestEvent)x.Payload).Value).ShouldBe(["e22"]);
    }
}