using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Support;
using DomainBlocks.Testing.Integration;
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

    private PostgresEventStore<object> _eventStore = null!;

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
}