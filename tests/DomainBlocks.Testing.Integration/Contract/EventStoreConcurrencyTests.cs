using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.Contract;

/// <summary>
/// Several store instances appending concurrently to one store, as separate processes would.
/// </summary>
public abstract class EventStoreConcurrencyTests<TStreamPos, TLogPos>(IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private const int InstanceCount = 3;

    private IEventStore<object, string, TStreamPos, TLogPos>[] _instances = null!;

    [SetUp]
    public void SetUp()
    {
        var eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

        _instances = Enumerable
            .Range(0, InstanceCount)
            .Select(i => CreateEventStore(eventTypeMap, loggerNameSuffix: $"_{i}"))
            .ToArray();
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var instance in _instances.OfType<IAsyncDisposable>())
            await instance.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_ConcurrentAnyWritesToSameStream_AllSucceedWithContiguousPositions(
        CancellationToken cancellationToken)
    {
        const int eventCountPerInstance = 20;
        const int expectedTotalEventCount = InstanceCount * eventCountPerInstance;

        var streamId = NewStreamId();

        var tasks = _instances
            .SelectMany((instance, index) => Enumerable
                .Range(0, eventCountPerInstance)
                .Select(i => instance.AppendAsync(
                    streamId,
                    [new TestEvent { Value = $"w{index}-e{i}" }],
                    cancellationToken: cancellationToken)));

        await Task.WhenAll(tasks);

        var readEvents = await _instances[0].ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents.Length.ShouldBe(expectedTotalEventCount, "All events must be committed");

        readEvents
            .Select(x => x.Context.StreamPosition)
            .ShouldBe(
                Enumerable.Range(0, expectedTotalEventCount).Select(i => CreateStreamPosition((ulong)i)),
                "Stream positions must be contiguous starting from 0");
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_ConcurrentStreamCreation_ExactlyOneSucceeds(CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();

        var results = await Task.WhenAll(_instances.Select(async instance =>
        {
            try
            {
                await instance.AppendAsync(
                    streamId,
                    [new TestEvent { Value = "create" }],
                    ExpectedStreamState.DoesNotExist<TStreamPos>(),
                    cancellationToken: cancellationToken);

                return (Success: true, Exception: null);
            }
            catch (StreamAppendConflictException<TStreamPos> ex)
            {
                return (Success: false, Exception: (StreamAppendConflictException<TStreamPos>?)ex);
            }
        }));

        results.Count(r => r.Success).ShouldBe(1, "Exactly one writer must succeed in creating the stream");
        results.Count(r => !r.Success).ShouldBe(InstanceCount - 1, "All other writers must receive a conflict");

        foreach (var (_, ex) in results.Where(r => !r.Success))
        {
            ex.ShouldNotBeNull();
            ex.StreamId.ShouldBe(streamId);
            ex.ExpectedState.ShouldBe(ExpectedStreamState.DoesNotExist<TStreamPos>());

            // The observed state is optional: a store that learns of the conflict from a unique index violation
            // (MongoDB, when another instance wins the race) cannot report the winner's version without another
            // round trip. When it is reported, it must be the winner's.
            ex.ObservedState?.ShouldBe(ObservedStreamState.AtVersion(CreateStreamPosition(0)));
        }

        var readEvents = await _instances[0].ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents.ShouldHaveSingleItem().Context.StreamPosition.ShouldBe(CreateStreamPosition(0));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_ConcurrentWritesAtSameVersion_ExactlyOneSucceeds(
        CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();

        // Seed the stream with one event so that every writer expects version 0.
        await _instances[0].AppendAsync(
            streamId,
            [new TestEvent { Value = "seed" }],
            cancellationToken: cancellationToken);

        var results = await Task.WhenAll(_instances.Select(async instance =>
        {
            try
            {
                await instance.AppendAsync(
                    streamId,
                    [new TestEvent { Value = "raced" }],
                    ExpectedStreamState.AtVersion(CreateStreamPosition(0)),
                    cancellationToken: cancellationToken);

                return true;
            }
            catch (StreamAppendConflictException)
            {
                return false;
            }
        }));

        results.Count(success => success).ShouldBe(1, "Exactly one writer must win the version race");
        results.Count(success => !success).ShouldBe(InstanceCount - 1, "All other writers must be rejected");

        var readEvents = await _instances[0].ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents
            .Select(x => x.Context.StreamPosition)
            .ShouldBe([CreateStreamPosition(0), CreateStreamPosition(1)], "The stream must hold the seed and the winner");
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_ConcurrentWritesToIndependentStreams_AllSucceed(CancellationToken cancellationToken)
    {
        const int eventCountPerInstance = 50;

        var streamIds = _instances.Select(_ => NewStreamId()).ToList();

        var tasks = _instances.Select((instance, i) =>
            Task.WhenAll(Enumerable.Range(0, eventCountPerInstance).Select(j =>
                instance.AppendAsync(
                    streamIds[i],
                    [new TestEvent { Value = $"e{j}" }],
                    cancellationToken: cancellationToken))));

        await Task.WhenAll(tasks);

        foreach (var streamId in streamIds)
        {
            var readEvents = await _instances[0].ReadStream(streamId).ToArrayAsync(cancellationToken);

            readEvents.Length.ShouldBe(
                eventCountPerInstance,
                $"Stream {streamId} must have {eventCountPerInstance} events");

            readEvents
                .Select(x => x.Context.StreamPosition)
                .ShouldBe(Enumerable.Range(0, eventCountPerInstance).Select(i => CreateStreamPosition((ulong)i)));
        }
    }

    private static string NewStreamId() => $"test-{Guid.NewGuid():N}";
}