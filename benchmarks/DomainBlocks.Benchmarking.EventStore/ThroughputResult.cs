using HdrHistogram;

namespace DomainBlocks.Benchmarking.EventStore;

public sealed record ThroughputResult(
    int InFlight,
    long Completed,
    TimeSpan Duration,
    IReadOnlyList<double> PerSecondOps,
    HistogramBase Latencies,
    int Errors,
    GcSnapshot Gc)
{
    public double OpsPerSecond => Completed / Duration.TotalSeconds;
}