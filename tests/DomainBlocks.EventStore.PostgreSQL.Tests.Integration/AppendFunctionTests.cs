using System.Diagnostics;
using Npgsql;
using NUnit.Framework;
using Shouldly;
using static DomainBlocks.EventStore.PostgreSQL.Tests.Integration.AppendFunctionClient;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[TestFixture]
public class AppendFunctionTests
{
    private const string Schema = "dbx_append_fn_tests";
    private static readonly PostgresEventStoreOptions Options = new() { Schema = Schema };
    private AppendFunctionClient _client = null!;

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
    }

    [Test]
    public async Task NewStream_AssignsStreamPositionsFromZero()
    {
        var results = await _client.AppendAsync(Any("s1", JsonEvent(), JsonEvent(), JsonEvent()));

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusAppended, AppendProtocol.ObservedDoesNotExist, null, 0, 2)]);

        var rows = await _client.ReadRowsAsync();
        rows.Select(x => x.StreamPosition).ShouldBe([0, 1, 2]);
        rows.Select(x => x.Position).ShouldBe([0, 1, 2]);
        rows.Select(x => x.CommitIndex).ShouldBe([0, 1, 2]);
    }

    [Test]
    public async Task ExistingStream_ContinuesFromHead()
    {
        await _client.AppendAsync(Any("s1", JsonEvent(), JsonEvent()));

        var results = await _client.AppendAsync(Any("s1", JsonEvent()));

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusAppended, AppendProtocol.ObservedAtVersion, 1, 2, 2)]);
        (await _client.ReadRowsAsync()).Select(x => x.StreamPosition).ShouldBe([0, 1, 2]);
    }

    [Test]
    public async Task MultipleRequests_AssignsContiguousGlobalPositions()
    {
        var results = await _client.AppendAsync(
            Any("s1", JsonEvent(), JsonEvent()),
            Any("s2", JsonEvent()),
            Any("s1", JsonEvent()));

        results.Select(x => (x.Status, x.FirstPosition, x.LastPosition)).ShouldBe(
        [
            (AppendProtocol.StatusAppended, 0L, 1L),
            (AppendProtocol.StatusAppended, 2L, 2L),
            (AppendProtocol.StatusAppended, 3L, 3L)
        ]);

        var rows = await _client.ReadRowsAsync();
        rows.Select(x => x.Position).ShouldBe([0, 1, 2, 3]);
        rows.Select(x => (x.StreamId, x.StreamPosition)).ShouldBe([("s1", 0L), ("s1", 1L), ("s2", 0L), ("s1", 2L)]);
    }

    [Test]
    public async Task AdvancesSequenceByRowsInserted()
    {
        await _client.AppendAsync(Any("s1", JsonEvent(), JsonEvent()), Any("s2", JsonEvent()));
        (await _client.GetSequenceNextAsync()).ShouldBe(3);

        await _client.AppendAsync(Any("s3", JsonEvent()));
        (await _client.GetSequenceNextAsync()).ShouldBe(4);
    }

    [Test]
    public async Task ExpectedDoesNotExist_WhenStreamExists_ReturnsConflictAtVersion()
    {
        await _client.AppendAsync(Any("s1", JsonEvent(), JsonEvent()));

        var results = await _client.AppendAsync(DoesNotExist("s1", JsonEvent()));

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusConflict, AppendProtocol.ObservedAtVersion, 1, null, null)]);
        (await _client.ReadRowsAsync()).Count.ShouldBe(2);
    }

    [Test]
    public async Task ExpectedExists_WhenStreamMissing_ReturnsConflictDoesNotExist()
    {
        var results = await _client.AppendAsync(Exists("s1", JsonEvent()));

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusConflict, AppendProtocol.ObservedDoesNotExist, null, null, null)]);

        (await _client.ReadRowsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task ExpectedAtVersion_WhenMismatched_ReturnsConflictWithHead()
    {
        await _client.AppendAsync(Any("s1", JsonEvent(), JsonEvent(), JsonEvent()));

        var results = await _client.AppendAsync(AtVersion("s1", 1, JsonEvent()));

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusConflict, AppendProtocol.ObservedAtVersion, 2, null, null)]);
    }

    [Test]
    public async Task ExpectedAtVersion_WhenStreamMissing_ReturnsConflictDoesNotExist()
    {
        var results = await _client.AppendAsync(AtVersion("s1", 0, JsonEvent()));

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusConflict, AppendProtocol.ObservedDoesNotExist, null, null, null)]);
    }

    [Test]
    public async Task ExpectedAtVersion_WhenMatched_Appends()
    {
        await _client.AppendAsync(Any("s1", JsonEvent(), JsonEvent()));

        var results = await _client.AppendAsync(AtVersion("s1", 1, JsonEvent()));

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusAppended, AppendProtocol.ObservedAtVersion, 1, 2, 2)]);
    }

    [Test]
    public async Task ConflictInBatch_DoesNotAbortOtherRequests()
    {
        await _client.AppendAsync(Any("s1", JsonEvent()));

        var results = await _client.AppendAsync(
            Any("s2", JsonEvent()),
            DoesNotExist("s1", JsonEvent()),
            Any("s3", JsonEvent(), JsonEvent()));

        results.Select(x => (x.Status, x.FirstPosition, x.LastPosition)).ShouldBe(
        [
            (AppendProtocol.StatusAppended, 1L, 1L),
            (AppendProtocol.StatusConflict, null, null),
            (AppendProtocol.StatusAppended, 2L, 3L)
        ]);

        (await _client.ReadRowsAsync()).Select(x => x.Position).ShouldBe([0, 1, 2, 3]);
    }

    [Test]
    public async Task ConflictsAndDuplicates_DoNotAdvanceSequence()
    {
        var first = Any("s1", JsonEvent());
        await _client.AppendAsync(first);

        await _client.AppendAsync(
            DoesNotExist("s1", JsonEvent()),
            first with { StreamId = "s2" },
            Exists("s3", JsonEvent()));

        (await _client.GetSequenceNextAsync()).ShouldBe(1);
        (await _client.ReadRowsAsync()).Count.ShouldBe(1);
    }

    [Test]
    public async Task ExistingCommitId_ReturnsDuplicateWithoutWriting()
    {
        var request = Any("s1", JsonEvent(), JsonEvent());
        await _client.AppendAsync(request);

        var results = await _client.AppendAsync(request);

        results.ShouldBe(
            [new Result(0, AppendProtocol.StatusDuplicate, AppendProtocol.ObservedAtVersion, 1, null, null)]);
        (await _client.ReadRowsAsync()).Count.ShouldBe(2);
    }

    [Test]
    public async Task ExistingCommitId_OnDifferentStream_ReturnsDuplicate()
    {
        var request = Any("s1", JsonEvent());
        await _client.AppendAsync(request);

        var results = await _client.AppendAsync(request with { StreamId = "s2" });

        results[0].Status.ShouldBe(AppendProtocol.StatusDuplicate);
        (await _client.ReadRowsAsync()).Count.ShouldBe(1);
    }

    [Test]
    public async Task RepeatedCommitIdInBatch_WritesFirstOnly()
    {
        var request = Any("s1", JsonEvent());

        var results = await _client.AppendAsync(request, request, request with { StreamId = "s2" });

        results.Select(x => x.Status).ShouldBe(
        [
            AppendProtocol.StatusAppended,
            AppendProtocol.StatusDuplicate,
            AppendProtocol.StatusDuplicate
        ]);

        (await _client.ReadRowsAsync()).Count.ShouldBe(1);
        (await _client.GetSequenceNextAsync()).ShouldBe(1);
    }

    [Test]
    public async Task SameStreamTwiceInBatch_SecondSeesFirstHead()
    {
        var results = await _client.AppendAsync(
            DoesNotExist("s1", JsonEvent()),
            DoesNotExist("s1", JsonEvent()),
            AtVersion("s1", 0, JsonEvent()));

        results.Select(x => (x.Status, x.ObservedKind, x.ObservedVersion)).ShouldBe(
        [
            (AppendProtocol.StatusAppended, AppendProtocol.ObservedDoesNotExist, null),
            (AppendProtocol.StatusConflict, AppendProtocol.ObservedAtVersion, 0L),
            (AppendProtocol.StatusAppended, AppendProtocol.ObservedAtVersion, 0L)
        ]);

        (await _client.ReadRowsAsync()).Select(x => x.StreamPosition).ShouldBe([0, 1]);
    }

    [Test]
    public async Task JsonAndBytesEvents_StoredInRespectiveColumns()
    {
        byte[] bytes = [1, 2, 3, 255];

        await _client.AppendAsync(Any(
            "s1",
            Event.WithJson("json-event", "{\"a\": 1}", "{\"tenant\": \"x\"}"),
            Event.WithBytes("bytes-event", bytes)));

        var rows = await _client.ReadRowsAsync();

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

        await _client.AppendAsync(Any("s1", JsonEvent(), JsonEvent()), Any("s2", JsonEvent()));

        var rows = await _client.ReadRowsAsync();
        rows.Select(x => x.CreatedAt).Distinct().Count().ShouldBe(1);
        rows[0].CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        rows[0].CreatedAt.ShouldBeInRange(before, DateTime.UtcNow.AddSeconds(1));
    }

    [Test]
    public async Task EmptyBatch_ReturnsNoRows()
    {
        var results = await _client.AppendAsync();

        results.ShouldBeEmpty();
        (await _client.GetSequenceNextAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ZeroEventCount_RaisesInvalidParameterValue()
    {
        var ex = await Should.ThrowAsync<PostgresException>(() => _client.AppendAsync(Any("s1")));

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task AtVersionWithoutVersion_RaisesInvalidParameterValue()
    {
        var request = new Request("s1", AppendProtocol.ExpectedAtVersion, null, Guid.NewGuid(), JsonEvent());

        var ex = await Should.ThrowAsync<PostgresException>(() => _client.AppendAsync(request));

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task VersionWithoutAtVersion_RaisesInvalidParameterValue()
    {
        var request = new Request("s1", AppendProtocol.ExpectedAny, 3, Guid.NewGuid(), JsonEvent());

        var ex = await Should.ThrowAsync<PostgresException>(() => _client.AppendAsync(request));

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task EmptyStreamId_RaisesInvalidParameterValue()
    {
        var ex = await Should.ThrowAsync<PostgresException>(() => _client.AppendAsync(Any("", JsonEvent())));

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task ArrayLengthMismatch_RaisesInvalidParameterValue()
    {
        await using var command = SetUpFixture.DataSource.CreateCommand(
            $"SELECT * FROM {Schema}.append_events(" +
            "ARRAY['s1'], ARRAY[0::smallint, 0::smallint], ARRAY[NULL::bigint], ARRAY[gen_random_uuid()], ARRAY[1], " +
            "ARRAY['e'], ARRAY['{}'::jsonb], ARRAY[NULL::bytea], ARRAY[NULL::jsonb])");

        var ex = await Should.ThrowAsync<PostgresException>(command.ExecuteNonQueryAsync);

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task UnderRepeatableRead_RaisesInvalidTransactionState()
    {
        await using var connection = await SetUpFixture.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            _client.AppendAsync(connection, Any("s1", JsonEvent())));

        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidTransactionState);
    }

    [Test]
    public async Task LargeBatch_CompletesInLinearTime()
    {
        // 500 requests x 10 events. A quadratic array-subscript cost inside the function would make this take seconds.
        var requests = Enumerable
            .Range(0, 500)
            .Select(i => Any($"s{i % 50}", Enumerable.Range(0, 10).Select(_ => JsonEvent()).ToArray()))
            .ToArray();

        var start = Stopwatch.GetTimestamp();
        var results = await _client.AppendAsync(requests);
        var elapsed = Stopwatch.GetElapsedTime(start);

        TestContext.Out.WriteLine($"Appended 5000 events in {elapsed.TotalMilliseconds:F0} ms");

        results.Count.ShouldBe(500);
        results.ShouldAllBe(x => x.Status == AppendProtocol.StatusAppended);
        (await _client.GetSequenceNextAsync()).ShouldBe(5000);
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }
}
