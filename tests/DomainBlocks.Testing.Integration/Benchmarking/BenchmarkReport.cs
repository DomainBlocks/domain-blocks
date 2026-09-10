using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace DomainBlocks.Testing.Integration.Benchmarking;

/// <summary>
/// Writes benchmark results to the test output in a fixed layout, always preceded by enough environment detail for the
/// numbers to be interpreted and reproduced.
/// </summary>
public static class BenchmarkReport
{
    /// <summary>
    /// Writes the environment header. <paramref name="systemUnderTest"/> is the type whose assembly is being measured;
    /// its build configuration is reported separately from the harness's.
    /// </summary>
    public static async Task WriteEnvironmentAsync(Type systemUnderTest, string? description = null)
    {
        var output = TestContext.Out;
        var warnings = new List<string>();

        await output.WriteLineAsync("--- environment ---");
        await output.WriteLineAsync($"os:            {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        await output.WriteLineAsync($"cpu:           {Environment.ProcessorCount} logical processors");
        await output.WriteLineAsync($"runtime:       {RuntimeInformation.FrameworkDescription}");
        await output.WriteLineAsync($"gc:            {(GCSettings.IsServerGC ? "server" : "workstation")}, latency mode {GCSettings.LatencyMode}");
        await output.WriteLineAsync($"timer:         {Stopwatch.Frequency:N0} Hz{(Stopwatch.IsHighResolution ? "" : " (low resolution)")}");
        await output.WriteLineAsync($"sut build:     {DescribeBuild(systemUnderTest.Assembly, warnings)} ({systemUnderTest.Assembly.GetName().Name})");
        await output.WriteLineAsync($"harness build: {DescribeBuild(typeof(BenchmarkReport).Assembly, warnings)}");
        await output.WriteLineAsync($"debugger:      {(Debugger.IsAttached ? "attached" : "not attached")}");

        if (Debugger.IsAttached)
            warnings.Add("a debugger is attached");

        if (description is not null)
            await output.WriteLineAsync($"sut:           {description}");

        foreach (var warning in warnings)
            await output.WriteLineAsync($"WARNING: {warning}; results are not representative");
    }

    public static async Task WriteLatencyAsync(string title, LatencyResult result)
    {
        var output = TestContext.Out;

        await output.WriteLineAsync($"--- {title} ---");
        await output.WriteLineAsync($"samples:       {result.Latencies.Count:N0} in {result.Duration.TotalSeconds:F1} s");
        await output.WriteLineAsync($"errors:        {result.Errors:N0}");
        await WritePercentilesAsync(result.Latencies);
        await output.WriteLineAsync($"gc:            {result.Gc}");
    }

    public static async Task WriteThroughputAsync(string title, ThroughputResult result)
    {
        var output = TestContext.Out;
        var perSecond = result.PerSecondOps;
        var expectedMeanMillis = result.InFlight / result.OpsPerSecond * 1_000;

        await output.WriteLineAsync($"--- {title} ---");
        await output.WriteLineAsync($"in-flight:     {result.InFlight:N0}");
        await output.WriteLineAsync($"completed:     {result.Completed:N0} in {result.Duration.TotalSeconds:F1} s");
        await output.WriteLineAsync($"errors:        {result.Errors:N0}");
        await output.WriteLineAsync($"throughput:    {result.OpsPerSecond:N0} ops/s");

        if (perSecond.Count > 0)
        {
            var mean = perSecond.Average();
            var stdDev = Math.Sqrt(perSecond.Sum(x => (x - mean) * (x - mean)) / perSecond.Count);
            var cv = mean == 0 ? 0 : stdDev / mean;

            await output.WriteLineAsync(
                $"per second:    min {perSecond.Min():N0}, max {perSecond.Max():N0}, cv {cv:P1} " +
                $"[{string.Join(", ", perSecond.Select(x => x.ToString("N0")))}]");
        }

        await output.WriteLineAsync("latency under load:");
        await WritePercentilesAsync(result.Latencies);
        await output.WriteLineAsync(
            $"little's law:  in-flight / throughput = {expectedMeanMillis:F3} ms, measured mean {result.Latencies.MeanMillis:F3} ms");
        await output.WriteLineAsync($"gc:            {result.Gc}");
    }

    private static async Task WritePercentilesAsync(LatencyHistogram latencies)
    {
        var output = TestContext.Out;

        await output.WriteLineAsync($"mean:          {latencies.MeanMillis:F3} ms (stddev {latencies.StdDevMillis:F3} ms)");
        await output.WriteLineAsync($"min:           {latencies.MinMillis:F3} ms");
        await output.WriteLineAsync($"p50:           {latencies.PercentileMillis(0.50):F3} ms");
        await output.WriteLineAsync($"p90:           {latencies.PercentileMillis(0.90):F3} ms");
        await output.WriteLineAsync($"p99:           {latencies.PercentileMillis(0.99):F3} ms");
        await output.WriteLineAsync($"p99.9:         {latencies.PercentileMillis(0.999):F3} ms");
        await output.WriteLineAsync($"max:           {latencies.MaxMillis:F3} ms");
    }

    private static string DescribeBuild(Assembly assembly, List<string> warnings)
    {
        var debuggable = assembly.GetCustomAttribute<DebuggableAttribute>();
        var optimized = debuggable is null || !debuggable.IsJITOptimizerDisabled;

        if (!optimized)
            warnings.Add($"{assembly.GetName().Name} is a Debug build (JIT optimizations disabled); build with -c Release");

        return optimized ? "optimized" : "DEBUG";
    }
}
