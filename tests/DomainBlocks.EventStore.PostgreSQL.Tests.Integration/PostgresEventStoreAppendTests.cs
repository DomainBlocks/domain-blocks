using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Support;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing;
using DomainBlocks.Testing.Integration;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// PostgreSQL-specific append behaviour, verified against the event log table: how rows are stored, the input the
/// server rejects and the append's interaction with the sequence row. Behaviour every store shares is in the contract
/// suites.
/// </summary>
[TestFixture]
public class PostgresEventStoreAppendTests : PostgresIntegrationTest
{
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
    public async Task AppendAsync_NewStream_WritesRows()
    {
        var commitId = Guid.NewGuid();

        await _eventStore.AppendAsync(
            "s1",
            [Appendable("a"), Appendable("b")],
            commitId: commitId);

        var rows = await Client.ReadRowsAsync();

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
    public async Task AppendAsync_EmptyEvents_DoesNotAdvanceSequence()
    {
        await _eventStore.AppendAsync("s1", []);

        (await Client.ReadRowsAsync()).ShouldBeEmpty();
        (await Client.GetSequenceNextAsync()).ShouldBe(0);
    }

    [Test]
    public async Task AppendAsync_BytesEventData_StoredInByteaColumn()
    {
        await using var bytesStore = CreateBytesEventStore();

        await bytesStore.AppendAsync("s1", [Appendable("a")]);

        var rows = await Client.ReadRowsAsync();
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

        var rows = await Client.ReadRowsAsync();
        rows[0].Metadata.ShouldBe("{\"tenant\": \"acme\"}");
        rows[1].Metadata.ShouldBeNull();
    }

    [Test]
    public async Task AppendAsync_CreatedAt_IsUtcAndRecent()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        await _eventStore.AppendAsync("s1", [Appendable("a")]);

        var rows = await Client.ReadRowsAsync();
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

        (await Client.ReadRowsAsync()).ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_SequenceRowLockedElsewhere_TimesOutButStillCommits(CancellationToken ct)
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var lockCommand = new NpgsqlCommand($"SELECT next FROM {Schema}.sequences FOR UPDATE", connection))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        var options = new AppendOptions { Timeout = TimeSpan.FromMilliseconds(500) };

        await Should.ThrowAsync<TimeoutException>(() =>
            _eventStore.AppendAsync("s1", [Appendable("a")], options: options));

        (await Client.ReadRowsAsync()).ShouldBeEmpty();

        // The caller has given up, but the request is already queued: once the lock is released the batch commits.
        await transaction.RollbackAsync(ct);

        while ((await Client.ReadRowsAsync()).Count == 0)
            await Task.Delay(50, ct);
    }

    private PostgresEventStore<object> CreateBytesEventStore()
    {
        var eventCodec = EventCodec.Create(new EventCodecOptions<object, PostgresEventData, string>
        {
            TypeMap = DefaultEventTypeMap,
            EventSerde = ((IObjectSerde<byte[]>)new JsonUtf8BytesObjectSerde()).AsPostgresEventDataSerde(),
            MetadataSerde = new JsonMetadataSerde()
        });

        return Harness.CreateEventStore(eventCodec);
    }
}