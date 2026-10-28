using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Tests that the scavenger works with the shortest and longest timeouts that the store accepts.
/// </summary>
[TestFixture]
public class InactiveSlotScavengerTests : PostgresIntegrationTest
{
    private readonly SlotNameGenerator _slotNameGenerator = new(new SchemaObjectNames("dbx_scavenger"));

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task EnsureStarted_ShortestTimeout_DropsLeftoverSlot(CancellationToken ct)
    {
        var leftover = _slotNameGenerator.Next();

        try
        {
            await CreateSlotAsync(leftover, ct);
            await using var scavenger = CreateScavenger(TimerDurations.MinPeriod);

            scavenger.EnsureStarted();

            while (await SlotExistsAsync(leftover, ct))
                await Task.Delay(10, ct);
        }
        finally
        {
            await DropSlotIfExistsAsync(leftover);
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task DropInactiveSlotsAsync_LongestTimeout_KeepsLeftoverSlot(CancellationToken ct)
    {
        var leftover = _slotNameGenerator.Next();

        try
        {
            await CreateSlotAsync(leftover, ct);
            await using var scavenger = CreateScavenger(TimerDurations.MaxDuration);

            scavenger.EnsureStarted();
            await scavenger.DropInactiveSlotsAsync(ct);

            (await SlotExistsAsync(leftover, ct)).ShouldBeTrue();
        }
        finally
        {
            await DropSlotIfExistsAsync(leftover);
        }
    }

    private InactiveSlotScavenger CreateScavenger(TimeSpan inactiveSlotTimeout) => new(
        DataSource,
        _slotNameGenerator.NamePattern,
        inactiveSlotTimeout,
        LoggerFactory.CreateLogger("InactiveSlotScavenger"));

    private static async Task CreateSlotAsync(string slotName, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT pg_create_logical_replication_slot($1, 'pgoutput')");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> SlotExistsAsync(string slotName, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT EXISTS (SELECT FROM pg_replication_slots WHERE slot_name = $1)");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });
        return (bool)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    private static async Task DropSlotIfExistsAsync(string slotName)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT pg_drop_replication_slot(slot_name) FROM pg_replication_slots WHERE slot_name = $1");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });
        await command.ExecuteNonQueryAsync();
    }
}