using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using NUnit.Framework;
using Shouldly;
using static DomainBlocks.EventStore.PostgreSQL.Tests.Integration.AppendFunctionClient;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// The tail hears of appends through the notifications that append_events sends. The poll interval is longer than any
/// test, so an event arrives only if its append was heard.
/// </summary>
[TestFixture]
public class PostgresAppendListenerTests() : PostgresIntegrationTest(opt =>
{
    opt.Tail = new EventLogTailOptions { PollInterval = TimeSpan.FromHours(1) };
})
{
    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_EventsAppended_DeliversThemWithoutPolling(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();
        await using var enumerator = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);

        for (var i = 0; i < 3; i++)
        {
            var live = new TestEvent { Value = $"live-{i}" };
            await eventStore.AppendAsync("s1", [Appendable(live)], cancellationToken: ct);

            (await enumerator.MoveNextAsync()).ShouldBeTrue();
            enumerator.Current.Event.ShouldNotBeNull().Payload.ShouldBe(live);
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ListenForAppendsAsync_EnumerationStoppedWithoutCancellation_StopsListening(CancellationToken ct)
    {
        // Only listening is exercised, so the source needs no reader or decoder.
        var source = new PostgresSubscriptionSource<object>(
            DataSource,
            reader: null!,
            new SchemaObjectNames(Schema),
            decoder: null!);

        await using (var appends = source.ListenForAppendsAsync(ct).GetAsyncEnumerator(ct))
        {
            var next = appends.MoveNextAsync();
            await ListenerSessions.WaitForCountAsync(Schema, 1, ct);

            await Client.AppendAsync([Any("s1", JsonEvent(), JsonEvent())], ct);

            (await next).ShouldBeTrue();
            appends.Current.ShouldBe(LogPosition.FromInt64(1));
        }

        // The connection went back to the pool open, so it must have stopped listening first.
        await ListenerSessions.WaitForCountAsync(Schema, 0, ct);
    }
}