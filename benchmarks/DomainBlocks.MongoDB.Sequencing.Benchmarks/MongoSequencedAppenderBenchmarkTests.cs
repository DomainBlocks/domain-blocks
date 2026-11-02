using DomainBlocks.Benchmarking;
using DomainBlocks.Testing.Integration.MongoDB;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.MongoDB.Sequencing.Benchmarks;

/// <summary>
/// Append benchmarks for <see cref="MongoSequencedAppender{TDocument,TContext}"/>, with one document per append.
/// Results are printed to the test output. A test fails only if an append fails.
/// </summary>
[Category("Benchmark")]
public class MongoSequencedAppenderBenchmarkTests
{
    /// <summary>
    /// Measures the latency of a single append with no concurrent appends.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task AppendAsync_MeasureLatency(CancellationToken ct)
    {
        var options = new MongoSequencedAppenderOptions();

        await using var scenario = await AppenderBenchmarkScenario.CreateAsync(
            MongoTestEnvironment.ConnectionString,
            instanceCount: 1,
            options);

        var result = await new BenchmarkRunner().MeasureLatencyAsync(scenario.AppendAsync, new LatencyOptions(), ct);

        await WriteEnvironmentAsync(options);
        await BenchmarkReport.WriteLatencyAsync("append latency, 1 in flight", result);

        AssertSucceeded(result);
    }

    /// <summary>
    /// Measures append throughput with a fixed number of closed-loop workers spread over one or more appender
    /// instances.
    /// </summary>
    [TestCase(1, 1)]
    [TestCase(1, 10)]
    [TestCase(1, 100)]
    [TestCase(1, 1_000)]
    [TestCase(3, 1_000)]
    [TestCase(5, 1_000)]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task AppendAsync_MeasureThroughput(int instanceCount, int inFlight, CancellationToken ct)
    {
        var options = new MongoSequencedAppenderOptions
        {
            MaxBatchSize = 1_000,
            BatchingDelay = TimeSpan.FromMilliseconds(5)
        };

        await using var scenario = await AppenderBenchmarkScenario.CreateAsync(
            MongoTestEnvironment.ConnectionString,
            instanceCount,
            options);

        var result = await new BenchmarkRunner().MeasureThroughputAsync(
            scenario.AppendAsync,
            new ThroughputOptions { InFlight = inFlight },
            ct);

        await WriteEnvironmentAsync(options);

        await BenchmarkReport.WriteThroughputAsync(
            $"append throughput, {instanceCount} instance(s), {inFlight:N0} in flight",
            result);

        AssertSucceeded(result);
    }

    /// <summary>
    /// Measures the latency of a single append with no concurrent appends, through a proxy that delays the server's
    /// responses.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task AppendAsync_WithNetworkLatency_MeasureLatency(CancellationToken ct)
    {
        var options = new MongoSequencedAppenderOptions
        {
            BatchingDelay = TimeSpan.FromMilliseconds(10),
            BatchingDelayMinCount = 2
        };

        await using var proxy = await MongoLatencyProxy.StartAsync(latencyMillis: 5, jitterMillis: 3);

        await using var scenario = await AppenderBenchmarkScenario.CreateAsync(
            proxy.ConnectionString,
            instanceCount: 1,
            options);

        var result = await new BenchmarkRunner().MeasureLatencyAsync(
            scenario.AppendAsync,
            new LatencyOptions
            {
                WarmUp = TimeSpan.FromSeconds(1),
                MinWarmUpOperations = 10,
                SampleCount = 1_000,
                MaxDuration = TimeSpan.FromSeconds(30)
            },
            ct);

        await WriteEnvironmentAsync(options, proxy);

        await BenchmarkReport.WriteLatencyAsync(
            $"append latency, 1 in flight, network latency {proxy.Description}",
            result);

        AssertSucceeded(result);
    }

    /// <summary>
    /// Measures append throughput through a proxy that delays the server's responses.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task AppendAsync_WithNetworkLatency_MeasureThroughput(CancellationToken ct)
    {
        const int inFlight = 500;

        var options = new MongoSequencedAppenderOptions
        {
            BatchingDelay = TimeSpan.FromMilliseconds(30),
            BatchingDelayMinCount = 2
        };

        await using var proxy = await MongoLatencyProxy.StartAsync(latencyMillis: 30, jitterMillis: 5);

        await using var scenario = await AppenderBenchmarkScenario.CreateAsync(
            proxy.ConnectionString,
            instanceCount: 1,
            options);

        var result = await new BenchmarkRunner().MeasureThroughputAsync(
            scenario.AppendAsync,
            new ThroughputOptions { InFlight = inFlight },
            ct);

        await WriteEnvironmentAsync(options, proxy);

        await BenchmarkReport.WriteThroughputAsync(
            $"append throughput, 1 instance(s), {inFlight:N0} in flight, network latency {proxy.Description}",
            result);

        AssertSucceeded(result);
    }

    private static async Task WriteEnvironmentAsync(
        MongoSequencedAppenderOptions options,
        MongoLatencyProxy? proxy = null)
    {
        var networkLatency = proxy is null ? "none" : proxy.Description;

        var description =
            $"MongoSequencedAppender: batch size {options.MaxBatchSize}, queue capacity {options.QueueCapacity}, " +
            $"batching delay {options.BatchingDelay.TotalMilliseconds} ms, " +
            $"batching delay min count {options.BatchingDelayMinCount}; network latency {networkLatency}; " +
            $"server {await MongoTestEnvironment.GetServerVersionAsync()}";

        await BenchmarkReport.WriteEnvironmentAsync(typeof(MongoSequencedAppender<,>), description);
    }

    private static void AssertSucceeded(LatencyResult result)
    {
        result.Errors.ShouldBe(0, "a latency figure with failed appends is not meaningful");
        result.Latencies.TotalCount.ShouldBeGreaterThan(0);
    }

    private static void AssertSucceeded(ThroughputResult result)
    {
        result.Errors.ShouldBe(0, "a throughput figure with failed appends is not meaningful");
        result.Completed.ShouldBeGreaterThan(0);
    }
}