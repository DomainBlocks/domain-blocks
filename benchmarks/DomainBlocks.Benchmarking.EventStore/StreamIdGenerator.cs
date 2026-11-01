namespace DomainBlocks.Benchmarking.EventStore;

/// <summary>
/// Generates stream IDs that are unique across runs, so that each append takes the store's "new stream" path during
/// both warm-up and measurement.
/// </summary>
public sealed class StreamIdGenerator
{
    private readonly string _runId = $"bench-{Guid.NewGuid():N}";
    private long _nextNumber;

    public string Next() => $"{_runId}-{Interlocked.Increment(ref _nextNumber)}";
}