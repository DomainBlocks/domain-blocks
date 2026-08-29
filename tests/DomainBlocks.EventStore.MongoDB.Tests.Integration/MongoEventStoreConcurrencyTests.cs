using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoEventStoreConcurrencyTests
{
    private const int InstanceCount = 3;

    private MongoEventStoreOptions _options = null!;
    private TestMongoEventStoreFactory<object> _factory = null!;
    private ITestEventStoreHandle<object, string, StreamPosition, LogPosition>[] _handles = null!;

    [SetUp]
    public async Task SetUp()
    {
        _options = new MongoEventStoreOptions { DatabaseName = $"dbx_test_{Guid.NewGuid():N}" };
        _factory = TestMongoEventStoreFactory.CreateDefault(_options);

        _handles = new ITestEventStoreHandle<object, string, StreamPosition, LogPosition>[InstanceCount];

        for (var i = 0; i < InstanceCount; i++)
            _handles[i] = await _factory.CreateAsync($"instance_{i}");
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var handle in _handles)
            await handle.DisposeAsync();

        await _factory.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentAnyWrites_ToSameStream_AllSucceedWithContiguousPositions(CancellationToken ct)
    {
        const int eventCountPerInstance = 20;
        const int expectedTotalEventCount = InstanceCount * eventCountPerInstance;

        var streamId = $"shared-{Guid.NewGuid():N}";

        // All writers concurrently append to the same stream.
        var tasks = _handles
            .SelectMany((handle, index) => Enumerable
                .Range(0, eventCountPerInstance)
                .Select(i => handle.Instance.AppendAsync(
                    streamId,
                    [new TestEvent { Value = $"w{index}-e{i}" }],
                    cancellationToken: ct)));

        // Every task must complete successfully - no exceptions.
        await Task.WhenAll(tasks);

        // Read back and verify.
        var readEvents = await _handles[0].Instance.ReadStream(streamId).ToArrayAsync(ct);

        readEvents.Length.ShouldBe(expectedTotalEventCount, "All events must be committed");

        var positions = readEvents.Select(e => e.Context.StreamPosition.Value).ToArray();

        positions.ShouldBe(
            Enumerable.Range(0, expectedTotalEventCount).Select(i => (ulong)i),
            "Stream positions must be contiguous starting from 0");
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentStreamCreation_ExpectedStateDoesNotExist_ExactlyOneSucceeds(CancellationToken ct)
    {
        var streamId = $"new-{Guid.NewGuid():N}";

        var results = await Task.WhenAll(_handles.Select(async handle =>
        {
            try
            {
                await handle.Instance.AppendAsync(
                    streamId,
                    [new TestEvent { Value = "create" }],
                    ExpectedStreamState.DoesNotExist<StreamPosition>(),
                    cancellationToken: ct);

                return (Success: true, Exception: null);
            }
            catch (StreamAppendConflictException<StreamPosition> ex)
            {
                return (Success: false, Exception: ex);
            }
        }));

        var successCount = results.Count(r => r.Success);
        var conflictCount = results.Count(r => !r.Success);

        successCount.ShouldBe(1, "Exactly one writer must succeed in creating the stream");
        conflictCount.ShouldBe(InstanceCount - 1, "All other writers must receive a conflict");

        // Verify conflict exceptions are well-formed.
        foreach (var (_, ex) in results.Where(r => !r.Success))
        {
            ex.ShouldNotBeNull();
            ex.StreamId.ShouldBe(streamId);
            ex.ExpectedState.ShouldBe(ExpectedStreamState.DoesNotExist<StreamPosition>());
            ex.ActualState?.ShouldBe(StreamState.AtVersion(new StreamPosition(0)));
        }

        // Verify the stream contains exactly one event.
        var readEvents = await _handles[0].Instance.ReadStream(streamId).ToArrayAsync(ct);

        readEvents.ShouldHaveSingleItem();
        readEvents[0].Context.StreamPosition.ShouldBe(new StreamPosition(0));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentSpecificVersionWrites_ToSameStream_ExactlyOneSucceeds(CancellationToken ct)
    {
        var streamId = $"versioned-{Guid.NewGuid():N}";

        // Seed the stream with one event so all writers can expect version 0.
        await _handles[0].Instance.AppendAsync(
            streamId,
            [new TestEvent { Value = "seed" }],
            cancellationToken: ct);

        var results = await Task.WhenAll(_handles.Select(async handle =>
        {
            try
            {
                await handle.Instance.AppendAsync(
                    streamId,
                    [new TestEvent { Value = "raced" }],
                    ExpectedStreamState.AtVersion(new StreamPosition(0)),
                    cancellationToken: ct);

                return (Success: true, Exception: null);
            }
            catch (StreamAppendConflictException ex)
            {
                return (Success: false, Exception: ex);
            }
        }));

        var successes = results.Count(r => r.Success);
        var conflicts = results.Count(r => !r.Success);

        successes.ShouldBe(1, "Exactly one writer must win the version race");
        conflicts.ShouldBe(InstanceCount - 1, "All other writers must be rejected");

        // The stream must have exactly 2 events: the seed + the winner.
        var readEvents = await _handles[0].Instance.ReadStream(streamId).ToArrayAsync(ct);

        readEvents.Length.ShouldBe(2);
        readEvents[0].Context.StreamPosition.ShouldBe(new StreamPosition(0));
        readEvents[1].Context.StreamPosition.ShouldBe(new StreamPosition(1));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentWrites_ToIndependentStreams_AllSucceed(CancellationToken ct)
    {
        const int eventCountPerInstance = 50;

        var streamIds = _handles.Select(_ => $"indep-{Guid.NewGuid():N}").ToList();

        var tasks = _handles.Select((handle, i) =>
            Task.WhenAll(Enumerable.Range(0, eventCountPerInstance).Select(j =>
                handle.Instance.AppendAsync(
                    streamIds[i],
                    [new TestEvent { Value = $"e{j}" }],
                    cancellationToken: ct))));

        await Task.WhenAll(tasks);

        // Each stream must have exactly eventCountPerInstance events with contiguous positions.
        foreach (var streamId in streamIds)
        {
            var readEvents = await _handles[0].Instance.ReadStream(streamId).ToArrayAsync(ct);

            readEvents.Length.ShouldBe(
                eventCountPerInstance,
                $"Stream {streamId} must have {eventCountPerInstance} events");

            var positions = readEvents.Select(e => e.Context.StreamPosition.Value).ToList();
            positions.ShouldBe(Enumerable.Range(0, eventCountPerInstance).Select(i => (ulong)i));
        }
    }
}