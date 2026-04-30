using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreClientTests
{
    // Set a longer timeout when debugging.
#if DEBUG
    protected const int TestTimeoutMillis = 10 * 60 * 1_000;
#else
    protected const int TestTimeoutMillis = 120 * 1_000;
#endif

    private static IEnumerable<TestCaseData> PositionAndDirectionCases
    {
        get
        {
            yield return new TestCaseData(StreamReadPosition.Start, StreamReadDirection.Forward);
            yield return new TestCaseData(StreamReadPosition.Start, StreamReadDirection.Backward);
            yield return new TestCaseData(StreamReadPosition.End, StreamReadDirection.Forward);
            yield return new TestCaseData(StreamReadPosition.End, StreamReadDirection.Backward);
        }
    }

    private static IEnumerable<TestCaseData> PositionAndDirectionEdgeCases
    {
        get
        {
            yield return new TestCaseData(StreamReadPosition.Start, StreamReadDirection.Backward);
            yield return new TestCaseData(StreamReadPosition.End, StreamReadDirection.Forward);
        }
    }

    protected abstract IEventStoreClient<IDomainEvent> Client { get; }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsAnyAndStreamDoesNotExist_AppendsEvents(
        CancellationToken cancellationToken)
    {
        AppendEvent<IDomainEvent>[] events =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(streamId, events, cancellationToken: cancellationToken);

        var readEvents = await Client
            .ReadStreamAsync(streamId, cancellationToken: cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);
        readEvents.Unwrap().ShouldBe(events.Select(x => x.Event));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsAnyAndStreamExists_AppendsEvents(
        CancellationToken cancellationToken)
    {
        AppendEvent<IDomainEvent>[] events1 =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        AppendEvent<IDomainEvent>[] events2 =
        [
            CreateTestEvent("TestEvent4"),
            CreateTestEvent("TestEvent5"),
            CreateTestEvent("TestEvent6")
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(streamId, events1, cancellationToken: cancellationToken);
        await Client.AppendToStreamAsync(streamId, events2, cancellationToken: cancellationToken);

        var readEvents = await Client
            .ReadStreamAsync(streamId, cancellationToken: cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);

        readEvents
            .Unwrap()
            .ShouldBe(events1.Concat(events2).Select(x => x.Event));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateHasWrongVersion_ThrowsVersionConflict(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var expectedState = ExpectedStreamState.SpecificVersion(new StreamVersion(1));

        var exception = await Client
            .AppendToStreamAsync(
                streamId,
                [CreateTestEvent("TestEvent4")],
                new AppendToStreamOptions
                {
                    ExpectedState = expectedState
                },
                cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(expectedState);
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.StreamExists(new StreamVersion(2)));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsStreamExistsAndStreamDoesNotExist_ThrowsExpectedStreamToExist(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        var exception = await Client
            .AppendToStreamAsync(
                streamId,
                [
                    CreateTestEvent("TestEvent1"),
                    CreateTestEvent("TestEvent2"),
                    CreateTestEvent("TestEvent3")
                ],
                new AppendToStreamOptions { ExpectedState = ExpectedStreamState.StreamExists },
                cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(ExpectedStreamState.StreamExists);
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.StreamDoesNotExist);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task
        AppendToStreamAsync_ExpectedStateIsStreamDoesNotExistAndStreamExists_ThrowsExpectedStreamToNotExist(
            CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var exception = await Client
            .AppendToStreamAsync(
                streamId,
                [CreateTestEvent("TestEvent4")],
                new AppendToStreamOptions { ExpectedState = ExpectedStreamState.StreamDoesNotExist },
                cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(ExpectedStreamState.StreamDoesNotExist);
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.StreamExists(new StreamVersion(2)));
    }

    [TestCaseSource(nameof(PositionAndDirectionEdgeCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_EdgeCasePositionAndDirectionAndStreamExists_ReturnsEmpty(
        StreamReadPosition position,
        StreamReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(
            streamId,
            [CreateTestEvent("TestEvent1"), CreateTestEvent("TestEvent2")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await Client
            .ReadStreamAsync(streamId, options, cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(PositionAndDirectionEdgeCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_EdgeCasePositionAndDirectionAndStreamDoesNotExist_ReturnsEmpty(
        StreamReadPosition position,
        StreamReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await Client
            .ReadStreamAsync(streamId, options, cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(PositionAndDirectionCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_StreamDoesNotExistAndBehaviorIsThrow_ThrowsStreamNotFound(
        StreamReadPosition position,
        StreamReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction,
            StreamNotFoundBehavior = StreamNotFoundBehavior.Throw
        };

        await Client
            .ReadStreamAsync(streamId, options, cancellationToken)
            // Stream must be materialized.
            .ToArrayAsync(cancellationToken)
            .AsTask()
            .ShouldThrowAsync<StreamNotFoundException>();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_FromVersion_ReturnsExpectedEvents(CancellationToken cancellationToken)
    {
        IDomainEvent[] events1 =
        [
            CreateTestEvent("TestEvent1").Event,
            CreateTestEvent("TestEvent2").Event,
            CreateTestEvent("TestEvent3").Event
        ];

        IDomainEvent[] events2 =
        [
            CreateTestEvent("TestEvent4").Event,
            CreateTestEvent("TestEvent5").Event,
            CreateTestEvent("TestEvent6").Event
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(streamId, events1, cancellationToken: cancellationToken);
        await Client.AppendToStreamAsync(streamId, events2, cancellationToken: cancellationToken);

        var expected = events1.Concat(events2).ToArray();

        // Forward: At(v) == expected.Skip(v)
        for (var v = 0; v < expected.Length; v++)
        {
            var actual = await ReadEvents(v, StreamReadDirection.Forward);
            actual.ShouldBe(expected.Skip(v));
        }

        // Backward: At(v) == expected.Take(v+1).Reverse()
        for (var v = expected.Length - 1; v >= 0; v--)
        {
            var actual = await ReadEvents(v, StreamReadDirection.Backward);
            actual.ShouldBe(expected.Take(v + 1).Reverse());
        }

        ValueTask<IDomainEvent[]> ReadEvents(int startVersion, StreamReadDirection direction)
        {
            return Client
                .ReadStreamAsync(
                    streamId,
                    new ReadStreamOptions
                    {
                        Position = StreamReadPosition.At(StreamVersion.FromInt64(startVersion)),
                        Direction = direction
                    },
                    cancellationToken)
                .Unwrap()
                .ToArrayAsync(cancellationToken);
        }
    }

    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_SingleAppend_MeasureLatency(CancellationToken ct)
    {
        const int warmupIterations = 10;
        const int iterations = 100;

        for (var i = 0; i < warmupIterations; i++)
            await AppendAsync("warmup", ct);

        var latencies = new List<double>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            var streamId = $"test-{Guid.NewGuid():N}";
            var sw = Stopwatch.StartNew();
            await AppendAsync(streamId, ct);
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var sorted = latencies.OrderBy(x => x).ToList();
        await TestContext.Out.WriteLineAsync($"p50:  {sorted[Percentile(0.50)]:F1} ms");
        await TestContext.Out.WriteLineAsync($"p90:  {sorted[Percentile(0.90)]:F1} ms");
        await TestContext.Out.WriteLineAsync($"p99:  {sorted[Percentile(0.99)]:F1} ms");
        await TestContext.Out.WriteLineAsync($"min:  {sorted[0]:F1} ms");
        await TestContext.Out.WriteLineAsync($"max:  {sorted[^1]:F1} ms");
        await TestContext.Out.WriteLineAsync($"mean: {latencies.Average():F1} ms");

        return;

        int Percentile(double p) => (int)Math.Ceiling(sorted.Count * p) - 1;
    }

    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_MeasureThroughputCeiling(CancellationToken ct)
    {
        const int maxInFlight = 1000;
        const int warmUpSeconds = 3;
        const int measureSeconds = 15;

        var semaphore = new SemaphoreSlim(maxInFlight, maxInFlight);
        var ops = 0;
        var errors = 0;
        var isInMeasureWindow = new StrongBox<bool>(false);

        var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(TimeSpan.FromSeconds(warmUpSeconds + measureSeconds));

        var pendingTasks = new ConcurrentBag<Task>();

        var producerLoopTask = Task.Run(
            async () =>
            {
                using (runCts)
                {
                    while (!runCts.IsCancellationRequested)
                    {
                        try
                        {
                            await semaphore.WaitAsync(runCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        var isMeasuring = isInMeasureWindow.Value;

                        var task = AppendAsync($"test-{Guid.NewGuid():N}", runCts.Token).ContinueWith(
                            t =>
                            {
                                semaphore.Release();

                                if (t.IsCompletedSuccessfully)
                                {
                                    if (isMeasuring)
                                        Interlocked.Increment(ref ops);
                                }
                                else if (t.IsFaulted)
                                {
                                    Interlocked.Increment(ref errors);
                                }
                            },
                            TaskScheduler.Default);

                        pendingTasks.Add(task);
                    }
                }
            },
            ct);

        // Warm-up
        await Task.Delay(TimeSpan.FromSeconds(warmUpSeconds), ct);

        // Measure
        isInMeasureWindow.Value = true;
        var start = Stopwatch.GetTimestamp();
        await Task.Delay(TimeSpan.FromSeconds(measureSeconds), ct);

        // Stop
        isInMeasureWindow.Value = false;
        var elapsed = Stopwatch.GetElapsedTime(start);
        await producerLoopTask;
        await Task.WhenAll(pendingTasks);

        var throughput = ops / elapsed.TotalSeconds;

        await TestContext.Out.WriteLineAsync($"max in-flight: {maxInFlight}");
        await TestContext.Out.WriteLineAsync($"ops measured:  {ops}");
        await TestContext.Out.WriteLineAsync($"errors:        {errors}");
        await TestContext.Out.WriteLineAsync($"elapsed:       {elapsed.TotalMilliseconds:F0} ms");
        await TestContext.Out.WriteLineAsync($"throughput:    {throughput:F0} ops/sec");
    }

    protected static AppendEvent<IDomainEvent> CreateTestEvent(string value)
    {
        return new AppendEvent<IDomainEvent>(new TestEvent { Value = value });
    }

    private async Task AppendAsync(string streamId, CancellationToken ct)
    {
        AppendEvent<IDomainEvent>[] events = [CreateTestEvent("Benchmark")];

        var options = new AppendToStreamOptions
        {
            ExpectedState = ExpectedStreamState.Any,
            CommitId = Guid.CreateVersion7()
        };

        await Client.AppendToStreamAsync(streamId, events, options, ct);
    }

    protected interface IDomainEvent;

    protected record TestEvent : IDomainEvent
    {
        public required string Value { get; init; }
    }
}