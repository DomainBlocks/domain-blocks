using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using NpgsqlTypes;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Tests that a feed's slot ends a walsender that still holds it before the next session, leaves any other process that
/// holds it running, reports when it has been lost, and records the feed's progress only forwards.
/// </summary>
[TestFixture]
public class FeedReplicationSlotTests : PostgresIntegrationTest
{
    private readonly SlotNameGenerator _slotNameGenerator = new(new SchemaObjectNames("dbx_slot"));

    private SchemaObjectNames Names => new(Schema);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task PrepareForSessionAsync_HeldByWalSender_EndsWalSender(CancellationToken ct)
    {
        await using var slot = await CreateSlotAsync(ct);

        // A session's walsender holds the slot while it streams.
        await using var replication = new LogicalReplicationConnection(PostgresTestEnvironment.ConnectionString);
        await replication.Open(ct);
        var streaming = StreamAsync(replication, slot.Name, ct);
        var walSenderPid = await WaitForHolderAsync(slot.Name, ct);

        (await slot.PrepareForSessionAsync(ct)).ShouldBe(PrepareForSessionResult.Ready);

        (await IsBackendRunningAsync(walSenderPid, ct)).ShouldBeFalse();
        await Should.ThrowAsync<NpgsqlException>(streaming);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task PrepareForSessionAsync_HeldByClientBackend_ReturnsInUseWithoutEndingBackend(
        CancellationToken ct)
    {
        await using var slot = await CreateSlotAsync(ct);

        // Only a backend that creates a slot holds it for long enough to test, so the slot is created again under its
        // name.
        await DropSlotAsync(slot.Name, ct);
        await using var creation = await BlockedSlotCreation.StartAsync(DataSource, slot.Name, ct);

        (await slot.PrepareForSessionAsync(ct)).ShouldBe(PrepareForSessionResult.InUse);
        (await IsBackendRunningAsync(creation.ProcessId, ct)).ShouldBeTrue();

        await creation.CompleteAsync(ct);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task PrepareForSessionAsync_Dropped_ReturnsLost(CancellationToken ct)
    {
        await using var slot = await CreateSlotAsync(ct);
        await DropSlotAsync(slot.Name, ct);

        (await slot.PrepareForSessionAsync(ct)).ShouldBe(PrepareForSessionResult.Lost);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task IsLostAsync_Created_ReturnsFalse(CancellationToken ct)
    {
        await using var slot = await CreateSlotAsync(ct);

        (await slot.IsLostAsync(ct)).ShouldBeFalse();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task IsLostAsync_Dropped_ReturnsTrue(CancellationToken ct)
    {
        await using var slot = await CreateSlotAsync(ct);
        await DropSlotAsync(slot.Name, ct);

        (await slot.IsLostAsync(ct)).ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task RecordDeliveredRow_PositionNotAfterLastDelivered_Throws(CancellationToken ct)
    {
        await using var slot = await CreateSlotAsync(ct);
        slot.RecordDeliveredRow(5);

        Should.Throw<ArgumentOutOfRangeException>(() => slot.RecordDeliveredRow(5));
        Should.Throw<ArgumentOutOfRangeException>(() => slot.RecordDeliveredRow(4));
        slot.LastDeliveredPosition.ShouldBe(5);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task RecordDeliveredTransaction_LsnNotAfterResumeLsn_Throws(CancellationToken ct)
    {
        await using var slot = await CreateSlotAsync(ct);
        var resumeLsn = new NpgsqlLogSequenceNumber(100);
        var earlierLsn = new NpgsqlLogSequenceNumber(99);
        slot.RecordDeliveredTransaction(resumeLsn);

        Should.Throw<ArgumentOutOfRangeException>(() => slot.RecordDeliveredTransaction(resumeLsn));
        Should.Throw<ArgumentOutOfRangeException>(() => slot.RecordDeliveredTransaction(earlierLsn));
        slot.ResumeLsn.ShouldBe(resumeLsn);
    }

    private Task<FeedReplicationSlot> CreateSlotAsync(CancellationToken ct)
    {
        return FeedReplicationSlot.CreateAsync(
            _slotNameGenerator.Next(),
            PostgresTestEnvironment.ConnectionString,
            DataSource,
            Names,
            LoggerFactory.CreateLogger("FeedReplicationSlot"),
            ct);
    }

    // Streams from the slot until the walsender ends. Nothing is appended, so no message arrives.
    private async Task StreamAsync(LogicalReplicationConnection connection, string slotName, CancellationToken ct)
    {
        var slot = new PgOutputReplicationSlot(slotName);
        var options = new PgOutputReplicationOptions(Names.Publication, PgOutputProtocolVersion.V1);

        await foreach (var _ in connection.StartReplication(slot, options, ct))
        {
        }
    }

    private static async Task DropSlotAsync(string slotName, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT pg_drop_replication_slot($1)");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });
        await command.ExecuteNonQueryAsync(ct);
    }

    // Returns the process ID of the backend that holds the slot, once one does.
    private static async Task<int> WaitForHolderAsync(string slotName, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT active_pid FROM pg_replication_slots WHERE slot_name = $1");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });

        while (true)
        {
            if (await command.ExecuteScalarAsync(ct) is int processId)
                return processId;

            await Task.Delay(20, ct);
        }
    }

    private static async Task<bool> IsBackendRunningAsync(int processId, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = $1)");

        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = processId });
        return (bool)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    // A client backend that creates a slot while a transaction runs. The creation waits for the transaction to end, and
    // the backend holds the slot until then.
    private sealed class BlockedSlotCreation(
        NpgsqlConnection transactionConnection,
        NpgsqlTransaction transaction,
        NpgsqlConnection creatorConnection,
        Task creation) :
        IAsyncDisposable
    {
        public int ProcessId { get; } = creatorConnection.ProcessID;

        // Returns once the creating backend holds the slot.
        public static async Task<BlockedSlotCreation> StartAsync(
            NpgsqlDataSource dataSource,
            string slotName,
            CancellationToken ct)
        {
            var transactionConnection = await dataSource.OpenConnectionAsync(ct);
            var transaction = await transactionConnection.BeginTransactionAsync(ct);

            // The creation waits only for transactions that have an ID.
            await using (var assignId = new NpgsqlCommand("SELECT pg_current_xact_id()", transactionConnection))
                await assignId.ExecuteNonQueryAsync(ct);

            var creatorConnection = await dataSource.OpenConnectionAsync(ct);
            var creation = CreateSlotAsync(creatorConnection, slotName, ct);
            (await WaitForHolderAsync(slotName, ct)).ShouldBe(creatorConnection.ProcessID);

            return new BlockedSlotCreation(transactionConnection, transaction, creatorConnection, creation);
        }

        public async Task CompleteAsync(CancellationToken ct)
        {
            await transaction.CommitAsync(ct);
            await creation;
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await transactionConnection.DisposeAsync();
            await creation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await creatorConnection.DisposeAsync();
        }

        private static async Task CreateSlotAsync(NpgsqlConnection connection, string slotName, CancellationToken ct)
        {
            await using var command = new NpgsqlCommand(
                "SELECT pg_create_logical_replication_slot($1, 'pgoutput')",
                connection);

            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}