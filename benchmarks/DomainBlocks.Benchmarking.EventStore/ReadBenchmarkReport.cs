using NUnit.Framework;

namespace DomainBlocks.Benchmarking.EventStore;

/// <summary>
/// Writes the results of reading the log in the same layout as <see cref="BenchmarkReport"/>.
/// </summary>
public static class ReadBenchmarkReport
{
    public static async Task WriteAsync(string title, ReadThroughputResult result)
    {
        var output = TestContext.Out;
        var passes = string.Join(", ", result.Passes.Select(x => $"{x.TotalMilliseconds:F0}"));

        await output.WriteLineAsync($"--- {title} ---");

        await output.WriteLineAsync(
            $"log:           {result.EventsInLog:N0} events, {result.EventsRead:N0} read per pass");

        await output.WriteLineAsync($"median pass:   {result.MedianPass.TotalMilliseconds:F1} ms");
        await output.WriteLineAsync($"throughput:    {result.EventsPerSecond:N0} events/s through the log");
        await output.WriteLineAsync($"allocated:     {result.AllocatedBytesPerEvent:F0} B per event in the log");
        await output.WriteLineAsync($"passes (ms):   {passes}");
        await output.WriteLineAsync($"gc:            {result.Gc}");
    }
}