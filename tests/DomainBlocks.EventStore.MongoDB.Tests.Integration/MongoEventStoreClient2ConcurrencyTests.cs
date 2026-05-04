using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoEventStoreClient2ConcurrencyTests
{
    private const int ClientCount = 5;

    private MongoEventStoreClientOptions2 _options = null!;
    private TestMongoEventStoreClient2Factory<object> _clientFactory = null!;
    private ITestEventStoreClientHandle<object>[] _clientHandles = null!;

    [SetUp]
    public async Task SetUp()
    {
        _options = new MongoEventStoreClientOptions2 { DatabaseName = $"dbx_test_{Guid.NewGuid():N}" };
        _clientFactory = TestMongoEventStoreClient2Factory.CreateDefault(_options);

        _clientHandles = new ITestEventStoreClientHandle<object>[ClientCount];

        for (var i = 0; i < ClientCount; i++)
            _clientHandles[i] = await _clientFactory.CreateAsync($"client_{i}");
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var clientHandle in _clientHandles)
            await clientHandle.DisposeAsync();

        await _clientFactory.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentAnyWrites_ToSameStream_AllSucceedWithContiguousVersions(CancellationToken ct)
    {
        const int eventCountPerClient = 20;
        const int expectedTotalEventCount = ClientCount * eventCountPerClient;

        var streamId = $"shared-{Guid.NewGuid():N}";

        // All writers concurrently append to the same stream.
        var tasks = _clientHandles
            .SelectMany((clientHandle, clientIndex) => Enumerable
                .Range(0, eventCountPerClient)
                .Select(i => clientHandle.Client.AppendToStreamAsync(
                    streamId,
                    [new TestEvent { Value = $"w{clientIndex}-e{i}" }],
                    new AppendToStreamOptions { ExpectedState = ExpectedStreamState.Any },
                    ct)));

        // Every task must complete successfully - no exceptions.
        await Task.WhenAll(tasks);

        // Read back and verify.
        var readEvents = await _clientHandles[0].Client
            .ReadStreamAsync(streamId, cancellationToken: ct)
            .ToArrayAsync(ct);

        readEvents.Length.ShouldBe(expectedTotalEventCount, "All events must be committed");

        var versions = readEvents.Select(e => e.Context.StreamVersion.Value).ToArray();

        versions.ShouldBe(
            Enumerable.Range(0, expectedTotalEventCount).Select(i => (ulong)i),
            "Stream versions must be contiguous starting from 0");
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentStreamCreation_ExpectedStateDoesNotExist_ExactlyOneSucceeds(CancellationToken ct)
    {
        var streamId = $"new-{Guid.NewGuid():N}";

        var results = await Task.WhenAll(_clientHandles.Select(async clientHandle =>
        {
            try
            {
                await clientHandle.Client.AppendToStreamAsync(
                    streamId,
                    [new TestEvent { Value = "create" }],
                    new AppendToStreamOptions { ExpectedState = ExpectedStreamState.StreamDoesNotExist },
                    ct);

                return (Success: true, Exception: null);
            }
            catch (StreamAppendConflictException ex)
            {
                return (Success: false, Exception: ex);
            }
        }));

        var successCount = results.Count(r => r.Success);
        var conflictCount = results.Count(r => !r.Success);

        successCount.ShouldBe(1, "Exactly one writer must succeed in creating the stream");
        conflictCount.ShouldBe(ClientCount - 1, "All other writers must receive a conflict");

        // Verify conflict exceptions are well-formed.
        foreach (var (_, ex) in results.Where(r => !r.Success))
        {
            ex.ShouldNotBeNull();
            ex.StreamId.ShouldBe(streamId);
            ex.ExpectedState.ShouldBe(ExpectedStreamState.StreamDoesNotExist);
            ex.ActualState?.ShouldBe(StreamState.StreamExists(StreamVersion.FromInt64(0)));
        }

        // Verify the stream contains exactly one event.
        var readEvents = await _clientHandles[0].Client
            .ReadStreamAsync(streamId, cancellationToken: ct)
            .ToArrayAsync(ct);

        readEvents.ShouldHaveSingleItem();
        readEvents[0].Context.StreamVersion.ShouldBe(StreamVersion.FromInt64(0));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentSpecificVersionWrites_ToSameStream_ExactlyOneSucceeds(CancellationToken ct)
    {
        var streamId = $"versioned-{Guid.NewGuid():N}";

        // Seed the stream with one event so all writers can target SpecificVersion(0).
        await _clientHandles[0].Client.AppendToStreamAsync(
            streamId,
            [new TestEvent { Value = "seed" }],
            new AppendToStreamOptions { ExpectedState = ExpectedStreamState.Any },
            ct);

        var targetVersion = ExpectedStreamState.SpecificVersion(StreamVersion.FromInt64(0));

        var results = await Task.WhenAll(_clientHandles.Select(async clientHandle =>
        {
            try
            {
                await clientHandle.Client.AppendToStreamAsync(
                    streamId,
                    [new TestEvent { Value = "raced" }],
                    new AppendToStreamOptions { ExpectedState = targetVersion },
                    ct);

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
        conflicts.ShouldBe(ClientCount - 1, "All other writers must be rejected");

        // The stream must have exactly 2 events: the seed + the winner.
        var readEvents = await _clientHandles[0].Client
            .ReadStreamAsync(streamId, cancellationToken: ct)
            .ToArrayAsync(ct);

        readEvents.Length.ShouldBe(2);
        readEvents[0].Context.StreamVersion.ShouldBe(StreamVersion.FromInt64(0));
        readEvents[1].Context.StreamVersion.ShouldBe(StreamVersion.FromInt64(1));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentWrites_ToIndependentStreams_AllSucceed(CancellationToken ct)
    {
        const int eventCountPerClient = 50;

        var streamIds = _clientHandles.Select(_ => $"indep-{Guid.NewGuid():N}").ToList();

        var tasks = _clientHandles.Select((clientHandle, i) =>
            Task.WhenAll(Enumerable.Range(0, eventCountPerClient).Select(j =>
                clientHandle.Client.AppendToStreamAsync(
                    streamIds[i],
                    [new TestEvent { Value = $"e{j}" }],
                    new AppendToStreamOptions { ExpectedState = ExpectedStreamState.Any },
                    ct))));

        await Task.WhenAll(tasks);

        // Each stream must have exactly writesPerWriter events with contiguous versions.
        foreach (var streamId in streamIds)
        {
            var readEvents = await _clientHandles[0].Client
                .ReadStreamAsync(streamId, cancellationToken: ct)
                .ToArrayAsync(ct);

            readEvents.Length.ShouldBe(
                eventCountPerClient,
                $"Stream {streamId} must have {eventCountPerClient} events");

            var versions = readEvents.Select(e => e.Context.StreamVersion.Value).ToList();
            versions.ShouldBe(Enumerable.Range(0, eventCountPerClient).Select(i => (ulong)i));
        }
    }
}