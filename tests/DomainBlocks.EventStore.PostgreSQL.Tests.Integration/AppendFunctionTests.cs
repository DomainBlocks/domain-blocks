using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using NUnit.Framework;
using Shouldly;
using static DomainBlocks.EventStore.PostgreSQL.Tests.Integration.AppendFunctionClient;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[TestFixture]
public class AppendFunctionTests : PostgresIntegrationTest
{
    [Test]
    public async Task NewStream_AssignsStreamPositionsFromZero()
    {
        var results = await Client.AppendAsync([Any("s1", JsonEvent(), JsonEvent(), JsonEvent())]);

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusAppended, AppendProtocol.ObservedDoesNotExist, null, 0, 2)]);

        var rows = await Client.ReadRowsAsync();
        rows.Select(x => x.StreamPosition).ShouldBe([0, 1, 2]);
        rows.Select(x => x.Position).ShouldBe([0, 1, 2]);
        rows.Select(x => x.CommitIndex).ShouldBe([0, 1, 2]);
    }

    [Test]
    public async Task ExistingStream_ContinuesFromHead()
    {
        await Client.AppendAsync([Any("s1", JsonEvent(), JsonEvent())]);

        var results = await Client.AppendAsync([Any("s1", JsonEvent())]);

        results.ShouldBe([new Result(0, AppendProtocol.StatusAppended, AppendProtocol.ObservedAtVersion, 1, 2, 2)]);
        (await Client.ReadRowsAsync()).Select(x => x.StreamPosition).ShouldBe([0, 1, 2]);
    }

    [Test]
    public async Task MultipleRequests_AssignsContiguousGlobalPositions()
    {
        var results = await Client.AppendAsync(
        [
            Any("s1", JsonEvent(), JsonEvent()),
            Any("s2", JsonEvent()),
            Any("s1", JsonEvent())
        ]);

        results.Select(x => (x.Status, x.FirstPosition, x.LastPosition)).ShouldBe(
        [
            (AppendProtocol.StatusAppended, 0L, 1L),
            (AppendProtocol.StatusAppended, 2L, 2L),
            (AppendProtocol.StatusAppended, 3L, 3L)
        ]);

        var rows = await Client.ReadRowsAsync();
        rows.Select(x => x.Position).ShouldBe([0, 1, 2, 3]);
        rows.Select(x => (x.StreamId, x.StreamPosition)).ShouldBe([("s1", 0L), ("s1", 1L), ("s2", 0L), ("s1", 2L)]);
    }

    [Test]
    public async Task AdvancesSequenceByRowsInserted()
    {
        await Client.AppendAsync([Any("s1", JsonEvent(), JsonEvent()), Any("s2", JsonEvent())]);
        (await Client.GetSequenceNextAsync()).ShouldBe(3);

        await Client.AppendAsync([Any("s3", JsonEvent())]);
        (await Client.GetSequenceNextAsync()).ShouldBe(4);
    }

    [Test]
    public async Task ExpectedDoesNotExist_WhenStreamExists_ReturnsConflictAtVersion()
    {
        await Client.AppendAsync([Any("s1", JsonEvent(), JsonEvent())]);

        var results = await Client.AppendAsync([DoesNotExist("s1", JsonEvent())]);

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusConflict, AppendProtocol.ObservedAtVersion, 1, null, null)]);

        (await Client.ReadRowsAsync()).Count.ShouldBe(2);
    }

    [Test]
    public async Task ExpectedExists_WhenStreamMissing_ReturnsConflictDoesNotExist()
    {
        var results = await Client.AppendAsync([Exists("s1", JsonEvent())]);

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusConflict, AppendProtocol.ObservedDoesNotExist, null, null, null)]);

        (await Client.ReadRowsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task ExpectedAtVersion_WhenMismatched_ReturnsConflictWithHead()
    {
        await Client.AppendAsync([Any("s1", JsonEvent(), JsonEvent(), JsonEvent())]);

        var results = await Client.AppendAsync([AtVersion("s1", 1, JsonEvent())]);

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusConflict, AppendProtocol.ObservedAtVersion, 2, null, null)]);
    }

    [Test]
    public async Task ExpectedAtVersion_WhenStreamMissing_ReturnsConflictDoesNotExist()
    {
        var results = await Client.AppendAsync([AtVersion("s1", 0, JsonEvent())]);

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusConflict, AppendProtocol.ObservedDoesNotExist, null, null, null)]);
    }

    [Test]
    public async Task ExpectedAtVersion_WhenMatched_Appends()
    {
        await Client.AppendAsync([Any("s1", JsonEvent(), JsonEvent())]);

        var results = await Client.AppendAsync([AtVersion("s1", 1, JsonEvent())]);

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusAppended, AppendProtocol.ObservedAtVersion, 1, 2, 2)]);
    }

    [Test]
    public async Task ConflictInBatch_DoesNotAbortOtherRequests()
    {
        await Client.AppendAsync([Any("s1", JsonEvent())]);

        var results = await Client.AppendAsync(
        [
            Any("s2", JsonEvent()),
            DoesNotExist("s1", JsonEvent()),
            Any("s3", JsonEvent(), JsonEvent())
        ]);

        results.Select(x => (x.Status, x.FirstPosition, x.LastPosition)).ShouldBe(
        [
            (AppendProtocol.StatusAppended, 1L, 1L),
            (AppendProtocol.StatusConflict, null, null),
            (AppendProtocol.StatusAppended, 2L, 3L)
        ]);

        (await Client.ReadRowsAsync()).Select(x => x.Position).ShouldBe([0, 1, 2, 3]);
    }

    [Test]
    public async Task ConflictsAndDuplicates_DoNotAdvanceSequence()
    {
        var first = Any("s1", JsonEvent());
        await Client.AppendAsync([first]);

        await Client.AppendAsync(
        [
            DoesNotExist("s1", JsonEvent()),
            first with { StreamId = "s2" },
            Exists("s3", JsonEvent())
        ]);

        (await Client.GetSequenceNextAsync()).ShouldBe(1);
        (await Client.ReadRowsAsync()).Count.ShouldBe(1);
    }

    [Test]
    public async Task ExistingCommitId_ReturnsDuplicateWithoutWriting()
    {
        var request = Any("s1", JsonEvent(), JsonEvent());
        await Client.AppendAsync([request]);

        var results = await Client.AppendAsync([request]);

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusDuplicate, AppendProtocol.ObservedAtVersion, 1, null, null)]);

        (await Client.ReadRowsAsync()).Count.ShouldBe(2);
    }

    [Test]
    public async Task ExistingCommitId_ProbeUsesPartialIndex()
    {
        // The probe must repeat the index predicate; without it the planner falls back to a sequential scan.
        await Client.AppendAsync(
        [
            Any("s1", JsonEvent(), JsonEvent()),
            Any("s2", JsonEvent())
        ]);

        await using var command = DataSource.CreateCommand(
            $"EXPLAIN (FORMAT TEXT) SELECT e.commit_id FROM {Schema}.event_log AS e " +
            "WHERE e.commit_id = ANY ($1) AND e.commit_index = 0");

        command.Parameters.Add(new NpgsqlParameter<Guid[]>
        {
            NpgsqlDbType = NpgsqlDbType.Uuid.AsArray(),
            TypedValue = [Guid.NewGuid()]
        });

        var plan = await ExplainAsync(command);

        plan.ShouldContain(line => line.Contains("event_log_commit_id_idx"), string.Join('\n', plan));
    }

    [Test]
    public async Task GetAppendStatus_CalledPerRow_IsInlined()
    {
        // The helper must be inlined so that the decision is planned as part of the calling statement rather than
        // evaluated as a function call per request. An inlined call leaves no trace of the function in the plan.
        // Column arguments keep the planner from constant-folding the call, which would hide a missing inline.
        await using var command = DataSource.CreateCommand(
            $"EXPLAIN (VERBOSE, COSTS OFF) SELECT {Schema}.get_append_status(false, k::smallint, NULL, -1) " +
            "FROM generate_series(0, 3) AS k");

        var plan = await ExplainAsync(command);

        plan.ShouldNotContain(line => line.Contains("get_append_status"), string.Join('\n', plan));
        plan.ShouldContain(line => line.Contains("CASE WHEN"), string.Join('\n', plan));
    }

    [Test]
    public async Task UnnestRequests_CalledInFrom_IsInlined()
    {
        // A set-returning helper must be inlined as a subquery; a Function Scan on it would mean the planner runs it
        // as a black box. The probe passes constants only, since a volatile argument such as gen_random_uuid() blocks
        // inlining by itself and would fail the test for the wrong reason.
        await using var command = DataSource.CreateCommand(
            $"EXPLAIN (COSTS OFF) SELECT * FROM {Schema}.unnest_requests(" +
            "ARRAY['s1'], ARRAY[0::smallint], ARRAY[NULL::bigint], " +
            "ARRAY['00000000-0000-0000-0000-000000000001'::uuid], ARRAY[1], ARRAY[]::uuid[])");

        var plan = await ExplainAsync(command);

        plan.ShouldNotContain(line => line.Contains("Function Scan on unnest_requests"), string.Join('\n', plan));
        plan.ShouldContain(line => line.Contains("WindowAgg"), string.Join('\n', plan));
    }

    [Test]
    public async Task GetStreamHeads_CalledInFrom_IsInlinedAndProbesIndexBackwards()
    {
        await using var command = DataSource.CreateCommand(
            $"EXPLAIN (COSTS OFF) SELECT * FROM {Schema}.get_stream_heads(ARRAY['s1', 's2'])");

        var plan = await ExplainAsync(command);

        plan.ShouldNotContain(line => line.Contains("Function Scan on get_stream_heads"), string.Join('\n', plan));

        plan.ShouldContain(
            line => line.Contains("Index Only Scan Backward using event_log_stream_id_stream_position_key"),
            string.Join('\n', plan));
    }

    [Test]
    public async Task ExistingCommitId_OnDifferentStream_ReturnsDuplicate()
    {
        var request = Any("s1", JsonEvent());
        await Client.AppendAsync([request]);

        var results = await Client.AppendAsync([request with { StreamId = "s2" }]);

        results[0].Status.ShouldBe(AppendProtocol.StatusDuplicate);
        (await Client.ReadRowsAsync()).Count.ShouldBe(1);
    }

    [Test]
    public async Task RepeatedCommitIdInBatch_WritesFirstOnly()
    {
        var request = Any("s1", JsonEvent());

        var results = await Client.AppendAsync(
        [
            request,
            request,
            request with { StreamId = "s2" }
        ]);

        results.Select(x => x.Status).ShouldBe(
        [
            AppendProtocol.StatusAppended,
            AppendProtocol.StatusDuplicate,
            AppendProtocol.StatusDuplicate
        ]);

        (await Client.ReadRowsAsync()).Count.ShouldBe(1);
        (await Client.GetSequenceNextAsync()).ShouldBe(1);
    }

    [Test]
    public async Task RepeatedCommitIdInBatch_OnDistinctStreams_WritesFirstOnly()
    {
        // In-batch duplicates on distinct streams: the first occurrence wins, later ones report Duplicate.
        var request = Any("s1", JsonEvent());

        var results = await Client.AppendAsync(
        [
            request,
            request with { StreamId = "s2" },
            Any("s3", JsonEvent()),
            request with { StreamId = "s4" }
        ]);

        results.Select(x => (x.Status, x.FirstPosition)).ShouldBe(
        [
            (AppendProtocol.StatusAppended, 0L),
            (AppendProtocol.StatusDuplicate, null),
            (AppendProtocol.StatusAppended, 1L),
            (AppendProtocol.StatusDuplicate, null)
        ]);

        (await Client.ReadRowsAsync()).Select(x => x.StreamId).ShouldBe(["s1", "s3"]);
        (await Client.GetSequenceNextAsync()).ShouldBe(2);
    }

    [Test]
    public async Task DistinctStreams_MixedOutcomes_AssignsPositionsAndEventsInRequestOrder()
    {
        // A multi-event conflict and a duplicate in the middle of the batch must not shift the events of later
        // requests, and positions must be contiguous over appended requests only.
        var existing = Any("s2", JsonEvent("e0"));

        await Client.AppendAsync(
        [
            existing,
            Any("s4", JsonEvent("f0"), JsonEvent("f1"))
        ]);

        var results = await Client.AppendAsync(
        [
            Any("s1", JsonEvent("a0"), JsonEvent("a1")),
            DoesNotExist("s2", JsonEvent("b0"), JsonEvent("b1"), JsonEvent("b2")),
            Any("s3", JsonEvent("c0")),
            existing with { StreamId = "s5" },
            AtVersion("s4", 1, JsonEvent("d0"), JsonEvent("d1"))
        ]);

        results.ShouldBe(
        [
            new Result(0, AppendProtocol.StatusAppended, AppendProtocol.ObservedDoesNotExist, null, 3, 4),
            new Result(1, AppendProtocol.StatusConflict, AppendProtocol.ObservedAtVersion, 0, null, null),
            new Result(2, AppendProtocol.StatusAppended, AppendProtocol.ObservedDoesNotExist, null, 5, 5),
            new Result(3, AppendProtocol.StatusDuplicate, AppendProtocol.ObservedDoesNotExist, null, null, null),
            new Result(4, AppendProtocol.StatusAppended, AppendProtocol.ObservedAtVersion, 1, 6, 7)
        ]);

        var rows = (await Client.ReadRowsAsync()).Where(x => x.Position >= 3).ToList();

        rows.Select(x => (x.Position, x.StreamId, x.StreamPosition, x.CommitIndex, x.EventName)).ShouldBe(
        [
            (3L, "s1", 0L, 0, "a0"),
            (4L, "s1", 1L, 1, "a1"),
            (5L, "s3", 0L, 0, "c0"),
            (6L, "s4", 2L, 0, "d0"),
            (7L, "s4", 3L, 1, "d1")
        ]);

        (await Client.GetSequenceNextAsync()).ShouldBe(8);
    }

    [Test]
    public async Task DistinctStreams_AllConflictOrDuplicate_LeavesSequenceUntouched()
    {
        var existing = Any("s1", JsonEvent());
        await Client.AppendAsync([existing]);

        var results = await Client.AppendAsync(
        [
            existing with { StreamId = "s2" },
            Exists("s3", JsonEvent())
        ]);

        results.Select(x => x.Status).ShouldBe([AppendProtocol.StatusDuplicate, AppendProtocol.StatusConflict]);
        (await Client.GetSequenceNextAsync()).ShouldBe(1);
        (await Client.ReadRowsAsync()).Count.ShouldBe(1);
    }

    [Test]
    public async Task SameStreamTwiceInBatch_SecondSeesFirstHead()
    {
        var results = await Client.AppendAsync(
        [
            DoesNotExist("s1", JsonEvent()),
            DoesNotExist("s1", JsonEvent()),
            AtVersion("s1", 0, JsonEvent())
        ]);

        results.Select(x => (x.Status, x.ObservedKind, x.ObservedVersion)).ShouldBe(
        [
            (AppendProtocol.StatusAppended, AppendProtocol.ObservedDoesNotExist, null),
            (AppendProtocol.StatusConflict, AppendProtocol.ObservedAtVersion, 0L),
            (AppendProtocol.StatusAppended, AppendProtocol.ObservedAtVersion, 0L)
        ]);

        (await Client.ReadRowsAsync()).Select(x => x.StreamPosition).ShouldBe([0, 1]);
    }

    [Test]
    public async Task JsonAndBytesEvents_StoredInRespectiveColumns()
    {
        byte[] bytes = [1, 2, 3, 255];

        await Client.AppendAsync(
        [
            Any(
                "s1",
                Event.WithJson("json-event", "{\"a\": 1}", "{\"tenant\": \"x\"}"),
                Event.WithBytes("bytes-event", bytes))
        ]);

        var rows = await Client.ReadRowsAsync();

        rows[0].EventName.ShouldBe("json-event");
        rows[0].EventData.ShouldBe("{\"a\": 1}");
        rows[0].EventDataBytes.ShouldBeNull();
        rows[0].Metadata.ShouldBe("{\"tenant\": \"x\"}");

        rows[1].EventName.ShouldBe("bytes-event");
        rows[1].EventData.ShouldBeNull();
        rows[1].EventDataBytes.ShouldBe(bytes);
        rows[1].Metadata.ShouldBeNull();
    }

    [Test]
    public async Task CreatedAt_IsSharedByTheBatchAndUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        await Client.AppendAsync(
        [
            Any("s1", JsonEvent(), JsonEvent()),
            Any("s2", JsonEvent())
        ]);

        var rows = await Client.ReadRowsAsync();
        rows.Select(x => x.CreatedAt).Distinct().Count().ShouldBe(1);
        rows[0].CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        rows[0].CreatedAt.ShouldBeInRange(before, DateTime.UtcNow.AddSeconds(1));
    }

    [Test]
    public async Task EmptyBatch_ReturnsNoRows()
    {
        var results = await Client.AppendAsync([]);

        results.ShouldBeEmpty();
        (await Client.GetSequenceNextAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ZeroEventCount_RaisesInvalidParameterValue()
    {
        var ex = await Should.ThrowAsync<PostgresException>(() => Client.AppendAsync([Any("s1")]));

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task AtVersionWithoutVersion_RaisesInvalidParameterValue()
    {
        var request = new Request("s1", AppendProtocol.ExpectedAtVersion, null, Guid.NewGuid(), JsonEvent());

        var ex = await Should.ThrowAsync<PostgresException>(() => Client.AppendAsync([request]));

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task VersionWithoutAtVersion_RaisesInvalidParameterValue()
    {
        var request = new Request("s1", AppendProtocol.ExpectedAny, 3, Guid.NewGuid(), JsonEvent());

        var ex = await Should.ThrowAsync<PostgresException>(() => Client.AppendAsync([request]));

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task EmptyStreamId_RaisesInvalidParameterValue()
    {
        var ex = await Should.ThrowAsync<PostgresException>(() => Client.AppendAsync([Any("", JsonEvent())]));

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task EmptyBatchWithEvents_RaisesInvalidParameterValue()
    {
        await using var command = DataSource.CreateCommand(
            $"SELECT * FROM {Schema}.append_events(" +
            "ARRAY[]::text[], ARRAY[]::smallint[], ARRAY[]::bigint[], ARRAY[]::uuid[], ARRAY[]::integer[], " +
            "ARRAY['e'], ARRAY['{}'::jsonb], ARRAY[NULL::bytea], ARRAY[NULL::jsonb])");

        var ex = await Should.ThrowAsync<PostgresException>(command.ExecuteNonQueryAsync);

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task ArrayLengthMismatch_RaisesInvalidParameterValue()
    {
        await using var command = DataSource.CreateCommand(
            $"SELECT * FROM {Schema}.append_events(" +
            "ARRAY['s1'], ARRAY[0::smallint, 0::smallint], ARRAY[NULL::bigint], ARRAY[gen_random_uuid()], ARRAY[1], " +
            "ARRAY['e'], ARRAY['{}'::jsonb], ARRAY[NULL::bytea], ARRAY[NULL::jsonb])");

        var ex = await Should.ThrowAsync<PostgresException>(command.ExecuteNonQueryAsync);

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task UnderRepeatableRead_RaisesInvalidTransactionState()
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            Client.AppendAsync(connection, [Any("s1", JsonEvent())]));

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidTransactionState);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task LargeBatch_CompletesInLinearTime(bool distinctStreams)
    {
        // 500 requests x 10 events, with distinct and with repeated streams. A quadratic cost inside the function
        // would make this take seconds.
        var requests = Enumerable
            .Range(0, 500)
            .Select(i => Any(
                distinctStreams ? $"s{i}" : $"s{i % 50}",
                [.. Enumerable.Range(0, 10).Select(_ => JsonEvent())]))
            .ToArray();

        var start = Stopwatch.GetTimestamp();
        var results = await Client.AppendAsync(requests);
        var elapsed = Stopwatch.GetElapsedTime(start);

        await TestContext.Out.WriteLineAsync($"Appended 5000 events in {elapsed.TotalMilliseconds:F0} ms");

        results.Count.ShouldBe(500);
        results.ShouldAllBe(x => x.Status == AppendProtocol.StatusAppended);
        (await Client.GetSequenceNextAsync()).ShouldBe(5000);
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    private static async Task<List<string>> ExplainAsync(NpgsqlCommand command)
    {
        await using var reader = await command.ExecuteReaderAsync();
        var plan = new List<string>();

        while (await reader.ReadAsync())
            plan.Add(reader.GetString(0));

        return plan;
    }
}