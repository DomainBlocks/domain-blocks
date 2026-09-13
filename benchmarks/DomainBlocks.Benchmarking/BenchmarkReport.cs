using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using HdrHistogram;
using NUnit.Framework;

namespace DomainBlocks.Benchmarking;

/// <summary>
/// Writes benchmark results in a fixed layout, preceded by environment detail needed to interpret the results.
/// </summary>
public static class BenchmarkReport
{
    private static readonly (string Label, double Percentile)[] Percentiles =
    [
        ("min:", 0),
        ("p50:", 50),
        ("p90:", 90),
        ("p99:", 99),
        ("p99.9:", 99.9)
    ];

    /// <summary>
    /// Writes the environment header. <paramref name="systemUnderTest"/> is the measured assembly's type.
    /// </summary>
    public static async Task WriteEnvironmentAsync(Type systemUnderTest, string? description = null)
    {
        var output = TestContext.Out;
        var warnings = new List<string>();

        await output.WriteLineAsync("--- environment ---");
        await WriteEnvironmentDetailsAsync(output, systemUnderTest, warnings);

        if (description is not null)
            await output.WriteLineAsync($"sut:           {description}");

        await WriteWarningsAsync(output, warnings);
    }

    public static async Task WriteLatencyAsync(string title, LatencyResult result)
    {
        var output = TestContext.Out;
        var latencies = result.Latencies;
        var totalSeconds = result.Duration.TotalSeconds;

        await output.WriteLineAsync($"--- {title} ---");
        await output.WriteLineAsync($"samples:       {latencies.TotalCount:N0} in {totalSeconds:F1} s");
        await output.WriteLineAsync($"errors:        {result.Errors:N0}");
        await WritePercentilesAsync(output, latencies);
        await output.WriteLineAsync($"gc:            {result.Gc}");
    }

    public static async Task WriteThroughputAsync(string title, ThroughputResult result)
    {
        var output = TestContext.Out;
        var expectedMeanMillis = result.InFlight / result.OpsPerSecond * 1_000;
        var measuredMeanMillis = Millis(result.Latencies.GetMean());

        await output.WriteLineAsync($"--- {title} ---");
        await output.WriteLineAsync($"in-flight:     {result.InFlight:N0}");
        await output.WriteLineAsync($"completed:     {result.Completed:N0} in {result.Duration.TotalSeconds:F1} s");
        await output.WriteLineAsync($"errors:        {result.Errors:N0}");
        await output.WriteLineAsync($"throughput:    {result.OpsPerSecond:N0} ops/s");
        await WritePerSecondSummaryAsync(output, result);

        await output.WriteLineAsync("latency under load:");
        await WritePercentilesAsync(output, result.Latencies);

        await output.WriteLineAsync(
            $"little's law:  in-flight / throughput = {expectedMeanMillis:F3} ms, " +
            $"measured mean {measuredMeanMillis:F3} ms");

        await output.WriteLineAsync($"gc:            {result.Gc}");
    }

    private static async Task WriteEnvironmentDetailsAsync(
        TextWriter output,
        Type systemUnderTest,
        List<string> warnings)
    {
        var systemAssembly = systemUnderTest.Assembly;
        var systemBuild = DescribeBuild(systemAssembly, warnings);
        var harnessBuild = DescribeBuild(typeof(BenchmarkReport).Assembly, warnings);
        var os = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
        var gcMode = GCSettings.IsServerGC ? "server" : "workstation";
        var timerResolution = Stopwatch.IsHighResolution ? "" : " (low resolution)";
        var debuggerAttached = Debugger.IsAttached;

        await output.WriteLineAsync($"os:            {os}");
        await output.WriteLineAsync($"cpu:           {Environment.ProcessorCount} logical processors");
        await output.WriteLineAsync($"runtime:       {RuntimeInformation.FrameworkDescription}");
        await output.WriteLineAsync($"gc:            {gcMode}, latency mode {GCSettings.LatencyMode}");
        await output.WriteLineAsync($"timer:         {Stopwatch.Frequency:N0} Hz{timerResolution}");
        await output.WriteLineAsync($"sut build:     {systemBuild} ({systemAssembly.GetName().Name})");
        await output.WriteLineAsync($"harness build: {harnessBuild}");
        await output.WriteLineAsync($"debugger:      {(debuggerAttached ? "attached" : "not attached")}");

        if (debuggerAttached)
            warnings.Add("a debugger is attached");
    }

    private static async Task WritePerSecondSummaryAsync(TextWriter output, ThroughputResult result)
    {
        var perSecond = result.PerSecondOps;

        if (perSecond.Count == 0)
            return;

        var mean = perSecond.Average();
        var stdDev = Math.Sqrt(perSecond.Sum(value => (value - mean) * (value - mean)) / perSecond.Count);
        var coefficientOfVariation = mean == 0 ? 0 : stdDev / mean;
        var values = string.Join(", ", perSecond.Select(value => value.ToString("N0")));

        await output.WriteLineAsync(
            $"per second:    min {perSecond.Min():N0}, max {perSecond.Max():N0}, " +
            $"cv {coefficientOfVariation:P1} [{values}]");
    }

    private static async Task WritePercentilesAsync(TextWriter output, HistogramBase latencies)
    {
        var mean = Millis(latencies.GetMean());
        var standardDeviation = Millis(latencies.GetStdDeviation());

        await output.WriteLineAsync($"mean:          {mean:F3} ms (stddev {standardDeviation:F3} ms)");

        foreach (var (label, percentile) in Percentiles)
        {
            var milliseconds = Millis(latencies.GetValueAtPercentile(percentile));
            await output.WriteLineAsync(FormatLatency(label, milliseconds));
        }

        await output.WriteLineAsync(FormatLatency("max:", Millis(latencies.GetMaxValue())));
    }

    private static async Task WriteWarningsAsync(TextWriter output, List<string> warnings)
    {
        foreach (var warning in warnings)
            await output.WriteLineAsync($"WARNING: {warning}; results are not representative");
    }

    private static string FormatLatency(string label, double milliseconds) => $"{label,-15}{milliseconds:F3} ms";

    /// <summary>
    /// Converts a histogram value recorded in <see cref="Stopwatch"/> ticks to milliseconds.
    /// </summary>
    private static double Millis(double stopwatchTicks) => stopwatchTicks / OutputScalingFactor.TimeStampToMilliseconds;

    private static string DescribeBuild(Assembly assembly, List<string> warnings)
    {
        var debuggable = assembly.GetCustomAttribute<DebuggableAttribute>();
        var optimized = debuggable is null || !debuggable.IsJITOptimizerDisabled;

        if (!optimized)
        {
            warnings.Add(
                $"{assembly.GetName().Name} is a Debug build (JIT optimizations disabled); " +
                "build with -c Release");
        }

        return optimized ? "optimized" : "DEBUG";
    }
}