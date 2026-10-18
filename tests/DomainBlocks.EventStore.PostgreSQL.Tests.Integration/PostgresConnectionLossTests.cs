using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Loss of the connection that listens for appends: the tail reads the events appended meanwhile within a poll
/// interval and listens again, so subscriptions observe every event once and never fall behind.
/// </summary>
[TestFixture]
public class PostgresConnectionLossTests() : PostgresIntegrationTest(opt =>
{
    opt.Tail = new EventLogTailOptions
    {
        PollInterval = TimeSpan.FromMilliseconds(200),
        RetryDelay = TimeSpan.FromMilliseconds(100),
        MaxRetryDelay = TimeSpan.FromMilliseconds(500)
    };
})
{
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
    public async Task SubscribeToAll_WhenListenerIsTerminated_ObservesEveryEventOnceWithoutFallingBehind(
        CancellationToken ct)
    {
        await using var all = _eventStore.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);
        await using var stream = _eventStore.SubscribeToStream("s1", SubscriptionOrigin.Start).GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(all);
        await ShouldBeCaughtUpAsync(stream);

        var before = await AppendEventsAsync("before", 5, ct);
        (await NextEventsAsync(all, before.Length)).ShouldBe(before);
        (await NextEventsAsync(stream, before.Length)).ShouldBe(before);

        await ListenerSessions.WaitForCountAsync(Schema, 1, ct);
        (await ListenerSessions.TerminateAsync(Schema, ct)).ShouldBe(1);

        // These are appended while nothing listens, so the tail reads them when its poll interval elapses.
        var during = await AppendEventsAsync("during", 10, ct);
        (await NextEventsAsync(all, during.Length)).ShouldBe(during);
        (await NextEventsAsync(stream, during.Length)).ShouldBe(during);

        await ListenerSessions.WaitForCountAsync(Schema, 1, ct);

        var after = await AppendEventsAsync("after", 1, ct);
        (await NextEventsAsync(all, after.Length)).ShouldBe(after);
        (await NextEventsAsync(stream, after.Length)).ShouldBe(after);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_FromEndWhenListenerIsTerminatedBeforeFirstEvent_ObservesEventsAppendedSince(
        CancellationToken ct)
    {
        await using var enumerator = _eventStore.SubscribeToStream("s1").GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(enumerator);

        await ListenerSessions.WaitForCountAsync(Schema, 1, ct);
        (await ListenerSessions.TerminateAsync(Schema, ct)).ShouldBe(1);
        var during = await AppendEventsAsync("during", 5, ct);

        (await NextEventsAsync(enumerator, during.Length)).ShouldBe(during);
    }

    private async Task<TestEvent[]> AppendEventsAsync(string prefix, int count, CancellationToken ct)
    {
        var events = Enumerable.Range(0, count).Select(i => new TestEvent { Value = $"{prefix}-{i}" }).ToArray();

        foreach (var e in events)
            await _eventStore.AppendAsync("s1", [Appendable(e)], cancellationToken: ct);

        return events;
    }

    // Reads the next messages, which must all be events. A FellBehind or CaughtUp among them fails the test.
    private static async Task<TestEvent[]> NextEventsAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> enumerator,
        int count)
    {
        var events = new TestEvent[count];

        for (var i = 0; i < count; i++)
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
            events[i] = enumerator.Current.Event.ShouldNotBeNull().Payload.ShouldBeOfType<TestEvent>();
        }

        return events;
    }

    private static async Task ShouldBeCaughtUpAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);
    }
}