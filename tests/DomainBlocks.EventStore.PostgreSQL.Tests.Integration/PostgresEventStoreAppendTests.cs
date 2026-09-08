using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Verifies AppendAsync by inspecting the event log table directly, since reads are implemented separately.
/// </summary>
[TestFixture]
public class PostgresEventStoreAppendTests
{
    private const string Schema = "dbx_es_append_tests";
    private static readonly PostgresEventStoreOptions Options = new() { Schema = Schema };
    private static readonly EventTypeMap EventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

    private AppendFunctionClient _client = null!;
    private PostgresEventStore<object> _eventStore = null!;

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
        _eventStore = CreateEventStore();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _eventStore.DisposeAsync();
    }

    [Test]
    public async Task AppendAsync_NewStream_WritesRows()
    {
        var commitId = Guid.NewGuid();

        await _eventStore.AppendAsync(
            "s1",
            [Appendable("a"), Appendable("b")],
            commitId: commitId);

        var rows = await _client.ReadRowsAsync();

        rows.Count.ShouldBe(2);
        rows.Select(x => x.Position).ShouldBe([0, 1]);
        rows.Select(x => x.StreamPosition).ShouldBe([0, 1]);
        rows.Select(x => x.CommitIndex).ShouldBe([0, 1]);
        rows.ShouldAllBe(x => x.StreamId == "s1");
        rows.ShouldAllBe(x => x.CommitId == commitId);
        rows.ShouldAllBe(x => x.EventName == "TestEvent");
        rows[0].EventData.ShouldNotBeNull().ShouldContain("\"a\"");
        rows[1].EventData.ShouldNotBeNull().ShouldContain("\"b\"");
        rows.ShouldAllBe(x => x.EventDataBytes == null);
    }

    [Test]
    public async Task AppendAsync_ExistingStream_ContinuesStreamPosition()
    {
        await _eventStore.AppendAsync("s1", [Appendable("a"), Appendable("b")]);
        await _eventStore.AppendAsync("s1", [Appendable("c")]);

        (await _client.ReadRowsAsync()).Select(x => x.StreamPosition).ShouldBe([0, 1, 2]);
    }

    [Test]
    public async Task AppendAsync_ExpectedAtVersionMismatch_ThrowsConflictWithObservedState()
    {
        await _eventStore.AppendAsync("s1", [Appendable("a"), Appendable("b"), Appendable("c")]);

        var expectedState = ExpectedStreamState.AtVersion(new StreamPosition(1));

        var ex = await Should.ThrowAsync<StreamAppendConflictException<StreamPosition>>(() =>
            _eventStore.AppendAsync("s1", [Appendable("d")], expectedState));

        ex.StreamId.ShouldBe("s1");
        ex.ExpectedState.ShouldBe(expectedState);
        ex.ObservedState.ShouldBe(ObservedStreamState.AtVersion(new StreamPosition(2)));
        (await _client.ReadRowsAsync()).Count.ShouldBe(3);
    }

    [Test]
    public async Task AppendAsync_ExpectedExistsAndStreamMissing_ThrowsConflictDoesNotExist()
    {
        var ex = await Should.ThrowAsync<StreamAppendConflictException<StreamPosition>>(() =>
            _eventStore.AppendAsync("s1", [Appendable("a")], ExpectedStreamState.Exists<StreamPosition>()));

        ex.ObservedState.ShouldBe(ObservedStreamState.DoesNotExist<StreamPosition>());
        (await _client.ReadRowsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task AppendAsync_ExpectedDoesNotExistAndStreamExists_ThrowsConflictAtVersion()
    {
        await _eventStore.AppendAsync("s1", [Appendable("a")]);

        var ex = await Should.ThrowAsync<StreamAppendConflictException<StreamPosition>>(() =>
            _eventStore.AppendAsync("s1", [Appendable("b")], ExpectedStreamState.DoesNotExist<StreamPosition>()));

        ex.ObservedState.ShouldBe(ObservedStreamState.AtVersion(new StreamPosition(0)));
    }

    [Test]
    public async Task AppendAsync_ExpectedAtVersionMatches_Appends()
    {
        await _eventStore.AppendAsync("s1", [Appendable("a")]);

        await _eventStore.AppendAsync("s1", [Appendable("b")], ExpectedStreamState.AtVersion(new StreamPosition(0)));

        (await _client.ReadRowsAsync()).Count.ShouldBe(2);
    }

    [Test]
    public async Task AppendAsync_SameCommitIdTwice_WritesOnce()
    {
        var commitId = Guid.NewGuid();

        await _eventStore.AppendAsync("s1", [Appendable("a")], commitId: commitId);
        await _eventStore.AppendAsync("s1", [Appendable("a")], commitId: commitId);

        (await _client.ReadRowsAsync()).Count.ShouldBe(1);
    }

    [Test]
    public async Task AppendAsync_EmptyEvents_WritesNothing()
    {
        await _eventStore.AppendAsync("s1", []);

        (await _client.ReadRowsAsync()).ShouldBeEmpty();
        (await _client.GetSequenceNextAsync()).ShouldBe(0);
    }

    [Test]
    public async Task AppendAsync_BytesEventData_StoredInByteaColumn()
    {
        await using var bytesStore = CreateEventStore(bytes: true);

        await bytesStore.AppendAsync("s1", [Appendable("a")]);

        var rows = await _client.ReadRowsAsync();
        rows[0].EventData.ShouldBeNull();
        rows[0].EventDataBytes.ShouldNotBeNull().ShouldNotBeEmpty();
    }

    [Test]
    public async Task AppendAsync_Metadata_StoredAsJsonbOrNull()
    {
        await _eventStore.AppendAsync(
            "s1",
            [
                AppendableEvent.Create<object>(new TestEvent { Value = "a" }, [new("tenant", "acme")]),
                Appendable("b")
            ]);

        var rows = await _client.ReadRowsAsync();
        rows[0].Metadata.ShouldBe("{\"tenant\": \"acme\"}");
        rows[1].Metadata.ShouldBeNull();
    }

    [Test]
    public async Task AppendAsync_CreatedAt_IsUtcAndRecent()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        await _eventStore.AppendAsync("s1", [Appendable("a")]);

        var rows = await _client.ReadRowsAsync();
        rows[0].CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        rows[0].CreatedAt.ShouldBeInRange(before, DateTime.UtcNow.AddSeconds(1));
    }

    [Test]
    public async Task AppendAsync_StreamIdWithNul_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(() => _eventStore.AppendAsync("s\01", [Appendable("a")]));
        await Should.ThrowAsync<ArgumentException>(() => _eventStore.AppendAsync("", [Appendable("a")]));
    }

    [Test]
    public async Task AppendAsync_EventDataWithNul_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(() => _eventStore.AppendAsync("s1", [Appendable("a\0b")]));

        (await _client.ReadRowsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task AppendAsync_SequenceRowLockedElsewhere_ThrowsTimeoutException()
    {
        await using var connection = await SetUpFixture.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var lockCommand = new NpgsqlCommand($"SELECT next FROM {Schema}.sequences FOR UPDATE", connection))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        var options = new AppendOptions { Timeout = TimeSpan.FromMilliseconds(500) };

        await Should.ThrowAsync<TimeoutException>(() =>
            _eventStore.AppendAsync("s1", [Appendable("a")], options: options));

        await transaction.RollbackAsync();

        (await _client.ReadRowsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task AppendAsync_CallerCancels_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _eventStore.AppendAsync("s1", [Appendable("a")], cancellationToken: cts.Token));
    }

    private static AppendableEvent<object> Appendable(string value)
    {
        return AppendableEvent.Create<object>(new TestEvent { Value = value });
    }

    private static PostgresEventStore<object> CreateEventStore(bool bytes = false)
    {
        var eventCodec = bytes
            ? EventCodec.Create(new EventCodecOptions<object, PostgresEventData, string>
            {
                TypeMap = EventTypeMap,
                EventSerde = ((IObjectSerde<byte[]>)new JsonUtf8BytesObjectSerde()).AsPostgresEventDataSerde(),
                MetadataSerde = new JsonMetadataSerde()
            })
            : TestPostgresEventCodec.Create<object>(EventTypeMap);

        return PostgresEventStore.Create(
            SetUpFixture.DataSource,
            eventCodec,
            Options,
            SetUpFixture.LoggerFactory.CreateLogger("PostgresEventStore"));
    }
}
