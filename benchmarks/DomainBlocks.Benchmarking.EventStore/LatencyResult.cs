using HdrHistogram;

namespace DomainBlocks.Benchmarking.EventStore;

public sealed record LatencyResult(HistogramBase Latencies, TimeSpan Duration, int Errors, GcSnapshot Gc);