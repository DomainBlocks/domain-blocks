using DomainBlocks.Benchmarking.EventStore;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Benchmarks;

[TestFixture]
public class MongoEventStoreBenchmarks() :
    EventStoreBenchmarks<StreamPosition, LogPosition>(new MongoEventStoreTestHarness())
{
    private static readonly EventTypeMap EventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

    /// <summary>
    /// Append-to-observe latency for a live subscription: each sample appends one event and waits for it to arrive.
    /// The change stream wakes the tail, which then reads the event, so a sample includes that read.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_MeasureLiveLatency(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore(EventTypeMap);

        await using var enumerator = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);

        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);

        var runner = new AppendBenchmarkRunner();

        var result = await runner.MeasureLatencyAsync(
            async (_, streamId, token) =>
            {
                await eventStore.AppendAsync(streamId, [new TestEvent { Value = "Benchmark" }],
                    cancellationToken: token);

                (await enumerator.MoveNextAsync()).ShouldBeTrue();
                enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.Event);
            },
            new LatencyOptions
            {
                WarmUp = TimeSpan.FromSeconds(1),
                MinWarmUpOperations = 10,
                SampleCount = 1_000,
                MaxDuration = TimeSpan.FromSeconds(30)
            },
            ct);

        await BenchmarkReport.WriteEnvironmentAsync(eventStore.GetType(), await Harness.DescribeAsync());
        await BenchmarkReport.WriteLatencyAsync("append-to-observe latency, live SubscribeToAll, 1 in flight", result);

        result.Errors.ShouldBe(0);
        result.Latencies.TotalCount.ShouldBeGreaterThan(0);
    }
}