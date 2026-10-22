using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Loss of the replication connection, which the feed resumes on its slot, and loss of the slot, after which the feed
/// continues on a new slot and subscriptions recover the events committed in the meantime from their last position.
/// </summary>
[TestFixture]
public class PostgresFeedRecoveryTests() : PostgresIntegrationTest(x =>
{
    x.Replication.RetryDelay = TimeSpan.FromMilliseconds(100);
    x.Replication.MaxRetryDelay = TimeSpan.FromMilliseconds(500);
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
    public async Task SubscribeToAll_WhenWalSenderIsTerminated_ObservesEveryEventOnceWithoutFallingBehind(
        CancellationToken ct)
    {
        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start)
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(enumerator);

        var before = await AppendEventsAsync("before", 10, ct);
        var observed = new List<TestEvent>();

        for (var i = 0; i < before.Length; i++)
            observed.Add((await NextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>());

        var slot = await TerminateWalSenderAsync(ct);

        // These commit while the feed reconnects, and its slot keeps them.
        var during = await AppendEventsAsync("during", 20, ct);

        for (var i = 0; i < during.Length; i++)
            observed.Add((await NextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>());

        observed.ShouldBe(before.Concat(during));
        (await GetSlotNamesAsync(ct)).ShouldHaveSingleItem().ShouldBe(slot);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenSlotIsDropped_ObservesEveryEventOnce(CancellationToken ct)
    {
        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start)
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(enumerator);

        var before = await AppendEventsAsync("before", 10, ct);
        var observed = new List<TestEvent>();

        for (var i = 0; i < before.Length; i++)
            observed.Add((await NextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>());

        var oldSlot = await DropSlotAsync(ct);

        // These commit before the feed has a new slot, which never streams them, so catch-up must recover them.
        var during = await AppendEventsAsync("during", 20, ct);

        var recovered = await ReadUntilRecoveredAsync(enumerator);
        observed.AddRange(recovered.Events);

        var after = new TestEvent { Value = "after" };
        await _eventStore.AppendAsync("s1", [Appendable(after)], cancellationToken: ct);
        observed.Add((await NextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>());

        recovered.FellBehindCount.ShouldBeGreaterThan(0);
        observed.ShouldBe(before.Concat(during).Append(after));

        var slots = await GetSlotNamesAsync(ct);
        slots.ShouldHaveSingleItem().ShouldNotBe(oldSlot);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenSlotIsDroppedWhileIdle_ReportsFellBehindThenCaughtUp(
        CancellationToken ct)
    {
        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start)
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(enumerator);
        await DropSlotAsync(ct);

        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.FellBehind);
        await ShouldBeCaughtUpAsync(enumerator);

        var live = new TestEvent { Value = "live" };
        await _eventStore.AppendAsync("s1", [Appendable(live)], cancellationToken: ct);
        (await NextEventAsync(enumerator)).Payload.ShouldBe(live);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenSlotIsInvalidated_ReportsFellBehindThenCaughtUp(CancellationToken ct)
    {
        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start)
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(enumerator);
        var oldSlot = await InvalidateSlotAsync(ct);

        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.FellBehind);
        await ShouldBeCaughtUpAsync(enumerator);

        var live = new TestEvent { Value = "live" };
        await _eventStore.AppendAsync("s1", [Appendable(live)], cancellationToken: ct);
        (await NextEventAsync(enumerator)).Payload.ShouldBe(live);

        (await GetSlotNamesAsync(ct)).ShouldHaveSingleItem().ShouldNotBe(oldSlot);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_TwoSubscribers_BothRecoverAfterSlotLoss(CancellationToken ct)
    {
        await using var first = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start)
            .GetAsyncEnumerator(ct);

        await using var second = _eventStore
            .SubscribeToStream("s1", SubscriptionOrigin.Start)
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(first);
        await ShouldBeCaughtUpAsync(second);

        await DropSlotAsync(ct);
        var during = await AppendEventsAsync("during", 5, ct);

        var firstRecovered = await ReadUntilRecoveredAsync(first);
        var secondRecovered = await ReadUntilRecoveredAsync(second);

        firstRecovered.Events.ShouldBe(during);
        secondRecovered.Events.ShouldBe(during);

        var live = new TestEvent { Value = "live" };
        await _eventStore.AppendAsync("s1", [Appendable(live)], cancellationToken: ct);
        (await NextEventAsync(first)).Payload.ShouldBe(live);
        (await NextEventAsync(second)).Payload.ShouldBe(live);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_FromEndWhenSlotIsDroppedBeforeFirstEvent_ObservesEventsAppendedSince(
        CancellationToken ct)
    {
        await using var enumerator = _eventStore.SubscribeToStream("s1").GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(enumerator);
        await DropSlotAsync(ct);

        // The subscription has observed nothing yet, so it recovers from where it started. Recovering from a later end
        // would skip these.
        var during = await AppendEventsAsync("during", 5, ct);

        var recovered = await ReadUntilRecoveredAsync(enumerator);

        recovered.Events.ShouldBe(during);
        recovered.FellBehindCount.ShouldBeGreaterThan(0);
    }

    private async Task<TestEvent[]> AppendEventsAsync(string prefix, int count, CancellationToken ct)
    {
        var events = Enumerable.Range(0, count).Select(i => new TestEvent { Value = $"{prefix}-{i}" }).ToArray();

        foreach (var e in events)
            await _eventStore.AppendAsync("s1", [Appendable(e)], cancellationToken: ct);

        return events;
    }

    private static async Task<string> TerminateWalSenderAsync(CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT slot_name, pg_terminate_backend(active_pid) FROM pg_replication_slots " +
            "WHERE slot_name LIKE 'dbx_%' AND active_pid IS NOT NULL");

        await using var reader = await command.ExecuteReaderAsync(ct);

        (await reader.ReadAsync(ct)).ShouldBeTrue("Expected an active replication slot to terminate");
        reader.GetBoolean(1).ShouldBeTrue();

        return reader.GetString(0);
    }

    // Ends the walsender's connection and drops the slot as soon as it is released, before the feed can resume on it.
    // Whichever walsender holds the slot is ended, until the drop succeeds.
    private static async Task<string> DropSlotAsync(CancellationToken ct)
    {
        var slotName = await GetActiveSlotNameAsync(ct);

        await using var drop = PostgresTestEnvironment.DataSource.CreateCommand(
            $$"""
              DO $$
              BEGIN
                  LOOP
                      BEGIN
                          PERFORM pg_drop_replication_slot('{{slotName}}');
                          EXIT;
                      EXCEPTION WHEN object_in_use THEN
                          PERFORM pg_terminate_backend(active_pid)
                          FROM pg_replication_slots
                          WHERE slot_name = '{{slotName}}' AND active_pid IS NOT NULL;

                          PERFORM pg_sleep(0.001);
                      END;
                  END LOOP;
              END
              $$
              """);

        await drop.ExecuteNonQueryAsync(ct);

        return slotName;
    }

    // Lets slots hold back only 1 MB of WAL, then moves to new WAL files until a checkpoint invalidates the feed's
    // slot. The checkpoint ends the walsender that holds the slot first. The setting is server-wide, which is safe
    // only because the tests run one at a time.
    private static async Task<string> InvalidateSlotAsync(CancellationToken ct)
    {
        var slotName = await GetActiveSlotNameAsync(ct);

        await ExecuteAsync("ALTER SYSTEM SET max_slot_wal_keep_size = '1MB'", ct);

        try
        {
            await ExecuteAsync("SELECT pg_reload_conf()", ct);

            await using var isValid = PostgresTestEnvironment.DataSource.CreateCommand(
                $"SELECT invalidation_reason IS NULL FROM pg_replication_slots WHERE slot_name = '{slotName}'");

            while (await isValid.ExecuteScalarAsync(ct) is true)
            {
                await ExecuteAsync("SELECT pg_logical_emit_message(false, 'dbx_test', ''), pg_switch_wal()", ct);
                await ExecuteAsync("CHECKPOINT", ct);
            }
        }
        finally
        {
            await ExecuteAsync("ALTER SYSTEM RESET max_slot_wal_keep_size", CancellationToken.None);
            await ExecuteAsync("SELECT pg_reload_conf()", CancellationToken.None);
        }

        return slotName;
    }

    private static async Task<string> GetActiveSlotNameAsync(CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT slot_name FROM pg_replication_slots WHERE slot_name LIKE 'dbx_%' AND active");

        return (string)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    private static async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<string>> GetSlotNamesAsync(CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT slot_name FROM pg_replication_slots WHERE slot_name LIKE 'dbx_%'");

        var names = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(0));

        return names;
    }

    private static async Task<ReadEvent<object, string, StreamPosition, LogPosition>> NextEventAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();

        return enumerator.Current.Event.ShouldNotBeNull();
    }

    private static async Task ShouldBeCaughtUpAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);
    }

    private static async Task<RecoveredEvents> ReadUntilRecoveredAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> enumerator)
    {
        var events = new List<TestEvent>();
        var fellBehindCount = 0;
        var caughtUpCount = 0;

        while (fellBehindCount == 0 || caughtUpCount < fellBehindCount)
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();

            switch (enumerator.Current)
            {
                case { Event: { } e }:
                    events.Add(e.Payload.ShouldBeOfType<TestEvent>());
                    break;
                case { IsCaughtUp: true }:
                    caughtUpCount++;
                    break;
                case { IsFellBehind: true }:
                    fellBehindCount++;
                    break;
            }
        }

        return new RecoveredEvents([.. events], fellBehindCount);
    }

    private sealed record RecoveredEvents(TestEvent[] Events, int FellBehindCount);
}