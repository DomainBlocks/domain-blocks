using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Several store instances appending concurrently against one schema, mirroring MongoEventStoreConcurrencyTests.
/// Streams are verified through the table directly until reads are implemented.
/// </summary>
public class PostgresEventStoreConcurrencyTests
{
    private const int InstanceCount = 3;
    private const string Schema = "dbx_es_concurrency_tests";
    private static readonly PostgresEventStoreOptions Options = new() { Schema = Schema };

    private AppendFunctionClient _client = null!;
    private IEventStore<object, string, StreamPosition, LogPosition>[] _instances = null!;

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
    public void SetUp()
    {
        _instances = new IEventStore<object, string, StreamPosition, LogPosition>[InstanceCount];

        var eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());
        var eventCodec = TestPostgresEventCodec.Create<object>(eventTypeMap);

        for (var i = 0; i < InstanceCount; i++)
        {
            _instances[i] = PostgresEventStore.Create(
                SetUpFixture.DataSource,
                eventCodec,
                Options,
                SetUpFixture.LoggerFactory.CreateLogger($"PostgresEventStore_{i}"));
        }
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var instance in _instances.OfType<IAsyncDisposable>())
            await instance.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentAnyWrites_ToSameStream_AllSucceedWithContiguousPositions(CancellationToken ct)
    {
        const int eventCountPerInstance = 20;
        const int expectedTotalEventCount = InstanceCount * eventCountPerInstance;

        var streamId = $"shared-{Guid.NewGuid():N}";

        var tasks = _instances
            .SelectMany((instance, index) => Enumerable
                .Range(0, eventCountPerInstance)
                .Select(i => instance.AppendAsync(
                    streamId,
                    [new TestEvent { Value = $"w{index}-e{i}" }],
                    cancellationToken: ct)));

        await Task.WhenAll(tasks);

        var rows = await _client.ReadRowsAsync(streamId);

        rows.Count.ShouldBe(expectedTotalEventCount, "All events must be committed");

        rows.Select(x => x.StreamPosition).ShouldBe(
            Enumerable.Range(0, expectedTotalEventCount).Select(i => (long)i),
            "Stream positions must be contiguous starting from 0");
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentStreamCreation_ExpectedStateDoesNotExist_ExactlyOneSucceeds(CancellationToken ct)
    {
        var streamId = $"new-{Guid.NewGuid():N}";

        var results = await Task.WhenAll(_instances.Select(
            async Task<(bool Success, StreamAppendConflictException<StreamPosition>? Exception)> (instance) =>
        {
            try
            {
                await instance.AppendAsync(
                    streamId,
                    [new TestEvent { Value = "create" }],
                    ExpectedStreamState.DoesNotExist<StreamPosition>(),
                    cancellationToken: ct);

                return (true, null);
            }
            catch (StreamAppendConflictException<StreamPosition> ex)
            {
                return (false, ex);
            }
        }));

        results.Count(r => r.Success).ShouldBe(1, "Exactly one writer must succeed in creating the stream");
        results.Count(r => !r.Success).ShouldBe(InstanceCount - 1, "All other writers must receive a conflict");

        foreach (var (_, ex) in results.Where(r => !r.Success))
        {
            ex.ShouldNotBeNull();
            ex.StreamId.ShouldBe(streamId);
            ex.ExpectedState.ShouldBe(ExpectedStreamState.DoesNotExist<StreamPosition>());
            ex.ObservedState.ShouldBe(ObservedStreamState.AtVersion(new StreamPosition(0)));
        }

        var rows = await _client.ReadRowsAsync(streamId);

        rows.ShouldHaveSingleItem();
        rows[0].StreamPosition.ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentSpecificVersionWrites_ToSameStream_ExactlyOneSucceeds(CancellationToken ct)
    {
        var streamId = $"versioned-{Guid.NewGuid():N}";

        await _instances[0].AppendAsync(streamId, [new TestEvent { Value = "seed" }], cancellationToken: ct);

        var results = await Task.WhenAll(_instances.Select(
            async Task<(bool Success, StreamAppendConflictException? Exception)> (instance) =>
        {
            try
            {
                await instance.AppendAsync(
                    streamId,
                    [new TestEvent { Value = "raced" }],
                    ExpectedStreamState.AtVersion(new StreamPosition(0)),
                    cancellationToken: ct);

                return (true, null);
            }
            catch (StreamAppendConflictException ex)
            {
                return (false, ex);
            }
        }));

        results.Count(r => r.Success).ShouldBe(1, "Exactly one writer must win the version race");
        results.Count(r => !r.Success).ShouldBe(InstanceCount - 1, "All other writers must be rejected");

        var rows = await _client.ReadRowsAsync(streamId);

        rows.Count.ShouldBe(2);
        rows.Select(x => x.StreamPosition).ShouldBe([0, 1]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentWrites_ToIndependentStreams_AllSucceed(CancellationToken ct)
    {
        const int eventCountPerInstance = 50;

        var streamIds = _instances.Select(_ => $"indep-{Guid.NewGuid():N}").ToList();

        var tasks = _instances.Select((instance, i) =>
            Task.WhenAll(Enumerable.Range(0, eventCountPerInstance).Select(j =>
                instance.AppendAsync(streamIds[i], [new TestEvent { Value = $"e{j}" }], cancellationToken: ct))));

        await Task.WhenAll(tasks);

        foreach (var streamId in streamIds)
        {
            var rows = await _client.ReadRowsAsync(streamId);

            rows.Count.ShouldBe(eventCountPerInstance, $"Stream {streamId} must have {eventCountPerInstance} events");
            rows.Select(x => x.StreamPosition)
                .ShouldBe(Enumerable.Range(0, eventCountPerInstance).Select(i => (long)i));
        }
    }
}
