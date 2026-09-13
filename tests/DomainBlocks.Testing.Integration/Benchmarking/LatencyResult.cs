namespace DomainBlocks.Testing.Integration.Benchmarking;

public sealed record LatencyResult(LatencyHistogram Latencies, TimeSpan Duration, int Errors, GcSnapshot Gc);