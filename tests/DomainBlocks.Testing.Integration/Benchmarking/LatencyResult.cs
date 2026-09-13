using HdrHistogram;

namespace DomainBlocks.Testing.Integration.Benchmarking;

public sealed record LatencyResult(HistogramBase Latencies, TimeSpan Duration, int Errors, GcSnapshot Gc);