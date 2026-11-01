using HdrHistogram;

namespace DomainBlocks.Benchmarking;

public sealed record LatencyResult(HistogramBase Latencies, TimeSpan Duration, int Errors, GcSnapshot Gc);