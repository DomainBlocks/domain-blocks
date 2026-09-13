namespace DomainBlocks.Testing.Integration.Benchmarking;

public sealed record ThroughputResult(
    int InFlight,
    long Completed,
    TimeSpan Duration,
    IReadOnlyList<double> PerSecondOps,
    LatencyHistogram Latencies,
    int Errors,
    GcSnapshot Gc)
{
    public double OpsPerSecond => Completed / Duration.TotalSeconds;
}